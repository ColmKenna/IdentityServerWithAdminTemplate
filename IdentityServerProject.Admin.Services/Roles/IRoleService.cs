using System.Threading;
using System.Threading.Tasks;
using IdentityServerProject.Services.Validation;

namespace IdentityServerProject.Services.Roles;

public class RoleCreateResult
{
    public bool Success { get; set; }
    public string? RoleId { get; set; }
    public string? ErrorMessage { get; set; }

    public static RoleCreateResult Succeeded(string roleId) => new() { Success = true, RoleId = roleId };

    public static RoleCreateResult Failed(string errorMessage) => new() { Success = false, ErrorMessage = errorMessage };
}

public class RoleDeleteResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }

    public static RoleDeleteResult Succeeded() => new() { Success = true };

    public static RoleDeleteResult Failed(string errorMessage) => new() { Success = false, ErrorMessage = errorMessage };
}

public interface IRoleService
{
    Task<ListResult<RoleListItem>> GetRolesAsync(
        string? filter, int pageNumber, int pageSize, CancellationToken cancellationToken = default);

    Task<RoleDetailsModel?> GetRoleAsync(
        string roleId, CancellationToken cancellationToken = default);

    Task<RoleCreateResult> CreateRoleAsync(
        RoleCreateInputModel input, CancellationToken cancellationToken = default);

    Task<RoleDeleteResult> DeleteRoleAsync(
        string roleId, CancellationToken cancellationToken = default);
}
