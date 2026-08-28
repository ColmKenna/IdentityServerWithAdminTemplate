using Microsoft.EntityFrameworkCore;

namespace IdentityServerProject.Services.Users;

public partial class UserDetailsService
{
    public async Task<UserDetailsModel?> GetUserDetailsAsync(UserActionContext context,
        CancellationToken cancellationToken = default)
    {
        UserId userId = context.Target;
        UserId? currentUserId = context.ActingUser;
        if (userId.IsEmpty)
            return null;

        UserAccountDetails? account = await _store.FindUserDetailsAsync(userId, cancellationToken);
        if (account is null)
            return null;

        var claims = account.Claims
            .Select(c => new UserClaimSummary
            {
                Type = c.Type,
                Value = c.Value,
                IsReserved = _reservedClaimTypes.IsReserved(c.Type)
            })
            .OrderBy(c => c.Type).ThenBy(c => c.Value)
            .ToList();

        int persistedGrantCount = await _persistedGrantDbContext.PersistedGrants
            .AsNoTracking()
            .CountAsync(g => g.SubjectId == userId.Value, cancellationToken);

        return new UserDetailsModel
        {
            Id = account.Id,
            UserName = account.UserName,
            Email = account.Email,
            FullName = account.FullName,
            IsLockedOut = account.IsLockedOut,
            LockoutEnd = account.LockoutEnd,
            AssignedRoles = account.AssignedRoles.ToList(),
            AllRoles = account.AllRoles.ToList(),
            Claims = claims,
            PersistedGrantCount = persistedGrantCount,
            IsCurrentUser = currentUserId is { IsEmpty: false } && currentUserId.Value == account.Id
        };
    }
}