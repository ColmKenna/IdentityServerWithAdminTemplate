using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Entities;
using Duende.IdentityServer.EntityFramework.Mappers;
using IdentityServerProject.Admin.Tests.Infrastructure;
using IdentityServerProject.Services.Clients;
using IdentityServerProject.Services.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Client = Duende.IdentityServer.Models.Client;
using IdentityResource = Duende.IdentityServer.Models.IdentityResource;

namespace IdentityServerProject.Admin.Tests.Clients;

public class ClientCreateServiceTests : IClassFixture<AdminWebFactory>
{
    private readonly AdminWebFactory _factory;

    public ClientCreateServiceTests(AdminWebFactory factory)
    {
        _factory = factory;
    }

    private async Task SeedIdentityScopesAsync()
    {
        await _factory.RunInScopeAsync(async sp =>
        {
            ConfigurationDbContext db = sp.GetRequiredService<ConfigurationDbContext>();
            if (!await db.IdentityResources.AnyAsync(resource => resource.Name == "openid"))
            {
                db.IdentityResources.Add(new IdentityResource("openid", new[] { "sub" }).ToEntity());
                db.IdentityResources.Add(new IdentityResource("profile", new[] { "name" }).ToEntity());
                await db.SaveChangesAsync();
            }
        });
    }

    [Fact]
    public async Task Should_CreateClient_WithPresetDefaults_AndGenerateHashedSecret()
    {
        await SeedIdentityScopesAsync();
        string clientId = $"test-web-{Guid.NewGuid():N}";
        var input = new ClientCreateInputModel
        {
            ClientId = clientId,
            ClientName = "Test Web Client",
            Description = "Web Application Preset Test",
            SelectedPreset = "web",
            RequirePkce = true,
            RequireClientSecret = true,
            GrantTypes = new List<string> { "authorization_code" },
            RedirectUris = new List<string> { "https://localhost:5001/signin-oidc" },
            PostLogoutRedirectUris = new List<string> { "https://localhost:5001/signout-callback-oidc" },
            CorsOrigins = new List<string>(),
            AllowedScopes = new List<string> { "openid", "profile" }
        };

        ClientCreateResult result = default!;
        await _factory.RunInScopeAsync(async sp =>
        {
            IClientCreateService service = sp.GetRequiredService<IClientCreateService>();
            result = await service.CreateClientAsync(input);
        });

        Assert.True(result.Success);
        Assert.Equal(clientId, result.ClientId);
        Assert.False(string.IsNullOrWhiteSpace(result.PlaintextSecret));

        // Verify entity in database
        await _factory.RunInScopeAsync(async sp =>
        {
            ConfigurationDbContext db = sp.GetRequiredService<ConfigurationDbContext>();
            Duende.IdentityServer.EntityFramework.Entities.Client? client = await db.Clients
                .Include(c => c.AllowedGrantTypes)
                .Include(c => c.RedirectUris)
                .Include(c => c.PostLogoutRedirectUris)
                .Include(c => c.ClientSecrets)
                .Include(c => c.AllowedScopes)
                .FirstOrDefaultAsync(c => c.ClientId == clientId);

            Assert.NotNull(client);
            Assert.Equal("Test Web Client", client.ClientName);
            Assert.True(client.RequirePkce);
            Assert.True(client.RequireClientSecret);
            Assert.Contains(client.AllowedGrantTypes, g => g.GrantType == "authorization_code");
            Assert.Contains(client.RedirectUris, r => r.RedirectUri == "https://localhost:5001/signin-oidc");
            Assert.Contains(client.AllowedScopes, s => s.Scope == "openid");

            Assert.Single(client.ClientSecrets);
            // Secret in DB must be hashed, not plaintext
            Assert.NotEqual(result.PlaintextSecret, client.ClientSecrets.First().Value);
        });
    }

