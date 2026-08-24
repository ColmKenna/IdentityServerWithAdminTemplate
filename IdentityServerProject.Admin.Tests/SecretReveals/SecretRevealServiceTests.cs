using System.Security.Claims;
using System.Text.Json;
using IdentityServerProject.Data;
using IdentityServerProject.Data.Adapters;
using IdentityServerProject.Services.AuditLogs;
using IdentityServerProject.Services.SecretReveals;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace IdentityServerProject.Admin.Tests.SecretReveals;

public sealed class SecretRevealServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 8, 13, 12, 0, 0, TimeSpan.Zero);
    private readonly Microsoft.Data.Sqlite.SqliteConnection _connection;
    private readonly ApplicationDbContext _dbContext;
    private readonly HttpContextAccessor _httpContextAccessor = new();
    private readonly FakeTimeProvider _timeProvider = new(Now);
    private readonly RecordingAuditWriter _auditWriter = new();
    private readonly RecordingLogger _logger = new();
    private readonly SecretRevealService _service;

    public SecretRevealServiceTests()
    {
        _connection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _dbContext = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options);
        _dbContext.Database.EnsureCreated();
        SetActor("actor-a");
        _service = new SecretRevealService(
            new EfSecretRevealStore(_dbContext),
            new EphemeralDataProtectionProvider(),
            _httpContextAccessor,
            _timeProvider,
            _auditWriter,
            _logger);
    }

    [Fact]
    public async Task IssueAndConsume_StoresOnlyDigestAndProtectedPayload_AndRevealsExactlyOnce()
    {
        const string plaintext = "top-secret-value-that-must-not-leak";
        var ticket = await _service.IssueAsync(
            SecretRevealPurpose.ClientCreated, " client-a ", plaintext);

        var stored = await _dbContext.SecretRevealRecords.AsNoTracking().SingleAsync();
        Assert.Equal(32, stored.HandleDigest.Length);
        Assert.Equal("actor-a", stored.ActorSubjectId);
        Assert.Equal("client-a", stored.TargetId);
        Assert.Equal(Now, stored.CreatedUtc);
        Assert.Equal(Now.AddMinutes(5), stored.ExpiresUtc);
        Assert.DoesNotContain(plaintext, stored.ProtectedPayload, StringComparison.Ordinal);
        Assert.DoesNotContain(ticket.Handle, stored.ProtectedPayload, StringComparison.Ordinal);
        Assert.All(new[] { stored.ActorSubjectId, stored.Purpose, stored.TargetId, stored.ProtectedPayload },
            value => Assert.DoesNotContain(ticket.Handle, value, StringComparison.Ordinal));

        var first = await _service.ConsumeAsync(
            SecretRevealPurpose.ClientCreated, "client-a", ticket.Handle);
        var second = await _service.ConsumeAsync(
            SecretRevealPurpose.ClientCreated, "client-a", ticket.Handle);

        Assert.Equal(SecretRevealConsumeStatus.Revealed, first.Status);
        Assert.Equal(plaintext, first.Plaintext);
        Assert.Equal(SecretRevealConsumeStatus.Unavailable, second.Status);
        Assert.Null(second.Plaintext);
        Assert.Empty(_dbContext.SecretRevealRecords);
        AssertAuditIsRedacted(plaintext, ticket.Handle, stored.ProtectedPayload);
    }

    [Fact]
    public async Task WrongActorPurposeOrTarget_IsUnavailableAndCannotInvalidateLegitimateReveal()
    {
        var ticket = await _service.IssueAsync(
            SecretRevealPurpose.ClientSecretGenerated, "client-a", "bound-secret");

        SetActor("actor-b");
        AssertUnavailable(await _service.ConsumeAsync(
            SecretRevealPurpose.ClientSecretGenerated, "client-a", ticket.Handle));
        Assert.Single(_dbContext.SecretRevealRecords);

        SetActor("actor-a");
        AssertUnavailable(await _service.ConsumeAsync(
            SecretRevealPurpose.ApiResourceSecretGenerated, "client-a", ticket.Handle));
        Assert.Single(_dbContext.SecretRevealRecords);

        AssertUnavailable(await _service.ConsumeAsync(
            SecretRevealPurpose.ClientSecretGenerated, "client-b", ticket.Handle));
        Assert.Single(_dbContext.SecretRevealRecords);

        var legitimate = await _service.ConsumeAsync(
            SecretRevealPurpose.ClientSecretGenerated, "client-a", ticket.Handle);
        Assert.Equal(SecretRevealConsumeStatus.Revealed, legitimate.Status);
        Assert.Equal("bound-secret", legitimate.Plaintext);
    }

    [Fact]
    public async Task ExpiredMalformedMissingAndUnauthenticatedHandles_AreGenericallyUnavailable()
    {
        var ticket = await _service.IssueAsync(
            SecretRevealPurpose.ApiResourceSecretGenerated, "sales.api", "expiring-secret");
        _timeProvider.Advance(TimeSpan.FromMinutes(5));

        AssertUnavailable(await _service.ConsumeAsync(
            SecretRevealPurpose.ApiResourceSecretGenerated, "sales.api", ticket.Handle));
        Assert.Empty(_dbContext.SecretRevealRecords);

        AssertUnavailable(await _service.ConsumeAsync(
            SecretRevealPurpose.ApiResourceSecretGenerated, "sales.api", "not base64url!"));
        AssertUnavailable(await _service.ConsumeAsync(
            SecretRevealPurpose.ApiResourceSecretGenerated,
            "sales.api",
            Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(new byte[32])));

        _httpContextAccessor.HttpContext = new DefaultHttpContext();
        AssertUnavailable(await _service.ConsumeAsync(
            SecretRevealPurpose.ApiResourceSecretGenerated, "sales.api", ticket.Handle));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.IssueAsync(
            SecretRevealPurpose.ApiResourceSecretGenerated, "sales.api", "secret"));
    }

    [Fact]
    public async Task UnprotectFailure_IsUnavailableAndDoesNotRecreateConsumedRecord()
    {
        var ticket = await _service.IssueAsync(
            SecretRevealPurpose.ClientSecretGenerated, "client-a", "do-not-log-me");
        var record = await _dbContext.SecretRevealRecords.SingleAsync();
        record.ProtectedPayload = "invalid-protected-payload";
        await _dbContext.SaveChangesAsync();

        var result = await _service.ConsumeAsync(
            SecretRevealPurpose.ClientSecretGenerated, "client-a", ticket.Handle);

        AssertUnavailable(result);
        Assert.Empty(_dbContext.SecretRevealRecords);
        Assert.Contains(_auditWriter.Events, audit =>
            audit.Action == AuditActions.Consume
            && audit.Outcome == AuditOutcome.Failed
            && audit.ReasonCode == AuditReasonCodes.PersistenceFailure);
        AssertAuditIsRedacted("do-not-log-me", ticket.Handle, "invalid-protected-payload");
        AssertLogsAreRedacted("do-not-log-me", ticket.Handle, "invalid-protected-payload");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Issue_InvalidTarget_IsRejected(string targetId)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.IssueAsync(
            SecretRevealPurpose.ClientCreated, targetId, "secret"));
        Assert.Empty(_dbContext.SecretRevealRecords);
    }

    [Fact]
    public async Task Issue_EmptyPlaintext_IsRejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.IssueAsync(
            SecretRevealPurpose.ClientCreated, "client-a", string.Empty));
        Assert.Empty(_dbContext.SecretRevealRecords);
    }

    private void SetActor(string subjectId)
    {
        _httpContextAccessor.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, subjectId) }, "SecretRevealTests"))
        };
    }

    private static void AssertUnavailable(SecretRevealConsumeResult result)
    {
        Assert.Equal(SecretRevealConsumeStatus.Unavailable, result.Status);
        Assert.Null(result.Plaintext);
    }

    private void AssertAuditIsRedacted(params string[] forbiddenValues)
    {
        var auditJson = JsonSerializer.Serialize(_auditWriter.Events);
        foreach (var forbiddenValue in forbiddenValues)
        {
            Assert.DoesNotContain(forbiddenValue, auditJson, StringComparison.Ordinal);
        }
    }

    private void AssertLogsAreRedacted(params string[] forbiddenValues)
    {
        var logText = string.Join(Environment.NewLine, _logger.Messages);
        foreach (var forbiddenValue in forbiddenValues)
        {
            Assert.DoesNotContain(forbiddenValue, logText, StringComparison.Ordinal);
        }
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }

    private sealed class RecordingAuditWriter : IAuditWriter
    {
        public List<AdminAuditEvent> Events { get; } = new();

        public Task WriteAsync(AdminAuditEvent auditEvent, CancellationToken cancellationToken = default)
        {
            Events.Add(auditEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingLogger : ILogger<SecretRevealService>
    {
        public List<string> Messages { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
