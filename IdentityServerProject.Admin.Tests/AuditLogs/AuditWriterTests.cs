using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using IdentityServerProject.Data;
using IdentityServerProject.Data.Adapters;
using IdentityServerProject.Services.AuditLogs;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace IdentityServerProject.Admin.Tests.AuditLogs;

/// <summary>
/// Unit tests for <see cref="AuditWriter"/> constructed directly (not via <c>AdminWebFactory</c>)
/// so actor/time/persistence collaborators can be fully controlled.
/// </summary>
public class AuditWriterTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;
    private readonly RecordingLogger<AuditWriter> _logger = new();

    public AuditWriterTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(o => o.UseSqlite(_connection));
        services.AddScoped<IAdminAuditStore, EfAdminAuditStore>();
        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.EnsureCreated();
    }

    private AuditWriter CreateWriter(HttpContext? httpContext, TimeProvider? timeProvider = null)
    {
        var accessor = new HttpContextAccessorStub(httpContext);
        return new AuditWriter(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            _logger,
            accessor,
            timeProvider ?? TimeProvider.System);
    }

    private async Task<AuditLogEntry> GetOnlyEntryAsync()
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.AuditLogEntries.SingleAsync();
    }

    private static HttpContext MakeAuthenticatedHttpContext(string subjectId, string name, string? ip = "203.0.113.7")
    {
        var context = new DefaultHttpContext();
        context.TraceIdentifier = "trace-123";
        if (ip != null)
        {
            context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(ip);
        }

        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, subjectId),
            new Claim(ClaimTypes.Name, name),
        }, "TestAuth");
        context.User = new ClaimsPrincipal(identity);
        return context;
    }

    [Fact]
    public async Task WriteAsync_AuthenticatedActor_EnrichesActorSubjectIdAndName()
    {
        var context = MakeAuthenticatedHttpContext("user-42", "alice@sales.local");
        var writer = CreateWriter(context);

        await writer.WriteAsync(new AdminAuditEvent(
            AuditCategories.Client, AuditActions.Create, AuditOutcome.Succeeded, AuditReasonCodes.Succeeded));

        var entry = await GetOnlyEntryAsync();
        Assert.Equal("user-42", entry.ActorSubjectId);
        Assert.Equal("alice@sales.local", entry.ActorName);
    }

    [Fact]
    public async Task WriteAsync_ActorWithOnlySubClaim_FallsBackToSubClaim()
    {
        var context = new DefaultHttpContext();
        var identity = new ClaimsIdentity(new[] { new Claim("sub", "subject-from-sub-claim") }, "TestAuth");
        context.User = new ClaimsPrincipal(identity);
        var writer = CreateWriter(context);

        await writer.WriteAsync(new AdminAuditEvent(
            AuditCategories.Client, AuditActions.Create, AuditOutcome.Succeeded, AuditReasonCodes.Succeeded));

        var entry = await GetOnlyEntryAsync();
        Assert.Equal("subject-from-sub-claim", entry.ActorSubjectId);
    }

    [Fact]
    public async Task WriteAsync_NoHttpContext_DoesNotThrowAndLeavesActorEmpty()
    {
        var writer = CreateWriter(httpContext: null);

        await writer.WriteAsync(new AdminAuditEvent(
            AuditCategories.Client, AuditActions.Create, AuditOutcome.Succeeded, AuditReasonCodes.Succeeded));

        var entry = await GetOnlyEntryAsync();
        Assert.Equal(string.Empty, entry.ActorSubjectId);
        Assert.Equal(string.Empty, entry.ActorName);
    }

    [Fact]
    public async Task WriteAsync_UnauthenticatedUser_LeavesActorEmpty()
    {
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) };
        var writer = CreateWriter(context);

        await writer.WriteAsync(new AdminAuditEvent(
            AuditCategories.Client, AuditActions.Create, AuditOutcome.Succeeded, AuditReasonCodes.Succeeded));

        var entry = await GetOnlyEntryAsync();
        Assert.Equal(string.Empty, entry.ActorSubjectId);
    }

    [Fact]
    public async Task WriteAsync_EnrichesCorrelationIdAndIpAddressFromHttpContext()
    {
        var context = MakeAuthenticatedHttpContext("user-1", "bob@sales.local", ip: "198.51.100.5");
        var writer = CreateWriter(context);

        await writer.WriteAsync(new AdminAuditEvent(
            AuditCategories.Client, AuditActions.Create, AuditOutcome.Succeeded, AuditReasonCodes.Succeeded));

        var entry = await GetOnlyEntryAsync();
        Assert.Equal("trace-123", entry.CorrelationId);
        Assert.Equal("198.51.100.5", entry.IpAddress);
    }

    [Fact]
    public async Task WriteAsync_UsesInjectedTimeProviderForUtcTimestamp()
    {
        var fixedInstant = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);
        var fakeTime = new FakeTimeProvider(fixedInstant);
        var writer = CreateWriter(httpContext: null, timeProvider: fakeTime);

        await writer.WriteAsync(new AdminAuditEvent(
            AuditCategories.Client, AuditActions.Create, AuditOutcome.Succeeded, AuditReasonCodes.Succeeded));

        var entry = await GetOnlyEntryAsync();
        Assert.Equal(fixedInstant.UtcDateTime, entry.Timestamp);
    }

    [Theory]
    [InlineData(AuditOutcome.Succeeded, true)]
    [InlineData(AuditOutcome.Denied, false)]
    [InlineData(AuditOutcome.Failed, false)]
    public async Task WriteAsync_DerivesIsSuccessFromOutcome(AuditOutcome outcome, bool expectedIsSuccess)
    {
        var writer = CreateWriter(httpContext: null);

        await writer.WriteAsync(new AdminAuditEvent(
            AuditCategories.Client, AuditActions.Create, outcome, AuditReasonCodes.Succeeded));

        var entry = await GetOnlyEntryAsync();
        Assert.Equal(outcome, entry.Outcome);
        Assert.Equal(expectedIsSuccess, entry.IsSuccess);
    }

    [Fact]
    public async Task WriteAsync_SerializesOldAndNewValues()
    {
        var writer = CreateWriter(httpContext: null);

        await writer.WriteAsync(new AdminAuditEvent(
            AuditCategories.Client, AuditActions.UpdateBasics, AuditOutcome.Succeeded, AuditReasonCodes.Succeeded,
            OldValues: new AllowedAuditValue("old-name"),
            NewValues: new AllowedAuditValue("new-name")));

        var entry = await GetOnlyEntryAsync();
        Assert.Contains("old-name", entry.OldValuesJson);
        Assert.Contains("new-name", entry.NewValuesJson);
    }

    [Fact]
    public async Task WriteAsync_NullOldAndNewValues_PersistsNullJson()
    {
        var writer = CreateWriter(httpContext: null);

        await writer.WriteAsync(new AdminAuditEvent(
            AuditCategories.Client, AuditActions.Create, AuditOutcome.Succeeded, AuditReasonCodes.Succeeded));

        var entry = await GetOnlyEntryAsync();
        Assert.Null(entry.OldValuesJson);
        Assert.Null(entry.NewValuesJson);
    }

    [Fact]
    public async Task WriteAsync_ArbitraryValueObjects_AreRedactedFromPersistenceAndTelemetry()
    {
        const string forbidden = "submitted-password-or-secret";
        var writer = CreateWriter(httpContext: null);

        await writer.WriteAsync(new AdminAuditEvent(
            AuditCategories.Client,
            AuditActions.UpdateAuthentication,
            AuditOutcome.Denied,
            AuditReasonCodes.ValidationFailed,
            OldValues: new { Password = forbidden },
            NewValues: new { Secret = forbidden }));

        var entry = await GetOnlyEntryAsync();
        Assert.Equal("{\"redacted\":true}", entry.OldValuesJson);
        Assert.Equal("{\"redacted\":true}", entry.NewValuesJson);
        Assert.DoesNotContain(forbidden, string.Join(Environment.NewLine, _logger.Entries.Select(item => item.Message)));
    }

    [Fact]
    public async Task WriteAsync_LogsTelemetryBeforeAttemptingPersistence_EvenWhenPersistenceFails()
    {
        // A scope factory pointed at a database with no schema: the insert will fail,
        // but the structured telemetry log must already have been recorded (Fallback Policy).
        using var brokenConnection = new SqliteConnection("DataSource=:memory:");
        brokenConnection.Open();
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(o => o.UseSqlite(brokenConnection));
        services.AddScoped<IAdminAuditStore, EfAdminAuditStore>();
        using var brokenProvider = services.BuildServiceProvider();
        // Deliberately do NOT call EnsureCreated() - AuditLogEntries table does not exist.

        var logger = new RecordingLogger<AuditWriter>();
        var writer = new AuditWriter(
            brokenProvider.GetRequiredService<IServiceScopeFactory>(),
            logger,
            new HttpContextAccessorStub(null),
            TimeProvider.System);

        var exception = await Record.ExceptionAsync(() => writer.WriteAsync(new AdminAuditEvent(
            AuditCategories.Client, AuditActions.Create, AuditOutcome.Denied, AuditReasonCodes.NotFound,
            TargetId: "client-1", Details: "not found")));

        Assert.Null(exception);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("Client/Create"));
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("Failed to persist audit log entry"));
    }

    [Fact]
    public async Task WriteAsync_PersistenceFailure_DoesNotThrow()
    {
        using var brokenConnection = new SqliteConnection("DataSource=:memory:");
        brokenConnection.Open();
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(o => o.UseSqlite(brokenConnection));
        services.AddScoped<IAdminAuditStore, EfAdminAuditStore>();
        using var brokenProvider = services.BuildServiceProvider();

        var writer = new AuditWriter(
            brokenProvider.GetRequiredService<IServiceScopeFactory>(),
            new RecordingLogger<AuditWriter>(),
            new HttpContextAccessorStub(null),
            TimeProvider.System);

        var exception = await Record.ExceptionAsync(() => writer.WriteAsync(new AdminAuditEvent(
            AuditCategories.Client, AuditActions.Create, AuditOutcome.Succeeded, AuditReasonCodes.Succeeded)));

        Assert.Null(exception);
    }

    [Theory]
    [InlineData(AuditOutcome.Succeeded, LogLevel.Information)]
    [InlineData(AuditOutcome.Denied, LogLevel.Warning)]
    [InlineData(AuditOutcome.Failed, LogLevel.Error)]
    public async Task WriteAsync_LogLevelMatchesOutcome(AuditOutcome outcome, LogLevel expectedLevel)
    {
        var logger = new RecordingLogger<AuditWriter>();
        var writer = new AuditWriter(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            logger,
            new HttpContextAccessorStub(null),
            TimeProvider.System);

        await writer.WriteAsync(new AdminAuditEvent(
            AuditCategories.Client, AuditActions.Create, outcome, AuditReasonCodes.Succeeded));

        Assert.Contains(logger.Entries, e => e.Level == expectedLevel);
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
    }

    private sealed class HttpContextAccessorStub : IHttpContextAccessor
    {
        public HttpContextAccessorStub(HttpContext? context) => HttpContext = context;
        public HttpContext? HttpContext { get; set; }
    }

    private sealed record AllowedAuditValue(string Name) : IAuditValue;

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
