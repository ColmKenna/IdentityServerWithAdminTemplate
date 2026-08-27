using System.Globalization;
using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Options;
using IdentityServerProject.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace IdentityServerProject.Admin.Tests.Infrastructure;

[Collection(Task02SqlServerCollection.Name)]
public sealed class MigrationAdoptionSqlServerTests
{
    private const string ProductVersion = "10.0.10";

    [Theory]
    [InlineData("ApplicationDbContext", "20260810232228_AddAuditLogEnhancements",
        "B47BFC21BEAE809E7CB40E84AABA3252061DF51755E72F43AB983C18FBAC745F", 106)]
    [InlineData("ConfigurationDbContext", "20260812232901_InitialDuendeConfiguration",
        "C66387366F23AAD1A5482E061F23FECA352C5D08230EB6D2F1948EFE1B972313", 429)]
    [InlineData("PersistedGrantDbContext", "20260812232913_InitialDuendeOperational",
        "A79B90BFE13438069C6EEDDCDA254FC9614016628BE5F2B01BDDBB18EC3C3096", 127)]
    public async Task ExactBaseline_AdoptsIdempotently_ThenMigratesToCurrent(
        string contextName,
        string baselineMigration,
        string fingerprint,
        int itemCount)
    {
        string databaseName = $"Task06_Adoption_{Guid.NewGuid():N}";
        string connectionString = Task02SqlServerFactory.BuildConnectionString(databaseName);
        (DbContext context, ServiceProvider? provider) = CreateContext(contextName, connectionString);
        await using (context)
        using (provider)
            try
            {
                await context.GetService<IMigrator>().MigrateAsync(baselineMigration);
                await context.Database.ExecuteSqlRawAsync("DROP TABLE dbo.__EFMigrationsHistory;");

                await ExecuteAdoptionAsync(connectionString, contextName, baselineMigration, fingerprint, itemCount);
                await ExecuteAdoptionAsync(connectionString, contextName, baselineMigration, fingerprint, itemCount);

                int historyCount = await context.Database
                    .SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM dbo.__EFMigrationsHistory")
                    .SingleAsync();
                Assert.Equal(1, historyCount);

                await context.Database.MigrateAsync();
                Assert.Empty(await context.Database.GetPendingMigrationsAsync());
            }
            finally
            {
                await context.Database.EnsureDeletedAsync();
            }
    }

    [Fact]
    public async Task PartialBaseline_IsRejectedWithoutHistoryStamp()
    {
        const string contextName = "ApplicationDbContext";
        const string baselineMigration = "20260810232228_AddAuditLogEnhancements";
        const string fingerprint = "B47BFC21BEAE809E7CB40E84AABA3252061DF51755E72F43AB983C18FBAC745F";
        string databaseName = $"Task06_Partial_{Guid.NewGuid():N}";
        string connectionString = Task02SqlServerFactory.BuildConnectionString(databaseName);
        (DbContext context, ServiceProvider? provider) = CreateContext(contextName, connectionString);
        await using (context)
        using (provider)
            try
            {
                await context.GetService<IMigrator>().MigrateAsync(baselineMigration);
                await context.Database.ExecuteSqlRawAsync(
                    "DROP TABLE dbo.__EFMigrationsHistory; " +
                    "DROP INDEX IX_AspNetUserClaims_UserId ON dbo.AspNetUserClaims;");

                SqlException error = await Assert.ThrowsAsync<SqlException>(() => ExecuteAdoptionAsync(
                    connectionString,
                    contextName,
                    baselineMigration,
                    fingerprint,
                    106));

                Assert.Equal(51002, error.Number);
                int historyExists = await context.Database.SqlQueryRaw<int>(
                        "SELECT CASE WHEN OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NULL THEN 0 ELSE 1 END AS Value")
                    .SingleAsync();
                Assert.Equal(0, historyExists);
            }
            finally
            {
                await context.Database.EnsureDeletedAsync();
            }
    }

    private static (DbContext Context, ServiceProvider? Provider) CreateContext(
        string contextName,
        string connectionString)
    {
        switch (contextName)
        {
            case "ApplicationDbContext":
                return (new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                    .UseSqlServer(connectionString, ConfigureMigrations)
                    .Options), null);

            case "ConfigurationDbContext":
            {
                ServiceProvider provider = new ServiceCollection()
                    .AddSingleton(new ConfigurationStoreOptions())
                    .BuildServiceProvider();
                DbContextOptions<ConfigurationDbContext> options = new DbContextOptionsBuilder<ConfigurationDbContext>()
                    .UseApplicationServiceProvider(provider)
                    .UseSqlServer(connectionString, ConfigureMigrations)
                    .Options;
                return (new ConfigurationDbContext(options), provider);
            }

            case "PersistedGrantDbContext":
            {
                ServiceProvider provider = new ServiceCollection()
                    .AddSingleton(new OperationalStoreOptions())
                    .BuildServiceProvider();
                DbContextOptions<PersistedGrantDbContext> options =
                    new DbContextOptionsBuilder<PersistedGrantDbContext>()
                        .UseApplicationServiceProvider(provider)
                        .UseSqlServer(connectionString, ConfigureMigrations)
                        .Options;
                return (new PersistedGrantDbContext(options), provider);
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(contextName));
        }
    }

    private static void ConfigureMigrations(SqlServerDbContextOptionsBuilder sql) =>
        sql.MigrationsAssembly(typeof(Program).Assembly.FullName);

    private static async Task ExecuteAdoptionAsync(
        string connectionString,
        string contextName,
        string baselineMigration,
        string fingerprint,
        int itemCount)
    {
        string scriptPath = Path.Combine(
            AppContext.BaseDirectory,
            "Data",
            "AdoptionScripts",
            "MigrationAdoption.sql");
        string script = await File.ReadAllTextAsync(scriptPath);
        script = script
            .Replace("$(ContextName)", contextName, StringComparison.Ordinal)
            .Replace("$(BaselineMigrationId)", baselineMigration, StringComparison.Ordinal)
            .Replace("$(ProductVersion)", ProductVersion, StringComparison.Ordinal)
            .Replace("$(ExpectedFingerprint)", fingerprint, StringComparison.Ordinal)
            .Replace("$(ExpectedItemCount)", itemCount.ToString(CultureInfo.InvariantCulture),
                StringComparison.Ordinal);

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(script, connection) { CommandTimeout = 120 };
        await command.ExecuteNonQueryAsync();
    }
}