using System.Security.Claims;

namespace IdentityServerProject.Services.AuditLogs;

public static class AuditActorResolver
{
    public static (string SubjectId, string Name) Resolve(ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated != true)
            return (string.Empty, string.Empty);

        string subjectId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                           ?? user.FindFirst("sub")?.Value
                           ?? string.Empty;

        string name = user.Identity.Name ?? string.Empty;

        return (subjectId, name);
    }
}