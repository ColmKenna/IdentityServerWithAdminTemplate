using Duende.IdentityServer.EntityFramework.Options;
using Microsoft.Extensions.DependencyInjection;

namespace IdentityServerProject.Admin.Tests.Infrastructure;

/// <summary>
///     Expired grants have to be removed by the host or PersistedGrants grows without bound.
///     The assertion is on the instance the container hands out, not on the one the builder
///     callback configured: Program.cs registers a second, bare OperationalStoreOptions for
///     the EF design-time tools, and a later singleton registration is what resolves.
/// </summary>
public class TokenCleanupTests : IClassFixture<AdminWebFactory>
{
    private readonly AdminWebFactory _factory;

    public TokenCleanupTests(AdminWebFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void TheResolvedOperationalStoreOptionsEnableCleanup()
    {
        OperationalStoreOptions options = _factory.Services.GetRequiredService<OperationalStoreOptions>();

        Assert.True(options.EnableTokenCleanup);
        Assert.Equal(3600, options.TokenCleanupInterval);
    }

    [Fact]
    public void EveryRegisteredOperationalStoreOptionsAgrees()
    {
        // Duplicate registrations are the hazard here: one configured instance and one bare
        // one resolve differently depending on which the consumer asks for, and the bare one
        // silently disables cleanup. Whichever is picked has to say the same thing.
        OperationalStoreOptions[] registered =
            _factory.Services.GetServices<OperationalStoreOptions>().ToArray();

        Assert.NotEmpty(registered);
        Assert.All(registered, options => Assert.True(options.EnableTokenCleanup));
    }
}
