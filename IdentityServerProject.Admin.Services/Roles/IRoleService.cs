using IdentityServerProject.Services.Validation;

namespace IdentityServerProject.Services.Roles;

public class RoleCreateResult
{
    public bool Success { get; set; }
    public RoleId? RoleId { get; set; }
    public string? ErrorMessage { get; set; }

    public static RoleCreateResult Succeeded(RoleId roleId) => new() { Success = true, RoleId = roleId };

    public static RoleCreateResult Failed(string errorMessage) =>
        new() { Success = false, ErrorMessage = errorMessage };
}

public interface IRoleService
{
    Task<ListResult<RoleListItem>> GetRolesAsync(
        ListQuery query,
        CancellationToken cancellationToken = default);

    Task<RoleDetailsModel?> GetRoleAsync(
        RoleId roleId, CancellationToken cancellationToken = default);

    Task<RoleCreateResult> CreateRoleAsync(
        RoleCreateInputModel input, CancellationToken cancellationToken = default);

    Task<AdminMutationResult> DeleteRoleAsync(
        RoleId roleId, CancellationToken cancellationToken = default);
}