namespace IdentityServerProject.Services.Clients;

public class ClientPresetService : IClientPresetService
{
    private static readonly List<ClientPreset> _presets = new()
    {
        new ClientPreset(
            "web",
            "Web Application",
            "Server-side web application (e.g., ASP.NET Core MVC).",
            "🌐",
            true,
            true,
            ["authorization_code"],
            ["openid", "profile"]),

        new ClientPreset(
            "spa-bff",
            "Single Page App (BFF)",
            "Single page application using Backend-for-Frontend pattern.",
            "🛡️",
            true,
            true,
            ["authorization_code"],
            ["openid", "profile"]),

        new ClientPreset(
            "spa-nobff",
            "Single Page App (Browser)",
            "Single page application running entirely in the browser.",
            "💻",
            true,
            false,
            ["authorization_code"],
            ["openid", "profile"]),

        new ClientPreset(
            "m2m",
            "Machine to Machine",
            "Non-interactive application or background service.",
            "⚙️",
            false,
            true,
            ["client_credentials"],
            [])
    };

    public IReadOnlyList<ClientPreset> GetAvailablePresets() => _presets;

    public ClientPreset? GetPreset(string id) => _presets.FirstOrDefault(p => p.Id == id);
}