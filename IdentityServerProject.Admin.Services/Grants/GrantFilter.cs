using IdentityServerProject.Services.Clients;
using IdentityServerProject.Services.Users;

namespace IdentityServerProject.Services.Grants;

/// <summary>
/// Filter criteria for querying persisted grants.
/// </summary>
public sealed record GrantFilter(
    UserId? SubjectId = null,
    ClientId? ClientId = null,
    string? TypeFilter = null)
{
    public static GrantFilter Create(string? subjectId = null, string? clientId = null, string? typeFilter = null) =>
        new(
            string.IsNullOrWhiteSpace(subjectId) ? null : UserId.Create(subjectId),
            string.IsNullOrWhiteSpace(clientId) ? null : Services.Clients.ClientId.Create(clientId),
            string.IsNullOrWhiteSpace(typeFilter) ? null : typeFilter.Trim());
}
