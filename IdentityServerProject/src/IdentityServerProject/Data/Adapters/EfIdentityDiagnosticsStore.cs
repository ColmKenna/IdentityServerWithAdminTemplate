using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IdentityServerProject.Services.Diagnostics;
using IdentityServerProject.Services.Users;
using Microsoft.EntityFrameworkCore;

namespace IdentityServerProject.Data.Adapters;

/// <summary>
/// EF-backed <see cref="IIdentityDiagnosticsStore"/> adapter against <see cref="ApplicationDbContext"/>.
/// </summary>
public sealed class EfIdentityDiagnosticsStore : IIdentityDiagnosticsStore
{
    private readonly ApplicationDbContext _dbContext;

    public EfIdentityDiagnosticsStore(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<bool> CanConnectAsync(CancellationToken cancellationToken = default) =>
        _dbContext.Database.CanConnectAsync(cancellationToken);

    public async Task<IReadOnlyList<string>> GetDistinctUserClaimTypesAsync(CancellationToken cancellationToken = default) =>
        await _dbContext.UserClaims
            .AsNoTracking()
            .Where(c => c.ClaimType != null)
            .Select(c => c.ClaimType!)
            .Distinct()
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ReservedClaimHolder>> GetClaimHoldersAsync(
        IReadOnlyCollection<string> claimTypes,
        CancellationToken cancellationToken = default)
    {
        // Projects into an anonymous, DB-translatable shape first: UserId's explicit string
        // conversion cannot be translated to SQL, so the typed wrapping happens client-side below.
        var rows = await (
            from claim in _dbContext.UserClaims.AsNoTracking()
            join user in _dbContext.Users.AsNoTracking() on claim.UserId equals user.Id
            where claim.ClaimType != null && claimTypes.Contains(claim.ClaimType)
            orderby user.UserName, claim.ClaimType
            select new
            {
                UserId = user.Id,
                UserName = user.UserName ?? user.Id,
                ClaimType = claim.ClaimType!,
                ClaimValue = claim.ClaimValue ?? string.Empty
            }).ToListAsync(cancellationToken);

        return rows.Select(r => new ReservedClaimHolder
        {
            UserId = UserId.Create(r.UserId),
            UserName = r.UserName,
            ClaimType = r.ClaimType,
            ClaimValue = r.ClaimValue
        }).ToList();
    }
}
