using System;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IdentityServerProject.Services.SecretReveals;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace IdentityServerProject.Data.Adapters;

/// <summary>
/// EF-backed <see cref="ISecretRevealStore"/> adapter against <see cref="ApplicationDbContext"/>.
/// Owns every SQL-Server-specific concern moved out of the (now host-agnostic) library service:
/// digest-collision detection on insert, and locked, serializable-isolation match-and-consume.
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
        string actorSubjectId,
        string purpose,
        string targetId,
        string protectedPayload,
        DateTimeOffset createdUtc,
        DateTimeOffset expiresUtc,
        CancellationToken cancellationToken = default)
    {
        _dbContext.SecretRevealRecords.Add(new SecretRevealRecord
        {
            HandleDigest = handleDigest,
            ActorSubjectId = actorSubjectId,
            Purpose = purpose,
            TargetId = targetId,
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
        string actorSubjectId,
        string purpose,
        string targetId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var lookup = SecretRevealLookup.NotFound();

        var strategy = _dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            // A retry must not reuse entities tracked from the failed attempt.
            _dbContext.ChangeTracker.Clear();
            lookup = SecretRevealLookup.NotFound();

            await using var transaction = await _dbContext.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            var record = await LoadForConsumeAsync(handleDigest, cancellationToken);
            if (record == null)
            {
                await transaction.CommitAsync(cancellationToken);
                return;
            }

            if (!string.Equals(record.ActorSubjectId, actorSubjectId, StringComparison.Ordinal)
                || !string.Equals(record.Purpose, purpose, StringComparison.Ordinal)
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

    public async Task CleanupExpiredAsync(DateTimeOffset now, int batchSize, CancellationToken cancellationToken = default)
    {
        var expiredIds = await _dbContext.SecretRevealRecords
            .Where(record => record.ExpiresUtc <= now)
            .OrderBy(record => record.Id)
            .Select(record => record.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        if (expiredIds.Count > 0)
        {
            await _dbContext.SecretRevealRecords
                .Where(record => expiredIds.Contains(record.Id))
                .ExecuteDeleteAsync(cancellationToken);
        }
    }

    private async Task<SecretRevealRecord?> LoadForConsumeAsync(byte[] digest, CancellationToken cancellationToken)
    {
        if (_dbContext.Database.IsSqlServer())
        {
            return await _dbContext.SecretRevealRecords
                .FromSqlInterpolated($"""
                    SELECT TOP(1) *
                    FROM [SecretRevealRecords] WITH (UPDLOCK, HOLDLOCK)
                    WHERE [HandleDigest] = {digest}
                    """)
                .SingleOrDefaultAsync(cancellationToken);
        }

        return await _dbContext.SecretRevealRecords
            .SingleOrDefaultAsync(record => record.HandleDigest == digest, cancellationToken);
    }

    private static bool IsDigestCollision(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 2601 or 2627 };
}
