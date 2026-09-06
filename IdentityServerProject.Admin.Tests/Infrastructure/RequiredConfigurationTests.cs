using IdentityServerProject.Configuration;
using Microsoft.Extensions.Configuration;

namespace IdentityServerProject.Admin.Tests.Infrastructure;

/// <summary>
///     Seed credentials must come from configuration and never from a committed fallback.
///     These cover the rule itself; every call site in Program.cs routes through it.
/// </summary>
public class RequiredConfigurationTests
{
    [Fact]
    public void Required_ReturnsTheConfiguredValue()
    {
        IConfiguration configuration = Build(("Seed:SysAdminPassword", "a-configured-password"));

        Assert.Equal("a-configured-password", configuration.Required("Seed:SysAdminPassword"));
    }

    [Fact]
    public void Required_WhenTheKeyIsAbsent_ThrowsNamingTheKey()
    {
        IConfiguration configuration = Build();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => configuration.Required("Seed:SysAdminPassword"));

        Assert.Contains("Seed:SysAdminPassword", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Required_WhenTheValueIsBlank_ThrowsNamingTheKey(string configured)
    {
        IConfiguration configuration = Build(("Seed:SysAdminPassword", configured));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => configuration.Required("Seed:SysAdminPassword"));

        Assert.Contains("Seed:SysAdminPassword", exception.Message, StringComparison.Ordinal);
    }

    private static IConfiguration Build(params (string Key, string Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.ToDictionary(s => s.Key, s => (string?)s.Value))
            .Build();
}
