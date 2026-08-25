using System.Threading;
using System.Threading.Tasks;

namespace IdentityServerProject.Services.Users;

/// <summary>
/// Retrieves and manages ASP.NET Core Identity users for display on the Admin console.
/// </summary>
public interface IUserListService
{
    /// <summary>
    /// Returns a filtered, paged list of users ordered by username.
    /// </summary>
    /// <param name="filter">
    /// Optional case-insensitive substring matched against username, email, or full name.
    /// A null or whitespace-only value returns all users.
    /// </param>
    /// <param name="pagination">Pagination settings (page number and page size).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ListResult<UserListItem>> GetUsersAsync(
        string? filter,
        Pagination pagination = default,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Unlocks a locked-out user account by clearing their lockout end date and resetting access failed count.
    /// </summary>
    /// <param name="userId">The unique ID of the user to unlock.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result that distinguishes a missing user from an Identity operation failure.</returns>
    Task<UserUnlockResult> UnlockUserAsync(
        UserId userId,
        CancellationToken cancellationToken = default);
}
