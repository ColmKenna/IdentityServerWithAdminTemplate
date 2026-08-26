namespace IdentityServerProject.Services;

/// <summary>
/// A free-text filter paired with a page request. Repeated identically across every admin list
/// endpoint (Users, Clients, Roles, ApiScopes, Apis, IdentityResources) as two loose parameters —
/// bound together here so the pair travels as one value from Razor page to store.
/// </summary>
public sealed record ListQuery(string? Filter, Pagination Pagination = default);
