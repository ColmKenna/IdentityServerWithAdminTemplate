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

    public Task<ListResult<RoleListItem>> GetRolesAsync(
        string? filter,
        Pagination pagination = default,
        CancellationToken cancellationToken = default)
    {
        return _store.GetRolesAsync(filter, pagination, cancellationToken);
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
                    await AuditDeniedAsync(AuditAction.Create, AuditReasonCode.NameCollision, null, input.Name,
                        "A role with that name already exists.", cancellationToken);
                    return RoleCreateResult.Failed("A role with that name already exists.");

                case RoleCreateOutcome.ValidationFailed:
                    await AuditDeniedAsync(AuditAction.Create, AuditReasonCode.ValidationFailed, null, input.Name,
                        outcome.ErrorMessage ?? "Validation failed.", cancellationToken);
                    return RoleCreateResult.Failed(outcome.ErrorMessage ?? "Validation failed.");
            }

            await _auditWriter.WriteAsync(new AdminAuditEvent(
                AuditCategory.Role, AuditAction.Create, AuditOutcome.Succeeded, AuditReasonCode.Succeeded,
                TargetId: outcome.RoleId, TargetName: input.Name,
                Details: "Role created"), cancellationToken);

            return RoleCreateResult.Succeeded(outcome.RoleId!);
        }
        catch (Exception ex)
        {
            await AuditFailedAsync(AuditAction.Create, null, input.Name, ex, cancellationToken);
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
                    await AuditDeniedAsync(AuditAction.Delete, AuditReasonCode.NotFound, roleId, outcome.TargetName,
                        "Role not found.", cancellationToken);
                    return RoleDeleteResult.Failed("Role not found.");

                case RoleDeleteOutcome.ProtectedRoleBlocked:
                    await AuditDeniedAsync(AuditAction.Delete, AuditReasonCode.ProtectedResource, roleId, outcome.TargetName,
                        "Cannot delete protected role.", cancellationToken);
                    return RoleDeleteResult.Failed("You cannot delete this protected role.");

                case RoleDeleteOutcome.ValidationFailed:
                    await AuditDeniedAsync(AuditAction.Delete, AuditReasonCode.ValidationFailed, roleId, outcome.TargetName,
                        "Failed to delete role.", cancellationToken);
                    return RoleDeleteResult.Failed("Failed to delete role.");
            }

            await _auditWriter.WriteAsync(new AdminAuditEvent(
                AuditCategory.Role, AuditAction.Delete, AuditOutcome.Succeeded, AuditReasonCode.Succeeded,
                TargetId: roleId, TargetName: outcome.TargetName,
                Details: "Role deleted manually by administrator"), cancellationToken);

            return RoleDeleteResult.Succeeded();
        }
        catch (Exception ex)
        {
            await AuditFailedAsync(AuditAction.Delete, roleId, roleId, ex, cancellationToken);
            throw;
        }
    }

    private Task AuditDeniedAsync(AuditAction action, AuditReasonCode reasonCode, string? targetId, string? targetName, string details, CancellationToken cancellationToken)
        => _auditWriter.WriteAsync(new AdminAuditEvent(
            AuditCategory.Role, action, AuditOutcome.Denied, reasonCode,
            TargetId: targetId, TargetName: targetName, Details: details), cancellationToken);

    private Task AuditFailedAsync(AuditAction action, string? targetId, string? targetName, Exception ex, CancellationToken cancellationToken)
        => _auditWriter.WriteAsync(new AdminAuditEvent(
            AuditCategory.Role, action, AuditOutcome.Failed, AuditReasonCode.PersistenceFailure,
            TargetId: targetId, TargetName: targetName, Details: $"Unexpected error ({ex.GetType().Name})"), cancellationToken);
}
