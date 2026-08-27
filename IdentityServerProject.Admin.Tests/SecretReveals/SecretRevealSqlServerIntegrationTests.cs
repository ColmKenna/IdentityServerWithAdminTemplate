using System.Collections.Concurrent;
using System.Security.Claims;
using IdentityServerProject.Admin.Tests.Infrastructure;
using IdentityServerProject.Data;
using IdentityServerProject.Data.Adapters;
using IdentityServerProject.Services.AuditLogs;
using IdentityServerProject.Services.SecretReveals;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IdentityServerProject.Admin.Tests.SecretReveals;

[Collection(Task02SqlServerCollection.Name)]
public sealed class SecretRevealSqlServerIntegrationTests
{
    private readonly Task02SqlServerFactory _factory;

    public SecretRevealSqlServerIntegrationTests(Task02SqlServerFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task RevealIssuedByOneInstance_SurvivesRestartAndIsConsumedByAnotherInstance()
    {
        const string plaintext = "cross-instance-secret";
        SecretRevealTicket ticket;
        await using (ServiceProvider issuer = BuildInstance("shared-actor"))
        await using (AsyncServiceScope scope = issuer.CreateAsyncScope())
            ticket = await scope.ServiceProvider.GetRequiredService<ISecretRevealService>().IssueAsync(
                new SecretRevealTarget(SecretRevealPurpose.ClientCreated, "cross-instance-client"), plaintext);

        await using (ServiceProvider verifier = BuildInstance("shared-actor"))
        await using (AsyncServiceScope scope = verifier.CreateAsyncScope())
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            SecretRevealRecord record = await db.SecretRevealRecords.AsNoTracking()
                .SingleAsync(reveal => reveal.TargetId == "cross-instance-client");
            Assert.Equal(32, record.HandleDigest.Length);
            Assert.DoesNotContain(ticket.Handle, record.ProtectedPayload, StringComparison.Ordinal);
            Assert.DoesNotContain(plaintext, record.ProtectedPayload, StringComparison.Ordinal);
            Assert.True(await db.DataProtectionKeys.AnyAsync());

            SecretRevealConsumeResult result = await scope.ServiceProvider.GetRequiredService<ISecretRevealService>()
                .ConsumeAsync(
                    new SecretRevealTarget(SecretRevealPurpose.ClientCreated, "cross-instance-client"), ticket.Handle);
            Assert.Equal(SecretRevealConsumeStatus.Revealed, result.Status);
            Assert.Equal(plaintext, result.Plaintext);
        }

        await using ServiceProvider finalVerifier = BuildInstance("shared-actor");
        await using AsyncServiceScope finalScope = finalVerifier.CreateAsyncScope();
        Assert.False(await finalScope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
            .SecretRevealRecords.AnyAsync(reveal => reveal.TargetId == "cross-instance-client"));
    }

    [Fact]
    public async Task TwoConcurrentSqlServerConsumers_ReceiveExactlyOnePlaintextResult()
    {
        SecretRevealTicket ticket;
        await using (ServiceProvider issuer = BuildInstance("concurrent-actor"))
        await using (AsyncServiceScope scope = issuer.CreateAsyncScope())
            ticket = await scope.ServiceProvider.GetRequiredService<ISecretRevealService>().IssueAsync(
                new SecretRevealTarget(SecretRevealPurpose.ApiResourceSecretGenerated, "concurrent.api"),
                "one-consumer-only");

        using var start = new Barrier(3);
        Task<SecretRevealConsumeResult>[] attempts = Enumerable.Range(0, 2).Select(_ => Task.Run(async () =>
        {
            await using ServiceProvider instance = BuildInstance("concurrent-actor");
            await using AsyncServiceScope scope = instance.CreateAsyncScope();
            ISecretRevealService service = scope.ServiceProvider.GetRequiredService<ISecretRevealService>();
            start.SignalAndWait();
            return await service.ConsumeAsync(
                new SecretRevealTarget(SecretRevealPurpose.ApiResourceSecretGenerated, "concurrent.api"),
                ticket.Handle);
        })).ToArray();

        start.SignalAndWait();
        SecretRevealConsumeResult[] results = await Task.WhenAll(attempts);

        Assert.Single(results, result =>
            result.Status == SecretRevealConsumeStatus.Revealed
            && result.Plaintext == "one-consumer-only");
        Assert.Single(results, result =>
            result.Status == SecretRevealConsumeStatus.Unavailable
            && result.Plaintext == null);
    }

    private ServiceProvider BuildInstance(string actorSubjectId)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IAuditWriter, ThreadSafeAuditWriter>();
        services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(
            _factory.IdentityConnectionString,
            sql => sql.MigrationsAssembly(typeof(Program).Assembly.FullName).EnableRetryOnFailure()));
        services.AddDataProtection()
            .SetApplicationName("IdentityServerProject")
            .PersistKeysToDbContext<ApplicationDbContext>();
        services.AddScoped<ISecretRevealStore, EfSecretRevealStore>();
        services.AddScoped<ISecretRevealService, SecretRevealService>();

        ServiceProvider provider = services.BuildServiceProvider();
        provider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, actorSubjectId) }, "SqlSecretRevealTests"))
        };
        return provider;
    }

    private sealed class ThreadSafeAuditWriter : IAuditWriter
    {
        private readonly ConcurrentQueue<AdminAuditEvent> _events = new();

        public Task WriteAsync(AdminAuditEvent auditEvent, CancellationToken cancellationToken = default)
        {
            _events.Enqueue(auditEvent);
            return Task.CompletedTask;
        }
    }
}