using System.Security.Cryptography;
using IdentityServerProject.Services.AuditLogs;
using IdentityServerProject.Services.Users;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;

namespace IdentityServerProject.Services.SecretReveals;

public sealed class SecretRevealService : ISecretRevealService
{
    internal const string ProtectorPurpose = "IdentityServerProject.Admin.SecretReveal.v1";
    private const int CleanupBatchSize = 100;
    private const int HandleGenerationAttempts = 3;
    internal static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
    private readonly IAuditWriter _auditWriter;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<SecretRevealService> _logger;
    private readonly IDataProtector _protector;

    private readonly ISecretRevealStore _store;
    private readonly TimeProvider _timeProvider;

    public SecretRevealService(
        ISecretRevealStore store,
        IDataProtectionProvider dataProtectionProvider,
        IHttpContextAccessor httpContextAccessor,
        TimeProvider timeProvider,
        IAuditWriter auditWriter,
        ILogger<SecretRevealService> logger)
    {
        _store = store;
        _protector = dataProtectionProvider.CreateProtector(ProtectorPurpose);
        _httpContextAccessor = httpContextAccessor;
        _timeProvider = timeProvider;
        _auditWriter = auditWriter;
        _logger = logger;
    }

    public async Task<SecretRevealTicket> IssueAsync(
        SecretRevealTarget target,
        string plaintext,
        CancellationToken cancellationToken = default)
    {
        SecretRevealPurpose purpose = target.Purpose;
        string targetId = target.TargetId;
        string normalizedTarget = NormalizeTarget(targetId);
        ValidatePurpose(purpose);
        if (normalizedTarget.Length == 0)
            await ThrowMissingTargetIdAsync(targetId, cancellationToken);

        if (string.IsNullOrEmpty(plaintext))
            await ThrowMissingPlaintextAsync(normalizedTarget, cancellationToken);

        UserId actorSubjectId = ResolveActorSubjectId();
        if (actorSubjectId.IsEmpty)
            await ThrowUnauthenticatedSubjectAsync(normalizedTarget, cancellationToken);

        var securityContext = SecretSecurityContext.Create(actorSubjectId, purpose, normalizedTarget);
        DateTimeOffset now = _timeProvider.GetUtcNow();
        DateTimeOffset expiresUtc = now.Add(Lifetime);
        string protectedPayload = BindingProtector(actorSubjectId.Value, purpose, normalizedTarget).Protect(plaintext);

        for (int attempt = 1; attempt <= HandleGenerationAttempts; attempt++)
        {
            byte[] rawHandle = RandomNumberGenerator.GetBytes(32);
            string handle = WebEncoders.Base64UrlEncode(rawHandle);
            byte[] digest = SHA256.HashData(rawHandle);

            SecretRevealInsertStatus insertStatus;
            try
            {
                insertStatus = await _store.TryInsertAsync(
                    digest, securityContext, protectedPayload, now, expiresUtc, cancellationToken);
            }
            catch (Exception ex)
            {
                await AuditAsync(AuditAction.Issue, AuditOutcome.Failed, AuditReasonCode.PersistenceFailure,
                    normalizedTarget, $"Secret reveal issuance failed ({ex.GetType().Name}).", cancellationToken);
                throw;
            }

            if (insertStatus == SecretRevealInsertStatus.Inserted)
            {
                await AuditAsync(AuditAction.Issue, AuditOutcome.Succeeded, AuditReasonCode.Succeeded,
                    normalizedTarget, $"Issued {purpose} secret reveal.", cancellationToken);
                await TryCleanupExpiredAsync(now, cancellationToken);
                return new SecretRevealTicket(SecretRevealHandle.Create(handle), expiresUtc);
            }

            // Digest collision: loop around with a freshly generated handle.
        }

        var collisionFailure = new InvalidOperationException("Unable to allocate a secret reveal handle.");
        await AuditAsync(AuditAction.Issue, AuditOutcome.Failed, AuditReasonCode.PersistenceFailure,
            normalizedTarget, "Secret reveal handle allocation failed.", cancellationToken);
        throw collisionFailure;
    }

    private async Task ThrowMissingTargetIdAsync(string targetId, CancellationToken cancellationToken)
    {
        await AuditAsync(AuditAction.Issue, AuditOutcome.Denied, AuditReasonCode.ValidationFailed,
            targetId, "Secret reveal target is invalid.", cancellationToken);
        throw new ArgumentException("A non-empty target ID is required.", nameof(targetId));
    }

    private async Task ThrowMissingPlaintextAsync(string normalizedTarget, CancellationToken cancellationToken)
    {
        await AuditAsync(AuditAction.Issue, AuditOutcome.Denied, AuditReasonCode.ValidationFailed,
            normalizedTarget, "Secret reveal payload is invalid.", cancellationToken);
        throw new ArgumentException("A non-empty plaintext value is required.", "plaintext");
    }

    private async Task ThrowUnauthenticatedSubjectAsync(string normalizedTarget, CancellationToken cancellationToken)
    {
        await AuditAsync(AuditAction.Issue, AuditOutcome.Denied, AuditReasonCode.WrongContext,
            normalizedTarget, "Secret reveal issuance requires an authenticated subject.", cancellationToken);
        throw new InvalidOperationException("An authenticated subject is required to issue a secret reveal.");
    }

