namespace IdentityServerProject.Services.Clients;
public class ClientPresetService : IClientPresetService
{
    private static readonly List<ClientPreset> _presets = new()
    {
        new ClientPreset(
            Id: "web",
            Name: "Web Application",
            Description: "Server-side web application (e.g., ASP.NET Core MVC).",
            Icon: "🌐",
            RequirePkce: true,
            RequireClientSecret: true,
            GrantTypes: new List<string> { "authorization_code" },
            AllowedScopes: new List<string> { "openid", "profile" }),

        new ClientPreset(
            Id: "spa-bff",
            Name: "Single Page App (BFF)",
            Description: "Single page application using Backend-for-Frontend pattern.",
            Icon: "🛡️",
            RequirePkce: true,
            RequireClientSecret: true,
            GrantTypes: new List<string> { "authorization_code" },
            AllowedScopes: new List<string> { "openid", "profile" }),

        new ClientPreset(
            Id: "spa-nobff",
            Name: "Single Page App (Browser)",
            Description: "Single page application running entirely in the browser.",
            Icon: "💻",
            RequirePkce: true,
            RequireClientSecret: false,
            GrantTypes: new List<string> { "authorization_code" },
            AllowedScopes: new List<string> { "openid", "profile" }),

        new ClientPreset(
            Id: "m2m",
            Name: "Machine to Machine",
            Description: "Non-interactive application or background service.",
            Icon: "⚙️",
            RequirePkce: false,
            RequireClientSecret: true,
            GrantTypes: new List<string> { "client_credentials" },
            AllowedScopes: new List<string>())
    };

    public IReadOnlyList<ClientPreset> GetAvailablePresets()
    {
        return _presets;
    }

    public ClientPreset? GetPreset(string id)
    {
        return _presets.FirstOrDefault(p => p.Id == id);
    }
}
