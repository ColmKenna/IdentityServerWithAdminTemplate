using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Duende.IdentityServer.EntityFramework.Entities;
using Client = Duende.IdentityServer.EntityFramework.Entities.Client;

namespace IdentityServerProject.Services.Clients;

public partial class ClientDetailsService
{
    private static bool IsInteractiveClient(Client entity)
    {
        var grantTypes = entity.AllowedGrantTypes.Select(g => g.GrantType).ToList();
        if (grantTypes.Count == 0)
            return true;

        return grantTypes.Any(g => g != GrantTypeClientCredentials);
    }

    private static (bool HasDrifted, string? DriftDetails) EvaluatePresetDrift(Client entity, List<string> currentGrantTypes)
    {
        var preset = entity.Properties.FirstOrDefault(p => p.Key == ClientCreateService.PresetPropertyKey)?.Value;
        if (string.IsNullOrWhiteSpace(preset))
            return (false, null);

        var (expectedPkce, expectedSecret, expectedGrantTypes) = preset switch
        {
            "m2m" => (false, true, new[] { GrantTypeClientCredentials }),
            "spa-nobff" => (true, false, new[] { GrantTypeAuthorizationCode }),
            _ => (true, true, new[] { GrantTypeAuthorizationCode })
        };

        var mismatches = new List<string>();
        if (entity.RequirePkce != expectedPkce)
        {
            mismatches.Add($"PKCE is {(entity.RequirePkce ? "required" : "optional")}, preset '{preset}' expects {(expectedPkce ? "required" : "optional")}");
        }

        if (entity.RequireClientSecret != expectedSecret)
        {
            mismatches.Add($"Client secret is {(entity.RequireClientSecret ? "required" : "not required")}, preset '{preset}' expects {(expectedSecret ? "required" : "not required")}");
        }

        var expectedSorted = expectedGrantTypes.OrderBy(g => g, StringComparer.Ordinal).ToList();
        if (!currentGrantTypes.SequenceEqual(expectedSorted, StringComparer.Ordinal))
        {
            mismatches.Add($"Grant types are [{string.Join(", ", currentGrantTypes)}], preset '{preset}' expects [{string.Join(", ", expectedSorted)}]");
        }

        return mismatches.Count > 0
            ? (true, string.Join("; ", mismatches))
            : (false, null);
    }

    private static (bool CanDelete, string? BlockReason) EvaluateDeleteEligibility(Client entity, DateTimeOffset utcNow)
    {
        if (entity.Enabled)
            return (false, "Client must be disabled before it can be deleted.");

        var disabledAtValue = entity.Properties.FirstOrDefault(p => p.Key == DisabledAtPropertyKey)?.Value;
        if (!DateTime.TryParse(disabledAtValue, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var disabledAt))
            return (false, $"Client must be disabled for at least {MinimumDisabledDaysBeforeDelete} days before it can be deleted.");

        var daysDisabled = (utcNow.UtcDateTime - disabledAt.ToUniversalTime()).TotalDays;
        if (daysDisabled < MinimumDisabledDaysBeforeDelete)
        {
            var daysRemaining = MinimumDisabledDaysBeforeDelete - (int)Math.Floor(daysDisabled);
            return (false, $"Client has been disabled for {(int)Math.Floor(daysDisabled)} day(s). It can be deleted in {daysRemaining} more day(s) (90-day retention rule).");
        }

        return (true, null);
    }

    private static string DeriveClientType(Client entity)
    {
        var grantType = entity.AllowedGrantTypes.Select(g => g.GrantType).FirstOrDefault();

        return grantType switch
        {
            null => "SPA with BFF",
            GrantTypeAuthorizationCode => "SPA with BFF",
            GrantTypeClientCredentials => "Machine to Machine",
            GrantTypeHybrid => "Hybrid App",
            GrantTypeImplicit => "Implicit App",
            GrantTypeDeviceCode => "Device App",
            _ => Prettify(grantType)
        };
    }

    private static string Prettify(string grantType)
    {
        var words = grantType.Replace('_', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", words.Select(w => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(w)));
    }
}
