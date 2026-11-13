using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using IdentityServerProject.Mobile.Core.Domain;

namespace IdentityServerProject.Mobile.Core.Data;

/// <summary>Key/value rows for sync bookkeeping that is not part of any synced record.</summary>
public sealed class SyncMetadata
{
    public string Key { get; set; } = string.Empty;
    public string? Value { get; set; }
}

/// <summary>
///     The on-device schema. Relationships between synced entities are plain id properties with no
///     database foreign key: during a sync pull a child can legitimately arrive before its parent, and a
///     constraint would reject it. The services keep relationships consistent instead.
/// </summary>
public sealed class MobileDbContext(DbContextOptions<MobileDbContext> options) : DbContext(options)
{
    public DbSet<Note> Notes => Set<Note>();
    public DbSet<SyncMetadata> SyncMetadata => Set<SyncMetadata>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        // SQLite has no native timestamp type, and EF cannot ORDER BY its default DateTimeOffset text
        // form. UTC ticks sort correctly and round-trip exactly.
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UtcTicksConverter>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureSyncEntity<Note>(modelBuilder);

        modelBuilder.Entity<Note>(entity =>
        {
            // SQLite's default collation is byte order, which files "apple" after "Zebra".
            entity.Property(n => n.Title).UseCollation("NOCASE");
            entity.HasIndex(n => n.UpdatedAt);
        });

        modelBuilder.Entity<SyncMetadata>().HasKey(m => m.Key);
    }

    private static void ConfigureSyncEntity<T>(ModelBuilder modelBuilder) where T : SyncEntity
    {
        modelBuilder.Entity<T>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.HasIndex(e => e.SyncStatus);

            // Tombstones stay in the table until the server acknowledges them, but no screen should
            // ever see one. The sync engine opts out with IgnoreQueryFilters().
            entity.HasQueryFilter(e => !e.IsDeleted);
        });
    }

    private sealed class UtcTicksConverter() : ValueConverter<DateTimeOffset, long>(
        value => value.UtcTicks,
        ticks => new DateTimeOffset(ticks, TimeSpan.Zero));
}