    [Fact]
    public async Task Should_FailCreation_When_ClientIdAlreadyExists()
    {
        string clientId = $"dup-client-{Guid.NewGuid():N}";
        var input = new ClientCreateInputModel
        {
            ClientId = clientId,
            ClientName = "Initial Client",
            SelectedPreset = "m2m",
            RequirePkce = false,
            RequireClientSecret = true,
            GrantTypes = new List<string> { "client_credentials" }
        };

        await _factory.RunInScopeAsync(async sp =>
        {
            IClientCreateService service = sp.GetRequiredService<IClientCreateService>();
            ClientCreateResult initialResult = await service.CreateClientAsync(input);
            Assert.True(initialResult.Success);
        });

        // Attempt duplicate creation
        ClientCreateResult dupResult = default!;
        await _factory.RunInScopeAsync(async sp =>
        {
            IClientCreateService service = sp.GetRequiredService<IClientCreateService>();
            dupResult = await service.CreateClientAsync(input);
        });

        Assert.False(dupResult.Success);
        Assert.Contains("already exists", dupResult.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Should_CreatePublicClient_WithoutSecret_When_RequireSecretIsFalse()
    {
        await SeedIdentityScopesAsync();
        string clientId = $"test-spa-{Guid.NewGuid():N}";
        var input = new ClientCreateInputModel
        {
            ClientId = clientId,
            ClientName = "SPA Client Public",
            SelectedPreset = "spa-nobff",
            RequirePkce = true,
            RequireClientSecret = false,
            GrantTypes = new List<string> { "authorization_code" },
            RedirectUris = new List<string> { "https://localhost:3000/callback" },
            CorsOrigins = new List<string> { "https://localhost:3000" }
        };

        ClientCreateResult result = default!;
        await _factory.RunInScopeAsync(async sp =>
        {
            IClientCreateService service = sp.GetRequiredService<IClientCreateService>();
            result = await service.CreateClientAsync(input);
        });

        Assert.True(result.Success);
        Assert.Null(result.PlaintextSecret);

        await _factory.RunInScopeAsync(async sp =>
        {
            ConfigurationDbContext db = sp.GetRequiredService<ConfigurationDbContext>();
            Duende.IdentityServer.EntityFramework.Entities.Client? client = await db.Clients
                .Include(c => c.ClientSecrets)
                .Include(c => c.AllowedCorsOrigins)
                .FirstOrDefaultAsync(c => c.ClientId == clientId);

            Assert.NotNull(client);
            Assert.False(client.RequireClientSecret);
            Assert.Empty(client.ClientSecrets);
            Assert.Contains(client.AllowedCorsOrigins, o => o.Origin == "https://localhost:3000");
        });
    }

    [Fact]
    public async Task Should_PersistSelectedPresetAsClientProperty_When_ClientCreated()
    {
        string clientId = $"test-preset-{Guid.NewGuid():N}";
        var input = new ClientCreateInputModel
        {
            ClientId = clientId,
            ClientName = "Preset Tracking Client",
            SelectedPreset = "m2m",
            RequirePkce = false,
            RequireClientSecret = true,
            GrantTypes = new List<string> { "client_credentials" }
        };

        await _factory.RunInScopeAsync(async sp =>
        {
            IClientCreateService service = sp.GetRequiredService<IClientCreateService>();
            ClientCreateResult result = await service.CreateClientAsync(input);
            Assert.True(result.Success);
        });

        await _factory.RunInScopeAsync(async sp =>
        {
            ConfigurationDbContext db = sp.GetRequiredService<ConfigurationDbContext>();
            Duende.IdentityServer.EntityFramework.Entities.Client? client = await db.Clients
                .Include(c => c.Properties)
                .FirstOrDefaultAsync(c => c.ClientId == clientId);

            Assert.NotNull(client);
            ClientProperty? presetProperty =
                client!.Properties.FirstOrDefault(p => p.Key == ClientCreateService.PresetPropertyKey);
            Assert.NotNull(presetProperty);
            Assert.Equal("m2m", presetProperty!.Value);
        });
    }

    [Fact]
    public async Task CloneClientAsync_CopiesConfigurationButGeneratesANewSecret()
    {
        await SeedIdentityScopesAsync();
        string tag = Guid.NewGuid().ToString("N");
        string sourceClientId = $"clone-source-{tag}";
        string clonedClientId = $"clone-target-{tag}";

        await _factory.RunInScopeAsync(async sp =>
        {
            ConfigurationDbContext db = sp.GetRequiredService<ConfigurationDbContext>();
            db.Clients.Add(new Client
            {
                ClientId = sourceClientId,
                ClientName = "Source client",
                Description = "Source description",
                Enabled = false,
                RequirePkce = true,
                RequireClientSecret = true,
                AccessTokenLifetime = 1234,
                AllowedGrantTypes = new List<string> { "authorization_code" },
                RedirectUris = new List<string> { "https://source.example/signin" },
                PostLogoutRedirectUris = new List<string> { "https://source.example/signout" },
                AllowedCorsOrigins = new List<string> { "https://source.example" },
                AllowedScopes = new List<string> { "openid", "profile" },
                Properties = new Dictionary<string, string> { ["source-property"] = "source-value" }
            }.ToEntity());
            await db.SaveChangesAsync();
        });

        ClientCreateResult result = default!;
        await _factory.RunInScopeAsync(async sp =>
        {
            IClientCreateService service = sp.GetRequiredService<IClientCreateService>();
            result = await service.CloneClientAsync(sourceClientId, new ClientCreateInputModel
            {
                ClientId = clonedClientId,
                ClientName = "Cloned client",
                Description = "Cloned description"
            });
        });

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(clonedClientId, result.ClientId);
        Assert.False(string.IsNullOrWhiteSpace(result.PlaintextSecret));

        await _factory.RunInScopeAsync(async sp =>
        {
            ConfigurationDbContext db = sp.GetRequiredService<ConfigurationDbContext>();
            Duende.IdentityServer.EntityFramework.Entities.Client clone = await db.Clients
                .AsNoTracking()
                .Include(client => client.AllowedGrantTypes)
                .Include(client => client.RedirectUris)
                .Include(client => client.PostLogoutRedirectUris)
                .Include(client => client.AllowedCorsOrigins)
                .Include(client => client.AllowedScopes)
                .Include(client => client.Properties)
                .Include(client => client.ClientSecrets)
                .SingleAsync(client => client.ClientId == clonedClientId);

            Assert.Equal("Cloned client", clone.ClientName);
            Assert.Equal("Cloned description", clone.Description);
            Assert.False(clone.Enabled);
            Assert.True(clone.RequirePkce);
            Assert.True(clone.RequireClientSecret);
            Assert.Equal(1234, clone.AccessTokenLifetime);
            Assert.Equal("authorization_code", Assert.Single(clone.AllowedGrantTypes).GrantType);
            Assert.Equal("https://source.example/signin", Assert.Single(clone.RedirectUris).RedirectUri);
            Assert.Equal("https://source.example/signout",
                Assert.Single(clone.PostLogoutRedirectUris).PostLogoutRedirectUri);
            Assert.Equal("https://source.example", Assert.Single(clone.AllowedCorsOrigins).Origin);
            Assert.Equal(new[] { "openid", "profile" },
                clone.AllowedScopes.Select(scope => scope.Scope).OrderBy(scope => scope));
            Assert.Equal("source-value", clone.Properties.Single(property => property.Key == "source-property").Value);
            Assert.NotEqual(result.PlaintextSecret, Assert.Single(clone.ClientSecrets).Value);
        });
    }

    [Fact]
    public async Task CloneClientAsync_MissingSource_ReturnsTheCurrentValidationFailureResult()
    {
        string targetClientId = $"clone-missing-target-{Guid.NewGuid():N}";

        await _factory.RunInScopeAsync(async sp =>
        {
            IClientCreateService service = sp.GetRequiredService<IClientCreateService>();
            ClientCreateResult result = await service.CloneClientAsync("missing-source", new ClientCreateInputModel
            {
                ClientId = targetClientId,
                ClientName = "Target client"
            });

            Assert.False(result.Success);
            Assert.Equal(AdminMutationStatus.ValidationFailed, result.Status);
            Assert.Equal("Source client not found.", result.ErrorMessage);
        });
    }

    [Fact]
    public async Task CloneClientAsync_ExistingTarget_ReturnsConflictWithoutReplacingIt()
    {
        string tag = Guid.NewGuid().ToString("N");
        string sourceClientId = $"clone-duplicate-source-{tag}";
        string targetClientId = $"clone-duplicate-target-{tag}";

        await _factory.RunInScopeAsync(async sp =>
        {
            ConfigurationDbContext db = sp.GetRequiredService<ConfigurationDbContext>();
            db.Clients.AddRange(
                new Client { ClientId = sourceClientId, ClientName = "Source", RequireClientSecret = false }.ToEntity(),
                new Client { ClientId = targetClientId, ClientName = "Existing target", RequireClientSecret = false }
                    .ToEntity());
            await db.SaveChangesAsync();
        });

        await _factory.RunInScopeAsync(async sp =>
        {
            IClientCreateService service = sp.GetRequiredService<IClientCreateService>();
            ClientCreateResult result = await service.CloneClientAsync(sourceClientId, new ClientCreateInputModel
            {
                ClientId = targetClientId,
                ClientName = "Replacement target"
            });

            Assert.False(result.Success);
            Assert.Equal(AdminMutationStatus.Conflict, result.Status);
        });

        await _factory.RunInScopeAsync(async sp =>
        {
            ConfigurationDbContext db = sp.GetRequiredService<ConfigurationDbContext>();
            Duende.IdentityServer.EntityFramework.Entities.Client target =
                await db.Clients.AsNoTracking().SingleAsync(client => client.ClientId == targetClientId);
            Assert.Equal("Existing target", target.ClientName);
        });
    }
}