namespace IdentityServerProject.Services.Clients;

using System.Collections.Generic;

public interface IClientPresetService
{
    IReadOnlyList<ClientPreset> GetAvailablePresets();
    ClientPreset? GetPreset(string id);
}
