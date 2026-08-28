using System.Data;
using IdentityServerProject.Services.SecretReveals;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace IdentityServerProject.Data.Adapters;

/// <summary>
///     EF-backed <see cref=\"ISecretRevealStore" /> adapter against <see cref=\"ApplicationDbContext\" />.
///     Owns every SQL-Server-specific concern moved out of the (now host-agnostic) library service:
///     digest-collision detection on insert, and locked, serializable-isolation match-and-consume.
/// </summary>
public sealed class EfSecretRevealStore : ISecretRevealStore
{
    private readonly ApplicationDbContext _dbContext;

    public EfSecretRevealStore(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<SecretRevealInsertStatus> TryInsertAsync(
        byte[] handleDigest,
        SecretSecurityContext securityContext,
        string protectedPayload,
        DateTimeOffset createdUtc,
        DateTimeOffset expiresUtc,
        CancellationToken cancellationToken = default)
    {
        string actorSubjectIdStr = securityContext.ActorSubjectId.Value ?? string.Empty;
        _dbContext.SecretRevealRecords.Add(new SecretRevealRecord
        {
            HandleDigest = handleDigest,
            ActorSubjectId = actorSubjectIdStr,
            Purpose = securityContext.Purpose.ToString(),
            TargetId = securityContext.TargetId,
            ProtectedPayload = protectedPayload,
            CreatedUtc = createdUtc,
            ExpiresUtc = expiresUtc
        });

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return SecretRevealInsertStatus.Inserted;
        }
        catch (DbUpdateException ex) when (IsDigestCollision(ex))
        {
            _dbContext.ChangeTracker.Clear();
            return SecretRevealInsertStatus.DigestCollision;
        }
    }

    public async Task<SecretRevealLookup> ConsumeAsync(
        byte[] handleDigest,
        SecretSecurityContext securityContext,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        string actorSubjectIdStr = securityContext.ActorSubjectId.Value ?? string.Empty;
        string purposeStr = securityContext.Purpose.ToString();
        string targetId = securityContext.TargetId;
        var lookup = SecretRevealLookup.NotFound();

        IExecutionStrategy strategy = _dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            // A retry must not reuse entities tracked from the failed attempt.
            _dbContext.ChangeTracker.Clear();
            lookup = SecretRevealLookup.NotFound();

            await using IDbContextTransaction transaction = await _dbContext.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            SecretRevealRecord? record = await LoadForConsumeAsync(handleDigest, cancellationToken);
            if (record is null)
            {
                await transaction.CommitAsync(cancellationToken);
                return;
            }

            if (!string.Equals(record.ActorSubjectId, actorSubjectIdStr, StringComparison.Ordinal)
                || !string.Equals(record.Purpose, purposeStr, StringComparison.Ordinal)
                || !string.Equals(record.TargetId, targetId, StringComparison.Ordinal))
            {
                lookup = SecretRevealLookup.WrongContext();
                await transaction.RollbackAsync(cancellationToken);
                return;
            }

            if (record.ExpiresUtc <= now)
            {
                lookup = SecretRevealLookup.Expired();
                _dbContext.SecretRevealRecords.Remove(record);
                await _dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return;
            }

            lookup = SecretRevealLookup.Revealed(record.ProtectedPayload);
            _dbContext.SecretRevealRecords.Remove(record);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });

        return lookup;
    }

    public async Task CleanupExpiredAsync(DateTimeOffset now, int batchSize,
        CancellationToken cancellationToken = default)
    {
        if (batchSize <= 0) throw new ArgumentOutOfRangeException(nameof(batchSize), "Batch size must be positive.");

        // Deleting in bounded chunks limits log-flush and locking pressure on SQL Server.
        List<long> expiredIds = await _dbContext.SecretRevealRecords
            .Where(r => r.ExpiresUtc <= now)
            .OrderBy(r => r.ExpiresUtc)
            .Select(r => r.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        if (expiredIds.Count == 0) return;

        await _dbContext.SecretRevealRecords
            .Where(r => expiredIds.Contains(r.Id))
            .ExecuteDeleteAsync(cancellationToken);
    }

    private async Task<SecretRevealRecord?> LoadForConsumeAsync(byte[] handleDigest,
        CancellationToken cancellationToken)
    {
        // On SQL Server, take an exclusive row-level lock so concurrent consumers serialize behind
        // the first transaction. On SQLite/in-memory test providers, fall back to standard LINQ.
        if (_dbContext.Database.IsSqlServer())
            return await _dbContext.SecretRevealRecords
                .FromSqlInterpolated(
                    $"SELECT * FROM dbo.SecretRevealRecords WITH (UPDLOCK, ROWLOCK) WHERE HandleDigest = {handleDigest}")
                .SingleOrDefaultAsync(cancellationToken);

        return await _dbContext.SecretRevealRecords
            .SingleOrDefaultAsync(r => r.HandleDigest == handleDigest, cancellationToken);
    }

    private static bool IsDigestCollision(DbUpdateException ex)
    {
        if (ex.InnerException is SqlException sqlEx && (sqlEx.Number == 2627 || sqlEx.Number == 2601)) return true;

        // SQLite unique constraint error code
        string message = ex.InnerException?.Message ?? ex.Message;
        return message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase)
               || message.Contains("DuplicateKeyException", StringComparison.OrdinalIgnoreCase);
    }
}