using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Identity;

namespace IdentityServerProject.Services.Users;

public enum UserUnlockStatus
{
    Succeeded,
    NotFound,
    Failed,
}

public sealed record UserUnlockResult(UserUnlockStatus Status, IReadOnlyList<string> Errors)
{
    public static UserUnlockResult Succeeded { get; } = new(UserUnlockStatus.Succeeded, Array.Empty<string>());

    public static UserUnlockResult NotFound { get; } = new(UserUnlockStatus.NotFound, Array.Empty<string>());

    public static UserUnlockResult Failed(IdentityResult result) => new(
        UserUnlockStatus.Failed,
        result.Errors
            .Select(error => error.Description)
            .Where(description => !string.IsNullOrWhiteSpace(description))
            .ToArray());
}
