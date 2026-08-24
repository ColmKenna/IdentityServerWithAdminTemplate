using Duende.IdentityServer.EntityFramework.DbContexts;
using IdentityServerProject.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace IdentityServerProject.Admin.Tests.Infrastructure;

[Collection(Task02SqlServerCollection.Name)]
public sealed class DatabaseSchemaReadinessTests
{
    private readonly Task02SqlServerFactory _factory;

    public DatabaseSchemaReadinessTests(Task02SqlServerFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task FullyMigratedDatabases_PassReadOnlyReadinessValidation()
    {
        await _factory.RunInScopeAsync(services => services
            .GetRequiredService<IDatabaseSchemaReadinessValidator>()
            .EnsureReadyAsync());
    }

    [Fact]
    public async Task FullyMigratedIdentityDatabase_ContainsAuditFilterIndexes()
    {
        var expectedIndexes = new[]
        {
            "IX_AuditLogEntries_ActorSubjectId_Timestamp",
            "IX_AuditLogEntries_TargetId_Timestamp",
            "IX_AuditLogEntries_Category_Action_Timestamp",
            "IX_AuditLogEntries_CorrelationId",
        };

        await using var connection = new SqlConnection(_factory.IdentityConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "SELECT [name] FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.AuditLogEntries');",
            connection);
        await using var reader = await command.ExecuteReaderAsync();

        var actualIndexes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync())
        {
            actualIndexes.Add(reader.GetString(0));
        }

        foreach (var expectedIndex in expectedIndexes)
        {
            Assert.Contains(expectedIndex, actualIndexes);
        }
    }

    [Fact]
    public async Task LegacySchemaWithoutHistory_FailsBeforeSeedAndNamesRequiredBundle()
    {
        var databaseName = $"Task06_Pending_{Guid.NewGuid():N}";
        var builder = new SqlConnectionStringBuilder(_factory.IdentityConnectionString)
        {
            InitialCatalog = databaseName
        };
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(builder.ConnectionString, sql =>
                sql.MigrationsAssembly(typeof(Program).Assembly.FullName))
            .Options;

        await using var pendingApplicationDb = new ApplicationDbContext(options);
        try
        {
            // Models the old EnsureCreated deployment: current tables exist, but migration
            // history does not. Test setup may create schema; production startup never does.
            await pendingApplicationDb.Database.EnsureCreatedAsync();

            await _factory.RunInScopeAsync(async services =>
            {
                var validator = new DatabaseSchemaReadinessValidator(
                    pendingApplicationDb,
                    services.GetRequiredService<ConfigurationDbContext>(),
                    services.GetRequiredService<PersistedGrantDbContext>(),
                    NullLogger<DatabaseSchemaReadinessValidator>.Instance);

                var error = await Assert.ThrowsAsync<InvalidOperationException>(
                    () => validator.EnsureReadyAsync());

                Assert.Contains(nameof(ApplicationDbContext), error.Message, StringComparison.Ordinal);
                Assert.Contains("identity", error.Message, StringComparison.Ordinal);
                Assert.False(await pendingApplicationDb.Users.AnyAsync());
            });
        }
        finally
        {
            await pendingApplicationDb.Database.EnsureDeletedAsync();
        }
    }
}
