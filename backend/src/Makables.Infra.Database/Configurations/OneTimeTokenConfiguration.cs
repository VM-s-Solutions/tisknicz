using Makables.Core.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Makables.Infra.Database.Configurations;

internal sealed class OneTimeTokenEntityConfiguration : IEntityTypeConfiguration<OneTimeToken>
{
    public void Configure(EntityTypeBuilder<OneTimeToken> builder)
    {
        builder.ToTable("one_time_tokens");

        // PK is the SHA-256 hex of the raw token. Lookup is a primary-key
        // probe; we never store or query by the raw value.
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("token_hash").HasMaxLength(128).IsRequired();

        builder.Property(t => t.UserId).HasColumnName("user_id").HasMaxLength(40).IsRequired();

        builder.Property(t => t.Purpose)
            .HasColumnName("purpose")
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(t => t.ExpiresAt).HasColumnName("expires_at").IsRequired();
        builder.Property(t => t.ConsumedAt).HasColumnName("consumed_at");
        builder.Property(t => t.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(t => t.IpAddress).HasColumnName("ip_address").HasMaxLength(64);

        // OneTimeToken is never soft-deleted (it's consumed, then purged
        // by the T-0114 cleanup Function). The inherited BaseEntity.IsActive
        // column would be dead weight; suppress it via Ignore so the table
        // doesn't carry a column the application never reads or writes.
        builder.Ignore(t => t.IsActive);

        // Per-user / per-purpose lookups for rate-limit + invalidate flows.
        builder.HasIndex(t => new { t.UserId, t.Purpose, t.CreatedAt });
        // Cleanup-by-expiry index: the data-retention Function in T-0114
        // can purge tokens past their ExpiresAt without a table scan.
        builder.HasIndex(t => t.ExpiresAt);
    }
}
