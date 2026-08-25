using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
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
    internal static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
    private const int CleanupBatchSize = 100;
    private const int HandleGenerationAttempts = 3;

    private readonly ISecretRevealStore _store;
    private readonly IDataProtector _protector;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly TimeProvider _timeProvider;
    private readonly IAuditWriter _auditWriter;
    private readonly ILogger<SecretRevealService> _logger;

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
        SecretRevealPurpose purpose,
        string targetId,
        string plaintext,
        CancellationToken cancellationToken = default)
    {
        var normalizedTarget = NormalizeTarget(targetId);
        ValidatePurpose(purpose);
        if (normalizedTarget.Length == 0)
        {
            await AuditAsync(AuditActions.Issue, AuditOutcome.Denied, AuditReasonCodes.ValidationFailed,
                targetId, "Secret reveal target is invalid.", cancellationToken);
            throw new ArgumentException("A non-empty target ID is required.", nameof(targetId));
        }

        if (string.IsNullOrEmpty(plaintext))
        {
            await AuditAsync(AuditActions.Issue, AuditOutcome.Denied, AuditReasonCodes.ValidationFailed,
                normalizedTarget, "Secret reveal payload is invalid.", cancellationToken);
            throw new ArgumentException("A non-empty plaintext value is required.", nameof(plaintext));
        }

        var actorSubjectId = ResolveActorSubjectId();
        if (actorSubjectId.IsEmpty)
        {
            await AuditAsync(AuditActions.Issue, AuditOutcome.Denied, AuditReasonCodes.WrongContext,
                normalizedTarget, "Secret reveal issuance requires an authenticated subject.", cancellationToken);
            throw new InvalidOperationException("An authenticated subject is required to issue a secret reveal.");
        }

        var securityContext = SecretSecurityContext.Create(actorSubjectId, purpose, normalizedTarget);
        var now = _timeProvider.GetUtcNow();
        var expiresUtc = now.Add(Lifetime);
        var protectedPayload = BindingProtector(actorSubjectId.Value, purpose, normalizedTarget).Protect(plaintext);

        for (var attempt = 1; attempt <= HandleGenerationAttempts; attempt++)
        {
            var rawHandle = RandomNumberGenerator.GetBytes(32);
            var handle = WebEncoders.Base64UrlEncode(rawHandle);
            var digest = SHA256.HashData(rawHandle);

            SecretRevealInsertStatus insertStatus;
            try
            {
                insertStatus = await _store.TryInsertAsync(
                    digest, securityContext, protectedPayload, now, expiresUtc, cancellationToken);
            }
            catch (Exception ex)
            {
                await AuditAsync(AuditActions.Issue, AuditOutcome.Failed, AuditReasonCodes.PersistenceFailure,
                    normalizedTarget, $"Secret reveal issuance failed ({ex.GetType().Name}).", cancellationToken);
                throw;
            }

            if (insertStatus == SecretRevealInsertStatus.Inserted)
            {
                await AuditAsync(AuditActions.Issue, AuditOutcome.Succeeded, AuditReasonCodes.Succeeded,
                    normalizedTarget, $"Issued {purpose} secret reveal.", cancellationToken);
                await TryCleanupExpiredAsync(now, cancellationToken);
                return new SecretRevealTicket(handle, expiresUtc);
            }

            // Digest collision: loop around with a freshly generated handle.
        }

        var collisionFailure = new InvalidOperationException("Unable to allocate a secret reveal handle.");
        await AuditAsync(AuditActions.Issue, AuditOutcome.Failed, AuditReasonCodes.PersistenceFailure,
            normalizedTarget, "Secret reveal handle allocation failed.", cancellationToken);
        throw collisionFailure;
    }

    public async Task<SecretRevealConsumeResult> ConsumeAsync(
        SecretRevealPurpose purpose,
        string targetId,
        string handle,
        CancellationToken cancellationToken = default)
    {
        var normalizedTarget = NormalizeTarget(targetId);
        var actorSubjectId = ResolveActorSubjectId();
        if (!Enum.IsDefined(purpose)
            || normalizedTarget.Length == 0
            || actorSubjectId.IsEmpty
            || !TryDigestHandle(handle, out var digest))
        {
            await AuditAsync(AuditActions.Consume, AuditOutcome.Denied, AuditReasonCodes.WrongContext,
                normalizedTarget, "Secret reveal is unavailable.", cancellationToken);
            return SecretRevealConsumeResult.Unavailable();
        }

        var securityContext = SecretSecurityContext.Create(actorSubjectId, purpose, normalizedTarget);
        SecretRevealLookup lookup;
        try
        {
            lookup = await _store.ConsumeAsync(
                digest, securityContext, _timeProvider.GetUtcNow(), cancellationToken);
        }
        catch (Exception ex)
        {
            await AuditAsync(AuditActions.Consume, AuditOutcome.Failed, AuditReasonCodes.PersistenceFailure,
                normalizedTarget, $"Secret reveal consumption failed ({ex.GetType().Name}).", cancellationToken);
            throw;
        }

        if (lookup.Status != SecretRevealLookupStatus.Revealed)
        {
            var unavailableReason = lookup.Status switch
            {
                SecretRevealLookupStatus.WrongContext => AuditReasonCodes.WrongContext,
                SecretRevealLookupStatus.Expired => AuditReasonCodes.Expired,
                _ => AuditReasonCodes.NotFound,
            };
            await AuditAsync(AuditActions.Consume, AuditOutcome.Denied, unavailableReason,
                normalizedTarget, "Secret reveal is unavailable.", cancellationToken);
            return SecretRevealConsumeResult.Unavailable();
        }

        try
        {
            var plaintext = BindingProtector(actorSubjectId.Value, purpose, normalizedTarget).Unprotect(lookup.ProtectedPayload!);
            await AuditAsync(AuditActions.Consume, AuditOutcome.Succeeded, AuditReasonCodes.Succeeded,
                normalizedTarget, $"Consumed {purpose} secret reveal.", cancellationToken);
            return SecretRevealConsumeResult.Revealed(plaintext);
        }
        catch (CryptographicException)
        {
            _logger.LogWarning(
                "A consumed secret reveal payload could not be unprotected for target {TargetId}.",
                normalizedTarget);
            await AuditAsync(AuditActions.Consume, AuditOutcome.Failed, AuditReasonCodes.PersistenceFailure,
                normalizedTarget, "Consumed secret reveal payload could not be unprotected.", cancellationToken);
            return SecretRevealConsumeResult.Unavailable();
        }
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
        {
            throw new ArgumentOutOfRangeException(nameof(purpose));
        }
    }

    private static bool TryDigestHandle(string? handle, out byte[] digest)
    {
        digest = Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(handle))
        {
            return false;
        }

        try
        {
            var rawHandle = WebEncoders.Base64UrlDecode(handle);
            if (rawHandle.Length != 32)
            {
                return false;
            }

            digest = SHA256.HashData(rawHandle);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private Task AuditAsync(
        string action,
        AuditOutcome outcome,
        string reasonCode,
        string targetId,
        string details,
        CancellationToken cancellationToken) =>
        _auditWriter.WriteAsync(new AdminAuditEvent(
            AuditCategories.SecretReveal,
            action,
            outcome,
            reasonCode,
            TargetId: string.IsNullOrWhiteSpace(targetId) ? null : targetId,
            TargetName: string.IsNullOrWhiteSpace(targetId) ? null : targetId,
            Details: details), cancellationToken);
}
