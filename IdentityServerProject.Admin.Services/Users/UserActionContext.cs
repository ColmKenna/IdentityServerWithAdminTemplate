namespace IdentityServerProject.Services.Users;

/// <summary>
///     Binds a target user to the (optional) administrator performing the action, so the two IDs
///     travel together instead of risking transposition or naming drift ("currentUserId" vs.
///     "actingUserId") between the service and store layers.
/// </summary>
public readonly record struct UserActionContext(UserId Target, UserId? ActingUser);