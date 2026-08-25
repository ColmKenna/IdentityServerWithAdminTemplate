using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IdentityServerProject.Services.Validation;

namespace IdentityServerProject.Services.Roles;

public class RoleListItem
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public bool IsProtected { get; set; }
}

public class RoleDetailsModel
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public bool IsProtected { get; set; }
}

public class RoleCreateInputModel
{
    public required string Name { get; set; }
}

public enum RoleCreateOutcome
{
    Succeeded,
    NameCollision,
    ValidationFailed
}

public enum RoleDeleteOutcome
{
    Succeeded,
    RoleNotFound,
    ProtectedRoleBlocked,
    ValidationFailed
}

public interface IRoleAdministrationStore
{
    Task<ListResult<RoleListItem>> GetRolesAsync(
        string? filter,
        Pagination pagination = default,
        CancellationToken cancellationToken = default);

    Task<RoleDetailsModel?> FindRoleAsync(
        string roleId, CancellationToken cancellationToken = default);

    Task<(RoleCreateOutcome Status, string? RoleId, string? ErrorMessage)> CreateRoleAsync(
        RoleCreateInputModel input, CancellationToken cancellationToken = default);

    Task<(RoleDeleteOutcome Status, string TargetName)> DeleteRoleAsync(
        string roleId, string protectedRoleName, CancellationToken cancellationToken = default);
}
