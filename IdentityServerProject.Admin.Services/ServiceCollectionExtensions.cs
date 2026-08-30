using IdentityServerProject.Services.Apis;
using IdentityServerProject.Services.ApiScopes;
using IdentityServerProject.Services.AuditLogs;
using IdentityServerProject.Services.Clients;
using IdentityServerProject.Services.Diagnostics;
using IdentityServerProject.Services.Grants;
using IdentityServerProject.Services.IdentityResources;
using IdentityServerProject.Services.Roles;
using IdentityServerProject.Services.Scopes;
using IdentityServerProject.Services.SecretReveals;
using IdentityServerProject.Services.Users;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///     Registers every IdentityServer admin-console service the Razor Pages host consumes. The host is
///     still responsible for registering an implementation of each persistence port this library
///     defines (<see cref="IIdentityUserAdministrationStore" />, <see cref="IAdminAuditStore" />,
///     <see cref="ISecretRevealStore" />, <see cref="IIdentityDiagnosticsStore" />) before resolving any
///     of these services from the container.
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
        // The five per-concern interfaces resolve *through* IClientDetailsService, not through the
        // concrete type. That keeps one instance (and one EF ChangeTracker) per request, and it means
        // a test that substitutes IClientDetailsService still intercepts every page model.
        services.AddScoped<IClientDetailsService, ClientDetailsService>();
        services.AddScoped<IClientOverviewService>(sp => sp.GetRequiredService<IClientDetailsService>());
        services.AddScoped<IClientAuthenticationService>(sp => sp.GetRequiredService<IClientDetailsService>());
        services.AddScoped<IClientPermissionsService>(sp => sp.GetRequiredService<IClientDetailsService>());
        services.AddScoped<IClientSecretsService>(sp => sp.GetRequiredService<IClientDetailsService>());
        services.AddScoped<IClientTokenSettingsService>(sp => sp.GetRequiredService<IClientDetailsService>());
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
        services.AddScoped<IRoleService, RoleService>();

        return services;
    }
}