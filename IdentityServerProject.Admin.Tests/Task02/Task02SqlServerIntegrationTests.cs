using System.Net;
using System.Security.Claims;
using AngleSharp.Html.Parser;
using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Entities;
using Duende.IdentityServer.EntityFramework.Mappers;
using Duende.IdentityServer.Models;
using IdentityServerProject.Admin.Tests.Infrastructure;
using IdentityServerProject.Data;
using IdentityServerProject.Services.AuditLogs;
using IdentityServerProject.Services.Clients;
using IdentityServerProject.Services.Users;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Client = Duende.IdentityServer.Models.Client;

namespace IdentityServerProject.Admin.Tests.Task02;

[Collection(Task02SqlServerCollection.Name)]
public sealed class Task02SqlServerIntegrationTests
{
    private readonly Task02SqlServerFactory _factory;

    public Task02SqlServerIntegrationTests(Task02SqlServerFactory factory)
    {
        _factory = factory;
        _factory.BackChannelLogout.Reset();
    }

    [Fact]
    public async Task RevokeUserAccess_RejectsPreviouslyIssuedIdentityCookieOnNextRequest()
    {
        using var client = _factory.CreateHttpsClient();
        await SignInAsync(client, "admin@sales.local", "Password123!");

        _factory.IdentityCommands.Reset();
        var before = await client.GetAsync("/Admin/Clients");
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        Assert.InRange(_factory.IdentityCommands.ReadCount, 1, 10);
        Console.WriteLine(
            $"TASK-02 per-request security-stamp validation executed {_factory.IdentityCommands.ReadCount} IdentityDb read command(s).");

        string userId = string.Empty;
        string? originalStamp = null;
        var grantKey = $"task02-cookie-{Guid.NewGuid():N}";
        await _factory.RunInScopeAsync(async services =>
        {
            var users = services.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.FindByNameAsync("admin@sales.local");
            Assert.NotNull(user);
            userId = user!.Id;
            originalStamp = user.SecurityStamp;

            var grants = services.GetRequiredService<PersistedGrantDbContext>();
            grants.PersistedGrants.Add(NewGrant(grantKey, userId));
            await grants.SaveChangesAsync();
        });

        await _factory.RunInScopeAsync(async services =>
        {
            var service = services.GetRequiredService<IUserDetailsService>();
            var result = await service.RevokeUserAccessAsync(userId, "another-administrator");
            Assert.True(result.Success);
            Assert.Equal(1, result.RevokedGrantCount);
        });

        await _factory.RunInScopeAsync(async services =>
        {
            var user = await services.GetRequiredService<ApplicationDbContext>().Users
                .AsNoTracking()
                .SingleAsync(u => u.Id == userId);
            Assert.NotEqual(originalStamp, user.SecurityStamp);

            var grantExists = await services.GetRequiredService<PersistedGrantDbContext>()
                .PersistedGrants.AsNoTracking().AnyAsync(g => g.Key == grantKey);
            Assert.False(grantExists);
        });

        var after = await client.GetAsync("/Admin/Clients");
        Assert.Equal(HttpStatusCode.Redirect, after.StatusCode);
        Assert.StartsWith("/Account/Login", after.Headers.Location?.PathAndQuery, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RevokeUserAccess_NotifiesOnlyAfterCommit_AndNotificationFailureReturnsWarning()
    {
        var tag = Guid.NewGuid().ToString("N");
        var grantKey = $"task02-notification-{tag}";
        string userId = string.Empty;
        string? originalStamp = null;

        await _factory.RunInScopeAsync(async services =>
        {
            var users = services.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser
            {
                UserName = $"notification-{tag}@sales.local",
                Email = $"notification-{tag}@sales.local"
            };
            Assert.True((await users.CreateAsync(user, "Password123!")).Succeeded);
            userId = user.Id;
            originalStamp = user.SecurityStamp;

            var grants = services.GetRequiredService<PersistedGrantDbContext>();
            grants.PersistedGrants.Add(NewGrant(grantKey, user.Id));
            await grants.SaveChangesAsync();
        });

        var callbackObservedCommittedState = false;
        _factory.BackChannelLogout.OnSendAsync = async (_, _) =>
        {
            await _factory.RunInScopeAsync(async services =>
            {
                var user = await services.GetRequiredService<ApplicationDbContext>().Users
                    .AsNoTracking().SingleAsync(u => u.Id == userId);
                var grantExists = await services.GetRequiredService<PersistedGrantDbContext>()
                    .PersistedGrants.AsNoTracking().AnyAsync(g => g.Key == grantKey);
                callbackObservedCommittedState = user.SecurityStamp != originalStamp && !grantExists;
            });
            throw new InvalidOperationException("Simulated downstream notification outage.");
        };

        try
        {
            await _factory.RunInScopeAsync(async services =>
            {
                var service = services.GetRequiredService<IUserDetailsService>();
                var result = await service.RevokeUserAccessAsync(userId, "another-administrator");
                Assert.True(result.Success);
                Assert.NotNull(result.WarningMessage);
            });
        }
        finally
        {
            _factory.BackChannelLogout.OnSendAsync = null;
        }

        Assert.True(callbackObservedCommittedState);
        await _factory.RunInScopeAsync(async services =>
        {
            var user = await services.GetRequiredService<ApplicationDbContext>().Users
                .AsNoTracking().SingleAsync(u => u.Id == userId);
            Assert.NotEqual(originalStamp, user.SecurityStamp);
            Assert.False(await services.GetRequiredService<PersistedGrantDbContext>()
                .PersistedGrants.AsNoTracking().AnyAsync(g => g.Key == grantKey));

            var audits = await services.GetRequiredService<ApplicationDbContext>().AuditLogEntries
                .AsNoTracking()
                .Where(a => a.TargetId == userId
                    && (a.Action == AuditActions.RevokeUserAccess || a.Action == AuditActions.SendBackChannelLogout))
                .ToListAsync();
            Assert.Single(audits, a => a.Action == AuditActions.RevokeUserAccess
                && a.Outcome == AuditOutcome.Succeeded);
            Assert.Single(audits, a => a.Action == AuditActions.SendBackChannelLogout
                && a.Outcome == AuditOutcome.Failed
                && a.ReasonCode == AuditReasonCodes.NotificationFailure);
        });
    }

    [Fact]
    public async Task RemoveOwnSysAdmin_IsDeniedAtServiceBoundary()
    {
        await _factory.RunInScopeAsync(async services =>
        {
            var users = services.GetRequiredService<UserManager<ApplicationUser>>();
            var admin = await users.FindByNameAsync("admin@sales.local");
            Assert.NotNull(admin);

            var actor = services.GetRequiredService<IHttpContextAccessor>();
            actor.HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    new[] { new Claim(ClaimTypes.NameIdentifier, admin!.Id) }, "Task02"))
            };

            var result = await services.GetRequiredService<IUserDetailsService>()
                .RemoveRoleAsync(admin.Id, Config.SysAdminRole);

            Assert.False(result.Success);
            Assert.Equal(AuditReasonCodes.SelfDemotion, result.ReasonCode);
            Assert.True(await users.IsInRoleAsync(admin, Config.SysAdminRole));
            actor.HttpContext = null;
        });
    }

    [Fact]
    public async Task ConcurrentRemovalOfTwoRemainingSysAdmins_AllowsExactlyOneAndAuditsOncePerAttempt()
    {
        var tag = Guid.NewGuid().ToString("N");
        var userIds = new List<string>();
        string seedAdminId = string.Empty;

        await _factory.RunInScopeAsync(async services =>
        {
            var users = services.GetRequiredService<UserManager<ApplicationUser>>();
            for (var index = 0; index < 2; index++)
            {
                var user = new ApplicationUser
                {
                    UserName = $"concurrent-admin-{tag}-{index}@sales.local",
                    Email = $"concurrent-admin-{tag}-{index}@sales.local"
                };
                Assert.True((await users.CreateAsync(user, "Password123!")).Succeeded);
                Assert.True((await users.AddToRoleAsync(user, Config.SysAdminRole)).Succeeded);
                userIds.Add(user.Id);
            }

            var seedAdmin = await users.FindByNameAsync("admin@sales.local");
            Assert.NotNull(seedAdmin);
            seedAdminId = seedAdmin!.Id;
            Assert.True((await users.RemoveFromRoleAsync(seedAdmin, Config.SysAdminRole)).Succeeded);
        });

        try
        {
            using var start = new Barrier(3);
            var attempts = userIds.Select(userId => Task.Run(async () =>
            {
                await using var scope = _factory.Services.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<IUserDetailsService>();
                start.SignalAndWait();
                return await service.RemoveRoleAsync(userId, Config.SysAdminRole);
            })).ToArray();

            start.SignalAndWait();
            var results = await Task.WhenAll(attempts);

            Assert.Single(results, result => result.Success);
            Assert.Single(results, result => !result.Success
                && result.ReasonCode == AuditReasonCodes.LastAdministrator);

            await _factory.RunInScopeAsync(async services =>
            {
                var db = services.GetRequiredService<ApplicationDbContext>();
                var sysAdminRoleId = await db.Roles.Where(r => r.Name == Config.SysAdminRole)
                    .Select(r => r.Id).SingleAsync();
                Assert.Equal(1, await db.UserRoles.CountAsync(ur => ur.RoleId == sysAdminRoleId));

                var audits = await db.AuditLogEntries.AsNoTracking()
                    .Where(a => a.Action == AuditActions.RemoveRole && userIds.Contains(a.TargetId!))
                    .ToListAsync();
                Assert.Equal(2, audits.Count);
                Assert.Single(audits, a => a.Outcome == AuditOutcome.Succeeded);
                Assert.Single(audits, a => a.Outcome == AuditOutcome.Denied
                    && a.ReasonCode == AuditReasonCodes.LastAdministrator);
            });
        }
        finally
        {
            await _factory.RunInScopeAsync(async services =>
            {
                var users = services.GetRequiredService<UserManager<ApplicationUser>>();
                var seedAdmin = await users.FindByIdAsync(seedAdminId);
                if (seedAdmin != null && !await users.IsInRoleAsync(seedAdmin, Config.SysAdminRole))
                {
                    await users.AddToRoleAsync(seedAdmin, Config.SysAdminRole);
                }
            });
        }
    }

    [Fact]
    public async Task ConcurrentEnableAndDelete_ProducesOnlyASerializedOutcome()
    {
        var clientId = $"task02-client-race-{Guid.NewGuid():N}";
        await SeedClientAsync(new Client
        {
            ClientId = clientId,
            ClientName = "TASK-02 Client Race",
            Enabled = false,
            RequireClientSecret = false
        });
        await _factory.RunInScopeAsync(async services =>
        {
            var db = services.GetRequiredService<ConfigurationDbContext>();
            var entityId = await db.Clients.Where(c => c.ClientId == clientId).Select(c => c.Id).SingleAsync();
            db.Set<ClientProperty>().Add(new ClientProperty
            {
                ClientId = entityId,
                Key = ClientDetailsService.DisabledAtPropertyKey,
                Value = DateTime.UtcNow.AddDays(-100).ToString("O")
            });
            await db.SaveChangesAsync();
        });

        using var start = new Barrier(3);
        var enable = Task.Run(async () =>
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IClientDetailsService>();
            start.SignalAndWait();
            return await service.ToggleClientStatusAsync(clientId);
        });
        var delete = Task.Run(async () =>
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IClientDetailsService>();
            start.SignalAndWait();
            return await service.DeleteClientAsync(clientId);
        });

        start.SignalAndWait();
        await Task.WhenAll(enable, delete);

        await _factory.RunInScopeAsync(async services =>
        {
            var client = await services.GetRequiredService<ConfigurationDbContext>().Clients
                .AsNoTracking().SingleOrDefaultAsync(c => c.ClientId == clientId);
            if (delete.Result.Success)
            {
                Assert.False(enable.Result);
                Assert.Null(client);
            }
            else
            {
                Assert.True(enable.Result);
                Assert.Equal(AuditReasonCodes.ClientEnabled, delete.Result.ReasonCode);
                Assert.NotNull(client);
                Assert.True(client!.Enabled);
            }
        });
    }

    [Fact]
    public async Task SecretRevocation_ExpiredReplacementDoesNotCount_AndExpiredTargetCanBeRemoved()
    {
        var clientId = $"task02-secret-expiry-{Guid.NewGuid():N}";
        await SeedClientAsync(new Client
        {
            ClientId = clientId,
            ClientName = "TASK-02 Secret Expiry",
            RequireClientSecret = true,
            ClientSecrets =
            {
                new Duende.IdentityServer.Models.Secret("valid-hash") { Expiration = DateTime.UtcNow.AddDays(1) },
                new Duende.IdentityServer.Models.Secret("expired-hash") { Expiration = DateTime.UtcNow.AddDays(-1) }
            }
        });

        var secretIds = await GetSecretIdsAsync(clientId);
        var validId = secretIds.Single(s => s.Expiration > DateTime.UtcNow).Id;
        var expiredId = secretIds.Single(s => s.Expiration < DateTime.UtcNow).Id;

        await _factory.RunInScopeAsync(async services =>
        {
            var service = services.GetRequiredService<IClientDetailsService>();
            var validResult = await service.RevokeClientSecretAsync(clientId, validId);
            Assert.False(validResult.Success);
            Assert.Equal(AuditReasonCodes.LastUsableSecret, validResult.ReasonCode);

            var expiredResult = await service.RevokeClientSecretAsync(clientId, expiredId);
            Assert.True(expiredResult.Success);
        });

        Assert.Single(await GetSecretIdsAsync(clientId));
    }

    [Fact]
    public async Task ConcurrentRevocationOfFinalTwoUsableSecrets_AllowsExactlyOneAndAuditsOncePerAttempt()
    {
        var clientId = $"task02-secret-race-{Guid.NewGuid():N}";
        await SeedClientAsync(new Client
        {
            ClientId = clientId,
            ClientName = "TASK-02 Secret Race",
            RequireClientSecret = true,
            ClientSecrets =
            {
                new Duende.IdentityServer.Models.Secret("first-hash") { Expiration = DateTime.UtcNow.AddDays(1) },
                new Duende.IdentityServer.Models.Secret("second-hash") { Expiration = DateTime.UtcNow.AddDays(1) }
            }
        });
        var secretIds = (await GetSecretIdsAsync(clientId)).Select(s => s.Id).ToArray();

        using var start = new Barrier(3);
        var attempts = secretIds.Select(secretId => Task.Run(async () =>
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IClientDetailsService>();
            start.SignalAndWait();
            return await service.RevokeClientSecretAsync(clientId, secretId);
        })).ToArray();

        start.SignalAndWait();
        var results = await Task.WhenAll(attempts);

        Assert.Single(results, result => result.Success);
        Assert.Single(results, result => !result.Success
            && result.ReasonCode == AuditReasonCodes.LastUsableSecret);
        Assert.Single(await GetSecretIdsAsync(clientId));

        await _factory.RunInScopeAsync(async services =>
        {
            var audits = await services.GetRequiredService<ApplicationDbContext>().AuditLogEntries
                .AsNoTracking()
                .Where(a => a.Action == AuditActions.RevokeSecret && a.TargetId == clientId)
                .ToListAsync();
            Assert.Equal(2, audits.Count);
            Assert.Single(audits, a => a.Outcome == AuditOutcome.Succeeded);
            Assert.Single(audits, a => a.Outcome == AuditOutcome.Denied
                && a.ReasonCode == AuditReasonCodes.LastUsableSecret);
        });
    }

    private async Task SeedClientAsync(Client client)
    {
        await _factory.RunInScopeAsync(async services =>
        {
            var db = services.GetRequiredService<ConfigurationDbContext>();
            db.Clients.Add(client.ToEntity());
            await db.SaveChangesAsync();
        });
    }

    private async Task<List<ClientSecret>> GetSecretIdsAsync(string clientId)
    {
        var result = new List<ClientSecret>();
        await _factory.RunInScopeAsync(async services =>
        {
            var client = await services.GetRequiredService<ConfigurationDbContext>().Clients
                .AsNoTracking()
                .Include(c => c.ClientSecrets)
                .SingleAsync(c => c.ClientId == clientId);
            result = client.ClientSecrets.ToList();
        });
        return result;
    }

    private static Duende.IdentityServer.EntityFramework.Entities.PersistedGrant NewGrant(
        string key,
        string subjectId) => new()
    {
        Key = key,
        Type = "refresh_token",
        ClientId = "task02-client",
        SubjectId = subjectId,
        CreationTime = DateTime.UtcNow,
        Expiration = DateTime.UtcNow.AddDays(1),
        Data = "{}"
    };

    private static async Task SignInAsync(HttpClient client, string username, string password)
    {
        var loginPage = await client.GetAsync("/Account/Login");
        loginPage.EnsureSuccessStatusCode();
        var document = await new HtmlParser().ParseDocumentAsync(await loginPage.Content.ReadAsStringAsync());
        var token = document.QuerySelector("input[name='__RequestVerificationToken']")?.GetAttribute("value");
        Assert.False(string.IsNullOrWhiteSpace(token));

        var response = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token!,
                ["Input.Username"] = username,
                ["Input.Password"] = password
            }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

}
