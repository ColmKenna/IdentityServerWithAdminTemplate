namespace IdentityServerProject.Services.Roles;

public class RoleListItem
{
    public required RoleId Id { get; set; }
    public required string Name { get; set; }
    public bool IsProtected { get; set; }
}

public class RoleDetailsModel
{
    public required RoleId Id { get; set; }
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
        ListQuery query,
        CancellationToken cancellationToken = default);

    Task<RoleDetailsModel?> FindRoleAsync(
        RoleId roleId, CancellationToken cancellationToken = default);

    Task<(RoleCreateOutcome Status, RoleId? RoleId, string? ErrorMessage)> CreateRoleAsync(
        RoleCreateInputModel input, CancellationToken cancellationToken = default);

    Task<(RoleDeleteOutcome Status, string TargetName)> DeleteRoleAsync(
        RoleId roleId, string protectedRoleName, CancellationToken cancellationToken = default);
}
