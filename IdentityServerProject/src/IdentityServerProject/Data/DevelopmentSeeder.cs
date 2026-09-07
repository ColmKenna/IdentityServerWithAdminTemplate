using Duende.IdentityServer.EntityFramework.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace IdentityServerProject.Data;

/// <summary>
///     Guards seeding and automated schema migration so they only ever execute in the
///     Development environment. Production and Staging apply reviewed migration bundles
///     out of band and rely on <see cref="IDatabaseSchemaReadinessValidator" /> to refuse to
///     start against a schema those bundles have not yet reached.
/// </summary>
public static class DevelopmentSeeder
{
    /// <summary>
    ///     Applies pending migrations to all three contexts so a fresh clone reaches a running
    ///     host with a single <c>dotnet run --project AppHost</c>, without a manual migration
    ///     bundle step. <c>MigrateAsync</c> only applies migrations not yet in the target
    ///     database's history table, so calling this again against an already-current database
    ///     is a safe no-op.
    /// </summary>
    public static async Task ApplyDevelopmentMigrationsAsync(
        IHostEnvironment environment,
        ApplicationDbContext identityDb,
        ConfigurationDbContext configDb,
        PersistedGrantDbContext operationalDb)
    {
        if (!environment.IsDevelopment()) return;

        await identityDb.Database.MigrateAsync();
        await configDb.Database.MigrateAsync();
        await operationalDb.Database.MigrateAsync();
    }

    public static async Task SeedIfDevelopmentAsync(IHostEnvironment environment, Func<Task> seed)
    {
        if (!environment.IsDevelopment()) return;

        await seed();
    }
}
