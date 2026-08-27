namespace IdentityServerProject.Services.Clients;

public record ClientPreset(
    string Id,
    string Name,
    string Description,
    string Icon,
    bool RequirePkce,
    bool RequireClientSecret,
    IReadOnlyList<string> GrantTypes,
    IReadOnlyList<string> AllowedScopes);