using System;
using System.Threading;
using System.Threading.Tasks;
using IdentityServerProject.Services.AuditLogs;
using IdentityServerProject.Services.Validation;

namespace IdentityServerProject.Services.Roles;

public class RoleService : IRoleService
{
    private readonly IRoleAdministrationStore _store;
    private readonly IAuditWriter _auditWriter;

    // We pass SysAdminRole as the protected role that cannot be deleted.
    private const string ProtectedRoleName = "SysAdmin";

    public RoleService(
        IRoleAdministrationStore store,
        IAuditWriter auditWriter)
    {
        _store = store;
        _auditWriter = auditWriter;
    }

    public Task<ListResult<RoleListItem>> GetRolesAsync(string? filter, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
    {
        return _store.GetRolesAsync(filter, pageNumber, pageSize, cancellationToken);
    }

    public Task<RoleDetailsModel?> GetRoleAsync(string roleId, CancellationToken cancellationToken = default)
    {
        return _store.FindRoleAsync(roleId, cancellationToken);
    }

    public async Task<RoleCreateResult> CreateRoleAsync(RoleCreateInputModel input, CancellationToken cancellationToken = default)
    {
        try
        {
            var outcome = await _store.CreateRoleAsync(input, cancellationToken);
            switch (outcome.Status)
            {
                case RoleCreateOutcome.NameCollision:
                    await AuditDeniedAsync(AuditActions.Create, AuditReasonCodes.NameCollision, null, input.Name,
                        "A role with that name already exists.", cancellationToken);
                    return RoleCreateResult.Failed("A role with that name already exists.");

                case RoleCreateOutcome.ValidationFailed:
                    await AuditDeniedAsync(AuditActions.Create, AuditReasonCodes.ValidationFailed, null, input.Name,
                        outcome.ErrorMessage ?? "Validation failed.", cancellationToken);
                    return RoleCreateResult.Failed(outcome.ErrorMessage ?? "Validation failed.");
            }

            await _auditWriter.WriteAsync(new AdminAuditEvent(
                AuditCategories.Role, AuditActions.Create, AuditOutcome.Succeeded, AuditReasonCodes.Succeeded,
                TargetId: outcome.RoleId, TargetName: input.Name,
                Details: "Role created"), cancellationToken);

            return RoleCreateResult.Succeeded(outcome.RoleId!);
        }
        catch (Exception ex)
        {
            await AuditFailedAsync(AuditActions.Create, null, input.Name, ex, cancellationToken);
            throw;
        }
    }

    public async Task<RoleDeleteResult> DeleteRoleAsync(string roleId, CancellationToken cancellationToken = default)
    {
        try
        {
            var outcome = await _store.DeleteRoleAsync(roleId, ProtectedRoleName, cancellationToken);
            switch (outcome.Status)
            {
                case RoleDeleteOutcome.RoleNotFound:
                    await AuditDeniedAsync(AuditActions.Delete, AuditReasonCodes.NotFound, roleId, outcome.TargetName,
                        "Role not found.", cancellationToken);
                    return RoleDeleteResult.Failed("Role not found.");

                case RoleDeleteOutcome.ProtectedRoleBlocked:
                    await AuditDeniedAsync(AuditActions.Delete, AuditReasonCodes.ProtectedResource, roleId, outcome.TargetName,
                        "Cannot delete protected role.", cancellationToken);
                    return RoleDeleteResult.Failed("You cannot delete this protected role.");

                case RoleDeleteOutcome.ValidationFailed:
                    await AuditDeniedAsync(AuditActions.Delete, AuditReasonCodes.ValidationFailed, roleId, outcome.TargetName,
                        "Failed to delete role.", cancellationToken);
                    return RoleDeleteResult.Failed("Failed to delete role.");
            }

            await _auditWriter.WriteAsync(new AdminAuditEvent(
                AuditCategories.Role, AuditActions.Delete, AuditOutcome.Succeeded, AuditReasonCodes.Succeeded,
                TargetId: roleId, TargetName: outcome.TargetName,
                Details: "Role deleted manually by administrator"), cancellationToken);

            return RoleDeleteResult.Succeeded();
        }
        catch (Exception ex)
        {
            await AuditFailedAsync(AuditActions.Delete, roleId, roleId, ex, cancellationToken);
            throw;
        }
    }

    private Task AuditDeniedAsync(string action, string reasonCode, string? targetId, string? targetName, string details, CancellationToken cancellationToken)
        => _auditWriter.WriteAsync(new AdminAuditEvent(
            AuditCategories.Role, action, AuditOutcome.Denied, reasonCode,
            TargetId: targetId, TargetName: targetName, Details: details), cancellationToken);

    private Task AuditFailedAsync(string action, string? targetId, string? targetName, Exception ex, CancellationToken cancellationToken)
        => _auditWriter.WriteAsync(new AdminAuditEvent(
            AuditCategories.Role, action, AuditOutcome.Failed, AuditReasonCodes.PersistenceFailure,
            TargetId: targetId, TargetName: targetName, Details: $"Unexpected error ({ex.GetType().Name})"), cancellationToken);
}
