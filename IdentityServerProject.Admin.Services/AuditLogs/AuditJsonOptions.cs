using System.Text.Json;
using System.Text.Json.Serialization;

namespace IdentityServerProject.Services.AuditLogs;

internal static class AuditJsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}