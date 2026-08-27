using IdentityServerProject.Services.Apis;
using IdentityServerProject.Services.ApiScopes;
using IdentityServerProject.Services.AuditLogs;
using IdentityServerProject.Services.Clients;
using IdentityServerProject.Services.Diagnostics;
using IdentityServerProject.Services.Grants;
using IdentityServerProject.Services.IdentityResources;
using IdentityServerProject.Services.Scopes;
using IdentityServerProject.Services.SecretReveals;
using IdentityServerProject.Services.Users;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers every IdentityServer admin-console service the Razor Pages host consumes. The host is
/// still responsible for registering an implementation of each persistence port this library
/// defines (<see cref="IIdentityUserAdministrationStore"/>, <see cref="IAdminAuditStore"/>,
/// <see cref="ISecretRevealStore"/>, <see cref="IIdentityDiagnosticsStore"/>) before resolving any
/// of these services from the container.
/// </summary>
public static class IdentityServerAdminServicesCollectionExtensions
{
    public static IServiceCollection AddIdentityServerAdminServices(this IServiceCollection services)
    {
        services.AddSingleton<ReservedClaimTypePolicy>();

        services.AddScoped<IApiResourceListService, ApiResourceListService>();
        services.AddScoped<IApiResourceEditorService, ApiResourceEditorService>();
        services.AddScoped<IScopeUsageService, ScopeUsageService>();
        services.AddScoped<IApiScopeListService, ApiScopeListService>();
        services.AddScoped<IApiScopeEditorService, ApiScopeEditorService>();
        services.AddScoped<IClientListService, ClientListService>();
        services.AddSingleton<IClientPresetService, ClientPresetService>();
        services.AddScoped<IClientDetailsService, ClientDetailsService>();
        services.AddScoped<IClientCreateService, ClientCreateService>();
        services.AddScoped<IGrantListService, GrantListService>();
        services.AddScoped<IIdentityResourceListService, IdentityResourceListService>();
        services.AddScoped<IIdentityResourceEditorService, IdentityResourceEditorService>();
        services.AddScoped<IUserListService, UserListService>();
        services.AddScoped<IUserCreateService, UserCreateService>();
        services.AddScoped<IUserDetailsService, UserDetailsService>();
        services.AddScoped<IAuditLogListService, AuditLogListService>();
        services.AddScoped<IAuditWriter, AuditWriter>();
        services.AddScoped<IDiagnosticsService, DiagnosticsService>();
        services.AddScoped<ISecretRevealService, SecretRevealService>();
        services.AddScoped<IdentityServerProject.Services.Roles.IRoleService, IdentityServerProject.Services.Roles.RoleService>();

        return services;
    }
}
