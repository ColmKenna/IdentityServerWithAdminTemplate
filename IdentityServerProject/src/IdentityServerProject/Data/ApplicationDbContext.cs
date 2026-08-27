using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace IdentityServerProject.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>, IDataProtectionKeyContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();
    public DbSet<SecretRevealRecord> SecretRevealRecords => Set<SecretRevealRecord>();
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<AuditLogEntry>(entity =>
        {
            entity.Property(x => x.ActorSubjectId).HasMaxLength(450).IsRequired();
            entity.Property(x => x.Category).HasMaxLength(450).IsRequired();
            entity.Property(x => x.Action).HasMaxLength(450).IsRequired();
            entity.Property(x => x.CorrelationId).HasMaxLength(450).IsRequired();
            entity.Property(x => x.TargetId).HasMaxLength(450);
            entity.Property(x => x.Outcome).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(x => x.ReasonCode).HasMaxLength(64).IsRequired();
            entity.HasIndex(x => x.Timestamp);
            entity.HasIndex(x => new { x.ActorSubjectId, x.Timestamp });
            entity.HasIndex(x => new { x.TargetId, x.Timestamp });
            entity.HasIndex(x => new { x.Category, x.Action, x.Timestamp });
            entity.HasIndex(x => x.CorrelationId);
        });

        builder.Entity<SecretRevealRecord>(entity =>
        {
            entity.ToTable("SecretRevealRecords");
            entity.Property(x => x.HandleDigest).HasColumnType("binary(32)").IsRequired();
            entity.Property(x => x.ActorSubjectId).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Purpose).HasMaxLength(64).IsRequired();
            entity.Property(x => x.TargetId).HasMaxLength(200).IsRequired();
            entity.Property(x => x.ProtectedPayload).IsRequired();
            entity.HasIndex(x => x.HandleDigest).IsUnique();
            entity.HasIndex(x => x.ActorSubjectId);
            entity.HasIndex(x => x.ExpiresUtc);
        });
    }
}