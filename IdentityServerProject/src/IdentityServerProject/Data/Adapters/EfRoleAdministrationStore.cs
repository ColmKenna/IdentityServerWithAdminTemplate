using IdentityServerProject.Services;
using IdentityServerProject.Services.Roles;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace IdentityServerProject.Data.Adapters;

public sealed class EfRoleAdministrationStore : IRoleAdministrationStore
{
    private readonly ApplicationDbContext _dbContext;
    private readonly RoleManager<IdentityRole> _roleManager;

    public EfRoleAdministrationStore(
        ApplicationDbContext dbContext,
        RoleManager<IdentityRole> roleManager)
    {
        _dbContext = dbContext;
        _roleManager = roleManager;
    }

    public async Task<ListResult<RoleListItem>> GetRolesAsync(
        ListQuery query,
        CancellationToken cancellationToken = default)
    {
        Pagination pagination = query.Pagination.Normalize();

        IQueryable<IdentityRole> dbQuery = ApplyFilter(_dbContext.Roles.AsNoTracking(), query.Filter);

        int totalCount = await dbQuery.CountAsync(cancellationToken);

        var rows = await dbQuery
            .OrderBy(r => r.Name)
            .Skip(pagination.Skip)
            .Take(pagination.PageSize)
            .Select(r => new { r.Id, r.Name })
            .ToListAsync(cancellationToken);

        var items = rows.Select(r => new RoleListItem
        {
            Id = RoleId.Create(r.Id),
            Name = r.Name ?? string.Empty,
            IsProtected = r.Name == "SysAdmin"
        }).ToList();

        return new ListResult<RoleListItem>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = pagination.PageNumber,
            PageSize = pagination.PageSize
        };
    }

    public async Task<RoleDetailsModel?> FindRoleAsync(RoleId roleId, CancellationToken cancellationToken = default)
    {
        string roleIdStr = roleId.Value ?? string.Empty;
        IdentityRole? role = await _dbContext.Roles.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == roleIdStr, cancellationToken);

        if (role is null) return null;

        return new RoleDetailsModel
        {
            Id = RoleId.Create(role.Id),
            Name = role.Name ?? string.Empty,
            IsProtected = role.Name == "SysAdmin"
        };
    }

    public async Task<(RoleCreateOutcome Status, RoleId? RoleId, string? ErrorMessage)> CreateRoleAsync(
        RoleCreateInputModel input, CancellationToken cancellationToken = default)
    {
        (RoleCreateOutcome Status, RoleId? RoleId, string? ErrorMessage) outcome = (
            Status: RoleCreateOutcome.NameCollision, RoleId: null, ErrorMessage: null);

        IExecutionStrategy strategy = _dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            _dbContext.ChangeTracker.Clear();

            IdentityRole? existingRole = await _roleManager.FindByNameAsync(input.Name);
            if (existingRole is not null)
            {
                outcome = (RoleCreateOutcome.NameCollision, null, null);
                return;
            }

            await using IDbContextTransaction transaction =
                await _dbContext.Database.BeginTransactionAsync(cancellationToken);

            var newRole = new IdentityRole(input.Name);
            IdentityResult result = await _roleManager.CreateAsync(newRole);

            if (!result.Succeeded)
            {
                await transaction.RollbackAsync(cancellationToken);
                string errorMessage = string.Join(" ", result.Errors.Select(e => e.Description));
                outcome = (RoleCreateOutcome.ValidationFailed, null, errorMessage);
                return;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            outcome = (RoleCreateOutcome.Succeeded, RoleId.Create(newRole.Id), null);
        });

        return outcome;
    }

    public async Task<(RoleDeleteOutcome Status, string TargetName)> DeleteRoleAsync(RoleId roleId,
        string protectedRoleName, CancellationToken cancellationToken = default)
    {
        string roleIdStr = roleId.Value ?? string.Empty;
        (RoleDeleteOutcome Status, string TargetName) outcome = (Status: RoleDeleteOutcome.RoleNotFound,
            TargetName: roleIdStr);

        IExecutionStrategy strategy = _dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            _dbContext.ChangeTracker.Clear();
            outcome = (RoleDeleteOutcome.RoleNotFound, roleIdStr);

            IdentityRole? role = await _roleManager.FindByIdAsync(roleIdStr);
            if (role is null) return;

            string targetName = role.Name ?? role.Id;

            if (string.Equals(role.Name, protectedRoleName, StringComparison.OrdinalIgnoreCase))
            {
                outcome = (RoleDeleteOutcome.ProtectedRoleBlocked, targetName);
                return;
            }

            await using IDbContextTransaction transaction =
                await _dbContext.Database.BeginTransactionAsync(cancellationToken);

            IdentityResult result = await _roleManager.DeleteAsync(role);
            if (!result.Succeeded)
            {
                await transaction.RollbackAsync(cancellationToken);
                outcome = (RoleDeleteOutcome.ValidationFailed, targetName);
                return;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            outcome = (RoleDeleteOutcome.Succeeded, targetName);
        });

        return outcome;
    }

    private static IQueryable<IdentityRole> ApplyFilter(IQueryable<IdentityRole> query, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter)) return query;

        string? escaped = LikeExtensions.EscapeLikePattern(filter.Trim().ToUpperInvariant());
        string pattern = $"%{escaped}%";

        return query.Where(r => r.NormalizedName != null && EF.Functions.Like(r.NormalizedName, pattern));
    }
}