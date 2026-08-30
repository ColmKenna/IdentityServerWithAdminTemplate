using System.Net;
using System.Security.Claims;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Entities;
using Duende.IdentityServer.EntityFramework.Mappers;
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
using Secret = Duende.IdentityServer.Models.Secret;

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
        using HttpClient client = _factory.CreateHttpsClient();
        await SignInAsync(client, "admin@sales.local", "Password123!");

        _factory.IdentityCommands.Reset();
        HttpResponseMessage before = await client.GetAsync("/Admin/Clients");
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        Assert.InRange(_factory.IdentityCommands.ReadCount, 1, 10);
        Console.WriteLine(
            $"TASK-02 per-request security-stamp validation executed {_factory.IdentityCommands.ReadCount} IdentityDb read command(s).");

        string userId = string.Empty;
        string? originalStamp = null;
        string grantKey = $"task02-cookie-{Guid.NewGuid():N}";
        await _factory.RunInScopeAsync(async services =>
        {
            UserManager<ApplicationUser> users = services.GetRequiredService<UserManager<ApplicationUser>>();
            ApplicationUser? user = await users.FindByNameAsync("admin@sales.local");
            Assert.NotNull(user);
            userId = user!.Id;
            originalStamp = user.SecurityStamp;

            PersistedGrantDbContext grants = services.GetRequiredService<PersistedGrantDbContext>();
            grants.PersistedGrants.Add(NewGrant(grantKey, userId));
            await grants.SaveChangesAsync();
        });

        await _factory.RunInScopeAsync(async services =>
        {
            IUserDetailsService service = services.GetRequiredService<IUserDetailsService>();
            UserAccessRevokeResult result =
                await service.RevokeUserAccessAsync(new UserActionContext(UserId.Create(userId),
                    UserId.Create("another-administrator")));
            Assert.True(result.Success);
            Assert.Equal(1, result.RevokedGrantCount);
        });

        await _factory.RunInScopeAsync(async services =>
        {
            ApplicationUser user = await services.GetRequiredService<ApplicationDbContext>().Users
                .AsNoTracking()
                .SingleAsync(u => u.Id == userId);
            Assert.NotEqual(originalStamp, user.SecurityStamp);

            bool grantExists = await services.GetRequiredService<PersistedGrantDbContext>()
                .PersistedGrants.AsNoTracking().AnyAsync(g => g.Key == grantKey);
            Assert.False(grantExists);
        });

        HttpResponseMessage after = await client.GetAsync("/Admin/Clients");
        Assert.Equal(HttpStatusCode.Redirect, after.StatusCode);
        Assert.StartsWith("/Account/Login", after.Headers.Location?.PathAndQuery, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RevokeUserAccess_NotifiesOnlyAfterCommit_AndNotificationFailureReturnsWarning()
    {
        string tag = Guid.NewGuid().ToString("N");
        string grantKey = $"task02-notification-{tag}";
        string userId = string.Empty;
        string? originalStamp = null;

        await _factory.RunInScopeAsync(async services =>
        {
            UserManager<ApplicationUser> users = services.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser
            {
                UserName = $"notification-{tag}@sales.local",
                Email = $"notification-{tag}@sales.local"
            };
            Assert.True((await users.CreateAsync(user, "Password123!")).Succeeded);
            userId = user.Id;
            originalStamp = user.SecurityStamp;

            PersistedGrantDbContext grants = services.GetRequiredService<PersistedGrantDbContext>();
            grants.PersistedGrants.Add(NewGrant(grantKey, user.Id));
            await grants.SaveChangesAsync();
        });

        bool callbackObservedCommittedState = false;
        _factory.BackChannelLogout.OnSendAsync = async (_, _) =>
        {
            await _factory.RunInScopeAsync(async services =>
            {
                ApplicationUser user = await services.GetRequiredService<ApplicationDbContext>().Users
                    .AsNoTracking().SingleAsync(u => u.Id == userId);
                bool grantExists = await services.GetRequiredService<PersistedGrantDbContext>()
                    .PersistedGrants.AsNoTracking().AnyAsync(g => g.Key == grantKey);
                callbackObservedCommittedState = user.SecurityStamp != originalStamp && !grantExists;
            });
            throw new InvalidOperationException("Simulated downstream notification outage.");
        };

        try
        {
            await _factory.RunInScopeAsync(async services =>
            {
                IUserDetailsService service = services.GetRequiredService<IUserDetailsService>();
                UserAccessRevokeResult result = await service.RevokeUserAccessAsync(
                    new UserActionContext(UserId.Create(userId), UserId.Create("another-administrator")));
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
            ApplicationUser user = await services.GetRequiredService<ApplicationDbContext>().Users
                .AsNoTracking().SingleAsync(u => u.Id == userId);
            Assert.NotEqual(originalStamp, user.SecurityStamp);
            Assert.False(await services.GetRequiredService<PersistedGrantDbContext>()
                .PersistedGrants.AsNoTracking().AnyAsync(g => g.Key == grantKey));

            List<AuditLogEntry> audits = await services.GetRequiredService<ApplicationDbContext>().AuditLogEntries
                .AsNoTracking()
                .Where(a => a.TargetId == userId
                            && (a.Action == AuditAction.RevokeUserAccess ||
                                a.Action == AuditAction.SendBackChannelLogout))
                .ToListAsync();
            Assert.Single(audits, a => a.Action == AuditAction.RevokeUserAccess
                                       && a.Outcome == AuditOutcome.Succeeded);
            Assert.Single(audits, a => a.Action == AuditAction.SendBackChannelLogout
                                       && a.Outcome == AuditOutcome.Failed
                                       && a.ReasonCode == AuditReasonCode.NotificationFailure);
        });
    }

    [Fact]
    public async Task RemoveOwnSysAdmin_IsDeniedAtServiceBoundary()
    {
        await _factory.RunInScopeAsync(async services =>
        {
            UserManager<ApplicationUser> users = services.GetRequiredService<UserManager<ApplicationUser>>();
            ApplicationUser? admin = await users.FindByNameAsync("admin@sales.local");
            Assert.NotNull(admin);

            IHttpContextAccessor actor = services.GetRequiredService<IHttpContextAccessor>();
            actor.HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    new[] { new Claim(ClaimTypes.NameIdentifier, admin!.Id) }, "Task02"))
            };

            RoleChangeResult result = await services.GetRequiredService<IUserDetailsService>()
                .RemoveRoleAsync(UserId.Create(admin.Id), Config.SysAdminRole);

            Assert.False(result.Success);
            Assert.Equal(AuditReasonCode.SelfDemotion, result.ReasonCode);
            Assert.True(await users.IsInRoleAsync(admin, Config.SysAdminRole));
            actor.HttpContext = null;
        });
    }

    [Fact]
    public async Task ConcurrentRemovalOfTwoRemainingSysAdmins_AllowsExactlyOneAndAuditsOncePerAttempt()
    {
        string tag = Guid.NewGuid().ToString("N");
        var userIds = new List<string>();
        string seedAdminId = string.Empty;

        await _factory.RunInScopeAsync(async services =>
        {
            UserManager<ApplicationUser> users = services.GetRequiredService<UserManager<ApplicationUser>>();
            for (int index = 0; index < 2; index++)
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

            ApplicationUser? seedAdmin = await users.FindByNameAsync("admin@sales.local");
            Assert.NotNull(seedAdmin);
            seedAdminId = seedAdmin!.Id;
            Assert.True((await users.RemoveFromRoleAsync(seedAdmin, Config.SysAdminRole)).Succeeded);
        });

        try
        {
            using var start = new Barrier(3);
            Task<RoleChangeResult>[] attempts = userIds.Select(userId => Task.Run(async () =>
            {
                await using AsyncServiceScope scope = _factory.Services.CreateAsyncScope();
                IUserDetailsService service = scope.ServiceProvider.GetRequiredService<IUserDetailsService>();
                start.SignalAndWait();
                return await service.RemoveRoleAsync(UserId.Create(userId), Config.SysAdminRole);
            })).ToArray();

            start.SignalAndWait();
            RoleChangeResult[] results = await Task.WhenAll(attempts);

            Assert.Single(results, result => result.Success);
            Assert.Single(results, result => !result.Success
                                             && result.ReasonCode == AuditReasonCode.LastAdministrator);

            await _factory.RunInScopeAsync(async services =>
            {
                ApplicationDbContext db = services.GetRequiredService<ApplicationDbContext>();
                string sysAdminRoleId = await db.Roles.Where(r => r.Name == Config.SysAdminRole)
                    .Select(r => r.Id).SingleAsync();
                Assert.Equal(1, await db.UserRoles.CountAsync(ur => ur.RoleId == sysAdminRoleId));

                List<AuditLogEntry> audits = await db.AuditLogEntries.AsNoTracking()
                    .Where(a => a.Action == AuditAction.RemoveRole && userIds.Contains(a.TargetId!))
                    .ToListAsync();
                Assert.Equal(2, audits.Count);
                Assert.Single(audits, a => a.Outcome == AuditOutcome.Succeeded);
                Assert.Single(audits, a => a.Outcome == AuditOutcome.Denied
                                           && a.ReasonCode == AuditReasonCode.LastAdministrator);
            });
        }
        finally
        {
            await _factory.RunInScopeAsync(async services =>
            {
                UserManager<ApplicationUser> users = services.GetRequiredService<UserManager<ApplicationUser>>();
                ApplicationUser? seedAdmin = await users.FindByIdAsync(seedAdminId);
                if (seedAdmin != null && !await users.IsInRoleAsync(seedAdmin, Config.SysAdminRole))
                    await users.AddToRoleAsync(seedAdmin, Config.SysAdminRole);
            });
        }
    }

    [Fact]
    public async Task ConcurrentEnableAndDelete_ProducesOnlyASerializedOutcome()
    {
        string clientId = $"task02-client-race-{Guid.NewGuid():N}";
        await SeedClientAsync(new Client
        {
            ClientId = clientId,
            ClientName = "TASK-02 Client Race",
            Enabled = false,
            RequireClientSecret = false
        });
        await _factory.RunInScopeAsync(async services =>
        {
            ConfigurationDbContext db = services.GetRequiredService<ConfigurationDbContext>();
            int entityId = await db.Clients.Where(c => c.ClientId == clientId).Select(c => c.Id).SingleAsync();
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
            await using AsyncServiceScope scope = _factory.Services.CreateAsyncScope();
            ClientDetailsService service = scope.ServiceProvider.GetRequiredService<ClientDetailsService>();
            start.SignalAndWait();
            return await service.ToggleClientStatusAsync(ClientId.Create(clientId));
        });
        var delete = Task.Run(async () =>
        {
            await using AsyncServiceScope scope = _factory.Services.CreateAsyncScope();
            ClientDetailsService service = scope.ServiceProvider.GetRequiredService<ClientDetailsService>();
            start.SignalAndWait();
            return await service.DeleteClientAsync(ClientId.Create(clientId));
        });

        start.SignalAndWait();
        await Task.WhenAll(enable, delete);

        await _factory.RunInScopeAsync(async services =>
        {
            Duende.IdentityServer.EntityFramework.Entities.Client? client = await services
                .GetRequiredService<ConfigurationDbContext>().Clients
                .AsNoTracking().SingleOrDefaultAsync(c => c.ClientId == clientId);
            if (delete.Result.Success)
            {
                Assert.False(enable.Result);
                Assert.Null(client);
            }
            else
            {
                Assert.True(enable.Result);
                Assert.Equal(AuditReasonCode.ClientEnabled, delete.Result.ReasonCode);
                Assert.NotNull(client);
                Assert.True(client!.Enabled);
            }
        });
    }

    [Fact]
    public async Task SecretRevocation_ExpiredReplacementDoesNotCount_AndExpiredTargetCanBeRemoved()
    {
        string clientId = $"task02-secret-expiry-{Guid.NewGuid():N}";
        await SeedClientAsync(new Client
        {
            ClientId = clientId,
            ClientName = "TASK-02 Secret Expiry",
            RequireClientSecret = true,
            ClientSecrets =
            {
                new Secret("valid-hash") { Expiration = DateTime.UtcNow.AddDays(1) },
                new Secret("expired-hash") { Expiration = DateTime.UtcNow.AddDays(-1) }
            }
        });

        List<ClientSecret> secretIds = await GetSecretIdsAsync(clientId);
        int validId = secretIds.Single(s => s.Expiration > DateTime.UtcNow).Id;
        int expiredId = secretIds.Single(s => s.Expiration < DateTime.UtcNow).Id;

        await _factory.RunInScopeAsync(async services =>
        {
            ClientDetailsService service = services.GetRequiredService<ClientDetailsService>();
            ClientSecretRevokeResult validResult =
                await service.RevokeClientSecretAsync(ClientId.Create(clientId), validId);
            Assert.False(validResult.Success);
            Assert.Equal(AuditReasonCode.LastUsableSecret, validResult.ReasonCode);

            ClientSecretRevokeResult expiredResult =
                await service.RevokeClientSecretAsync(ClientId.Create(clientId), expiredId);
            Assert.True(expiredResult.Success);
        });

        Assert.Single(await GetSecretIdsAsync(clientId));
    }

    [Fact]
    public async Task ConcurrentRevocationOfFinalTwoUsableSecrets_AllowsExactlyOneAndAuditsOncePerAttempt()
    {
        string clientId = $"task02-secret-race-{Guid.NewGuid():N}";
        await SeedClientAsync(new Client
        {
            ClientId = clientId,
            ClientName = "TASK-02 Secret Race",
            RequireClientSecret = true,
            ClientSecrets =
            {
                new Secret("first-hash") { Expiration = DateTime.UtcNow.AddDays(1) },
                new Secret("second-hash") { Expiration = DateTime.UtcNow.AddDays(1) }
            }
        });
        int[] secretIds = (await GetSecretIdsAsync(clientId)).Select(s => s.Id).ToArray();

        using var start = new Barrier(3);
        Task<ClientSecretRevokeResult>[] attempts = secretIds.Select(secretId => Task.Run(async () =>
        {
            await using AsyncServiceScope scope = _factory.Services.CreateAsyncScope();
            ClientDetailsService service = scope.ServiceProvider.GetRequiredService<ClientDetailsService>();
            start.SignalAndWait();
            return await service.RevokeClientSecretAsync(ClientId.Create(clientId), secretId);
        })).ToArray();

        start.SignalAndWait();
        ClientSecretRevokeResult[] results = await Task.WhenAll(attempts);

        Assert.Single(results, result => result.Success);
        Assert.Single(results, result => !result.Success
                                         && result.ReasonCode == AuditReasonCode.LastUsableSecret);
        Assert.Single(await GetSecretIdsAsync(clientId));

        await _factory.RunInScopeAsync(async services =>
        {
            List<AuditLogEntry> audits = await services.GetRequiredService<ApplicationDbContext>().AuditLogEntries
                .AsNoTracking()
                .Where(a => a.Action == AuditAction.RevokeSecret && a.TargetId == clientId)
                .ToListAsync();
            Assert.Equal(2, audits.Count);
            Assert.Single(audits, a => a.Outcome == AuditOutcome.Succeeded);
            Assert.Single(audits, a => a.Outcome == AuditOutcome.Denied
                                       && a.ReasonCode == AuditReasonCode.LastUsableSecret);
        });
    }

    private async Task SeedClientAsync(Client client)
    {
        await _factory.RunInScopeAsync(async services =>
        {
            ConfigurationDbContext db = services.GetRequiredService<ConfigurationDbContext>();
            db.Clients.Add(client.ToEntity());
            await db.SaveChangesAsync();
        });
    }

    private async Task<List<ClientSecret>> GetSecretIdsAsync(string clientId)
    {
        var result = new List<ClientSecret>();
        await _factory.RunInScopeAsync(async services =>
        {
            Duende.IdentityServer.EntityFramework.Entities.Client client = await services
                .GetRequiredService<ConfigurationDbContext>().Clients
                .AsNoTracking()
                .Include(c => c.ClientSecrets)
                .SingleAsync(c => c.ClientId == clientId);
            result = client.ClientSecrets.ToList();
        });
        return result;
    }

    private static PersistedGrant NewGrant(
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
        HttpResponseMessage loginPage = await client.GetAsync("/Account/Login");
        loginPage.EnsureSuccessStatusCode();
        IHtmlDocument document = await new HtmlParser().ParseDocumentAsync(await loginPage.Content.ReadAsStringAsync());
        string? token = document.QuerySelector("input[name='__RequestVerificationToken']")?.GetAttribute("value");
        Assert.False(string.IsNullOrWhiteSpace(token));

        HttpResponseMessage response = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token!,
                ["Input.Username"] = username,
                ["Input.Password"] = password
            }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }
}