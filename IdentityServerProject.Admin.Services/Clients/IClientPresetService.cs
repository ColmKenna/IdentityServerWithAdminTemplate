namespace IdentityServerProject.Services.Clients;
public interface IClientPresetService
{
    IReadOnlyList<ClientPreset> GetAvailablePresets();
    ClientPreset? GetPreset(string id);
}
