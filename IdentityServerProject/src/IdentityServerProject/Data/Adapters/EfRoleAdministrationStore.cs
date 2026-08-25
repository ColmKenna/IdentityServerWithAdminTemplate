using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IdentityServerProject.Services;
using IdentityServerProject.Services.Roles;
using IdentityServerProject.Services.Validation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

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
        string? filter,
        Pagination pagination = default,
        CancellationToken cancellationToken = default)
    {
        pagination = pagination.Normalize();

        var query = ApplyFilter(_dbContext.Roles.AsNoTracking(), filter);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(r => r.Name)
            .Skip(pagination.Skip)
            .Take(pagination.PageSize)
            .Select(r => new RoleListItem
            {
                Id = r.Id,
                Name = r.Name ?? string.Empty,
                IsProtected = r.Name == "SysAdmin"
            })
            .ToListAsync(cancellationToken);

        return new ListResult<RoleListItem>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = pagination.PageNumber,
            PageSize = pagination.PageSize,
        };
    }

    public async Task<RoleDetailsModel?> FindRoleAsync(string roleId, CancellationToken cancellationToken = default)
    {
        var role = await _dbContext.Roles.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == roleId, cancellationToken);

        if (role == null) return null;

        return new RoleDetailsModel
        {
            Id = role.Id,
            Name = role.Name ?? string.Empty,
            IsProtected = role.Name == "SysAdmin"
        };
    }

    public async Task<(RoleCreateOutcome Status, string? RoleId, string? ErrorMessage)> CreateRoleAsync(RoleCreateInputModel input, CancellationToken cancellationToken = default)
    {
        var outcome = (Status: RoleCreateOutcome.NameCollision, RoleId: (string?)null, ErrorMessage: (string?)null);

        var strategy = _dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            _dbContext.ChangeTracker.Clear();

            var existingRole = await _roleManager.FindByNameAsync(input.Name);
            if (existingRole != null)
            {
                outcome = (RoleCreateOutcome.NameCollision, null, null);
                return;
            }

            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

            var newRole = new IdentityRole(input.Name);
            var result = await _roleManager.CreateAsync(newRole);

            if (!result.Succeeded)
            {
                await transaction.RollbackAsync(cancellationToken);
                var errorMessage = string.Join(" ", result.Errors.Select(e => e.Description));
                outcome = (RoleCreateOutcome.ValidationFailed, null, errorMessage);
                return;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            outcome = (RoleCreateOutcome.Succeeded, newRole.Id, null);
        });

        return outcome;
    }

    public async Task<(RoleDeleteOutcome Status, string TargetName)> DeleteRoleAsync(string roleId, string protectedRoleName, CancellationToken cancellationToken = default)
    {
        var outcome = (Status: RoleDeleteOutcome.RoleNotFound, TargetName: roleId);

        var strategy = _dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            _dbContext.ChangeTracker.Clear();
            outcome = (RoleDeleteOutcome.RoleNotFound, roleId);

            var role = await _roleManager.FindByIdAsync(roleId);
            if (role == null)
            {
                return;
            }

            var targetName = role.Name ?? role.Id;

            if (string.Equals(role.Name, protectedRoleName, StringComparison.OrdinalIgnoreCase))
            {
                outcome = (RoleDeleteOutcome.ProtectedRoleBlocked, targetName);
                return;
            }

            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

            var result = await _roleManager.DeleteAsync(role);
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
        if (string.IsNullOrWhiteSpace(filter))
        {
            return query;
        }

        var escaped = LikeExtensions.EscapeLikePattern(filter.Trim().ToUpperInvariant());
        var pattern = $"%{escaped}%";

        return query.Where(r => r.NormalizedName != null && EF.Functions.Like(r.NormalizedName, pattern));
    }
}
