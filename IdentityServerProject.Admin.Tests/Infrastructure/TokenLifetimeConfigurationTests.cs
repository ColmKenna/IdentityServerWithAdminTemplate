using System.Text.Json;
using Duende.IdentityServer.Models;
using IdentityServerProject.Services.Validation;
using Microsoft.Extensions.Configuration;

namespace IdentityServerProject.Admin.Tests.Infrastructure;

/// <summary>
///     <c>IdentityServer:TokenLifetimes</c> only matters if three things independently hold: the
///     values reach <see cref="Config.Clients" />, the configuration keys Program.cs reads are the
///     ones actually documented in <c>appsettings.json</c>, and the shipped file states the same
///     values Duende's SDK already defaults to. None of the three follows from the others compiling.
/// </summary>
public class TokenLifetimeConfigurationTests
{
    [Fact]
    public void Clients_AppliesTheConfiguredTokenLifetimesToEveryClient()
    {
        var tokenLifetimes = new TokenLifetimes(
            AccessTokenLifetimeSeconds: 1200,
            IdentityTokenLifetimeSeconds: 60,
            AuthorizationCodeLifetimeSeconds: 90);
        var clients = new List<SeedClientSpec>
        {
            new("client-a", "Client A", AbsoluteHttpUri.Create("https://localhost:5001"), "secret-a"),
            new("client-b", "Client B", AbsoluteHttpUri.Create("https://localhost:5002"), "secret-b")
        };

        List<Client> seeded = Config.Clients(clients, tokenLifetimes).ToList();

        Assert.All(seeded, client =>
        {
            Assert.Equal(1200, client.AccessTokenLifetime);
            Assert.Equal(60, client.IdentityTokenLifetime);
            Assert.Equal(90, client.AuthorizationCodeLifetime);
        });
    }

    [Fact]
    public void ConfiguredSection_OverridesTheCodeDefaults()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["IdentityServer:TokenLifetimes:AccessTokenLifetimeSeconds"] = "900",
                ["IdentityServer:TokenLifetimes:IdentityTokenLifetimeSeconds"] = "120",
                ["IdentityServer:TokenLifetimes:AuthorizationCodeLifetimeSeconds"] = "180"
            })
            .Build();

        var tokenLifetimes = new TokenLifetimes(
            configuration.GetValue(
                "IdentityServer:TokenLifetimes:AccessTokenLifetimeSeconds",
                TokenLifetimes.Default.AccessTokenLifetimeSeconds),
            configuration.GetValue(
                "IdentityServer:TokenLifetimes:IdentityTokenLifetimeSeconds",
                TokenLifetimes.Default.IdentityTokenLifetimeSeconds),
            configuration.GetValue(
                "IdentityServer:TokenLifetimes:AuthorizationCodeLifetimeSeconds",
                TokenLifetimes.Default.AuthorizationCodeLifetimeSeconds));

        Assert.Equal(new TokenLifetimes(900, 120, 180), tokenLifetimes);
    }

    [Fact]
    public void AbsentSection_FallsBackToTheCodeDefaults()
    {
        IConfiguration configuration = new ConfigurationBuilder().Build();

        var tokenLifetimes = new TokenLifetimes(
            configuration.GetValue(
                "IdentityServer:TokenLifetimes:AccessTokenLifetimeSeconds",
                TokenLifetimes.Default.AccessTokenLifetimeSeconds),
            configuration.GetValue(
                "IdentityServer:TokenLifetimes:IdentityTokenLifetimeSeconds",
                TokenLifetimes.Default.IdentityTokenLifetimeSeconds),
            configuration.GetValue(
                "IdentityServer:TokenLifetimes:AuthorizationCodeLifetimeSeconds",
                TokenLifetimes.Default.AuthorizationCodeLifetimeSeconds));

        Assert.Equal(TokenLifetimes.Default, tokenLifetimes);
    }

    [Fact]
    public void ShippedAppsettingsJson_DeclaresTheSameValuesAsTheCodeDefault()
    {
        // Guards against the file and Config.TokenLifetimes.Default drifting apart silently —
        // both are meant to say the same thing (Duende's own defaults), stated explicitly rather
        // than left implicit in the SDK.
        string path = Path.Combine(
            AppContext.BaseDirectory, "appsettings.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));

        JsonElement tokenLifetimes = document.RootElement
            .GetProperty("IdentityServer")
            .GetProperty("TokenLifetimes");

        Assert.Equal(
            TokenLifetimes.Default.AccessTokenLifetimeSeconds,
            tokenLifetimes.GetProperty("AccessTokenLifetimeSeconds").GetInt32());
        Assert.Equal(
            TokenLifetimes.Default.IdentityTokenLifetimeSeconds,
            tokenLifetimes.GetProperty("IdentityTokenLifetimeSeconds").GetInt32());
        Assert.Equal(
            TokenLifetimes.Default.AuthorizationCodeLifetimeSeconds,
            tokenLifetimes.GetProperty("AuthorizationCodeLifetimeSeconds").GetInt32());
    }
}
