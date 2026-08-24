namespace IdentityServerProject.Services.Clients;

using System.Collections.Generic;

public record ClientPreset(
    string Id,
    string Name,
    string Description,
    string Icon,
    bool RequirePkce,
    bool RequireClientSecret,
    IReadOnlyList<string> GrantTypes,
    IReadOnlyList<string> AllowedScopes);
