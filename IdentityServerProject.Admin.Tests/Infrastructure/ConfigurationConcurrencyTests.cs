using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Mappers;
using Duende.IdentityServer.Models;
using IdentityServerProject.Services.Apis;
using IdentityServerProject.Services.Clients;
using IdentityServerProject.Services.Scopes;
using IdentityServerProject.Services.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IdentityServerProject.Admin.Tests.Infrastructure;

[Collection(Task02SqlServerCollection.Name)]
public sealed class ConfigurationConcurrencyTests
{
    private readonly Task02SqlServerFactory _factory;

    public ConfigurationConcurrencyTests(Task02SqlServerFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ConcurrentApiResourceCreation_ReturnsOneSuccessAndOneStableConflict()
    {
        var name = $"task05-api-race-{Guid.NewGuid():N}";
        using var start = new Barrier(3);
        var attempts = Enumerable.Range(0, 2).Select(index => Task.Run(async () =>
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IApiResourceEditorService>();
            start.SignalAndWait();
            return await service.SaveBasicsAsync(new SaveApiResourceBasicsCommand(
                null,
                ScopeName.Create(name),
                $"Concurrent API {index}",
                null));
        })).ToArray();

        start.SignalAndWait();
        var results = await Task.WhenAll(attempts);

        Assert.Single(results, result => result.Status == AdminMutationStatus.Succeeded);
        Assert.Single(results, result => result.Status == AdminMutationStatus.Conflict
            && result.Errors.ContainsKey("Basics.Name"));
    }

    [Fact]
    public async Task ConcurrentClientCreation_ReturnsOneSuccessAndOneStableConflict()
    {
        var clientId = $"task05-client-race-{Guid.NewGuid():N}";
        await _factory.RunInScopeAsync(async services =>
        {
            var db = services.GetRequiredService<ConfigurationDbContext>();
            if (!await db.IdentityResources.AnyAsync(resource => resource.Name == "openid"))
            {
                db.IdentityResources.Add(new IdentityResource("openid", new[] { "sub" }).ToEntity());
                await db.SaveChangesAsync();
            }
        });
        using var start = new Barrier(3);
        var attempts = Enumerable.Range(0, 2).Select(index => Task.Run(async () =>
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IClientCreateService>();
            start.SignalAndWait();
            return await service.CreateClientAsync(new ClientCreateInputModel
            {
                ClientId = clientId,
                ClientName = $"Concurrent Client {index}",
                RequireClientSecret = false,
                RequirePkce = true,
                GrantTypes = new List<string> { "authorization_code" },
                RedirectUris = new List<string> { $"https://client-{index}.example.test/callback" },
                AllowedScopes = new List<string> { "openid" }
            });
        })).ToArray();

        start.SignalAndWait();
        var results = await Task.WhenAll(attempts);

        Assert.Single(results, result => result.Status == AdminMutationStatus.Succeeded);
        Assert.Single(results, result => result.Status == AdminMutationStatus.Conflict
            && result.Errors.ContainsKey("ClientId"));
    }
}