    public async Task<SecretRevealConsumeResult> ConsumeAsync(
        SecretRevealTarget target,
        SecretRevealHandle handle,
        CancellationToken cancellationToken = default)
    {
        SecretRevealPurpose purpose = target.Purpose;
        string targetId = target.TargetId;
        string normalizedTarget = NormalizeTarget(targetId);
        UserId actorSubjectId = ResolveActorSubjectId();
        if (!Enum.IsDefined(purpose)
            || normalizedTarget.Length == 0
            || actorSubjectId.IsEmpty
            || !TryDigestHandle(handle.Value, out byte[] digest))
            return await BuildConsumeUnavailableResultAsync(normalizedTarget, cancellationToken);

        var securityContext = SecretSecurityContext.Create(actorSubjectId, purpose, normalizedTarget);
        SecretRevealLookup lookup;
        try
        {
            lookup = await _store.ConsumeAsync(
                digest, securityContext, _timeProvider.GetUtcNow(), cancellationToken);
        }
        catch (Exception ex)
        {
            await AuditAsync(AuditAction.Consume, AuditOutcome.Failed, AuditReasonCode.PersistenceFailure,
                normalizedTarget, $"Secret reveal consumption failed ({ex.GetType().Name}).", cancellationToken);
            throw;
        }

        if (lookup.Status != SecretRevealLookupStatus.Revealed)
            return await BuildLookupUnavailableResultAsync(lookup, normalizedTarget, cancellationToken);

        try
        {
            string plaintext = BindingProtector(actorSubjectId.Value, purpose, normalizedTarget)
                .Unprotect(lookup.ProtectedPayload!);
            await AuditAsync(AuditAction.Consume, AuditOutcome.Succeeded, AuditReasonCode.Succeeded,
                normalizedTarget, $"Consumed {purpose} secret reveal.", cancellationToken);
            return SecretRevealConsumeResult.Revealed(plaintext);
        }
        catch (CryptographicException)
        {
            _logger.LogWarning(
                "A consumed secret reveal payload could not be unprotected for target {TargetId}.",
                normalizedTarget);
            await AuditAsync(AuditAction.Consume, AuditOutcome.Failed, AuditReasonCode.PersistenceFailure,
                normalizedTarget, "Consumed secret reveal payload could not be unprotected.", cancellationToken);
            return SecretRevealConsumeResult.Unavailable();
        }
    }

    private async Task<SecretRevealConsumeResult> BuildConsumeUnavailableResultAsync(string normalizedTarget, CancellationToken cancellationToken)
    {
        await AuditAsync(AuditAction.Consume, AuditOutcome.Denied, AuditReasonCode.WrongContext,
            normalizedTarget, "Secret reveal is unavailable.", cancellationToken);
        return SecretRevealConsumeResult.Unavailable();
    }

    private async Task<SecretRevealConsumeResult> BuildLookupUnavailableResultAsync(
        SecretRevealLookup lookup, string normalizedTarget, CancellationToken cancellationToken)
    {
        AuditReasonCode unavailableReason = lookup.Status switch
        {
            SecretRevealLookupStatus.WrongContext => AuditReasonCode.WrongContext,
            SecretRevealLookupStatus.Expired => AuditReasonCode.Expired,
            _ => AuditReasonCode.NotFound
        };
        await AuditAsync(AuditAction.Consume, AuditOutcome.Denied, unavailableReason,
            normalizedTarget, "Secret reveal is unavailable.", cancellationToken);
        return SecretRevealConsumeResult.Unavailable();
    }

    private async Task TryCleanupExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            await _store.CleanupExpiredAsync(now, CleanupBatchSize, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Expired secret reveal cleanup failed; issuance remains committed.");
        }
    }

    private IDataProtector BindingProtector(
        string actorSubjectId,
        SecretRevealPurpose purpose,
        string targetId) =>
        _protector.CreateProtector(actorSubjectId, purpose.ToString(), targetId);

    private UserId ResolveActorSubjectId() =>
        UserId.Create(AuditActorResolver.Resolve(_httpContextAccessor.HttpContext?.User).SubjectId.Trim());

    private static string NormalizeTarget(string? targetId) => targetId?.Trim() ?? string.Empty;

    private static void ValidatePurpose(SecretRevealPurpose purpose)
    {
        if (!Enum.IsDefined(purpose))
            throw new ArgumentOutOfRangeException(nameof(purpose));
    }

    private static bool TryDigestHandle(string? handle, out byte[] digest)
    {
        digest = Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(handle))
            return false;

        try
        {
            byte[] rawHandle = WebEncoders.Base64UrlDecode(handle);
            if (rawHandle.Length != 32)
                return false;

            digest = SHA256.HashData(rawHandle);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private Task AuditAsync(
        AuditAction action,
        AuditOutcome outcome,
        AuditReasonCode reasonCode,
        string targetId,
        string details,
        CancellationToken cancellationToken) =>
        _auditWriter.WriteAsync(new AdminAuditEvent(
            AuditCategory.SecretReveal,
            action,
            outcome,
            reasonCode,
            string.IsNullOrWhiteSpace(targetId) ? null : targetId,
            string.IsNullOrWhiteSpace(targetId) ? null : targetId,
            Details: details), cancellationToken);
}