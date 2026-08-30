using IdentityServerProject.Services.Clients;
using Microsoft.Extensions.DependencyInjection;

namespace IdentityServerProject.Admin.Tests.Infrastructure;

/// <summary>
///     ClientDetailsService is surfaced through five per-concern interfaces. Nothing else asserts
///     that they share one instance, and a registration that handed out several would give each its
///     own EF ChangeTracker - a page model could then read state a sibling had not yet saved.
/// </summary>
public sealed class ClientServiceRegistrationTests : IDisposable
{
    private readonly AdminWebFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Should_ResolveOneInstance_When_AllClientInterfacesRequestedInOneScope()
    {
        await _factory.RunInScopeAsync(sp =>
        {
            var overview = sp.GetRequiredService<IClientOverviewService>();
            var authentication = sp.GetRequiredService<IClientAuthenticationService>();
            var permissions = sp.GetRequiredService<IClientPermissionsService>();
            var secrets = sp.GetRequiredService<IClientSecretsService>();
            var tokenSettings = sp.GetRequiredService<IClientTokenSettingsService>();

            Assert.Same(overview, authentication);
            Assert.Same(overview, permissions);
            Assert.Same(overview, secrets);
            Assert.Same(overview, tokenSettings);

            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Should_ResolveDifferentInstances_When_RequestedInSeparateScopes()
    {
        IClientOverviewService? first = null;

        await _factory.RunInScopeAsync(sp =>
        {
            first = sp.GetRequiredService<IClientOverviewService>();
            return Task.CompletedTask;
        });

        await _factory.RunInScopeAsync(sp =>
        {
            var second = sp.GetRequiredService<IClientOverviewService>();
            Assert.NotSame(first, second);
            return Task.CompletedTask;
        });
    }
}
