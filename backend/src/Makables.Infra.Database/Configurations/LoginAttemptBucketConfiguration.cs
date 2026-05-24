using Makables.Core.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Makables.Infra.Database.Configurations;

internal sealed class LoginAttemptBucketEntityConfiguration : IEntityTypeConfiguration<LoginAttemptBucket>
{
    public void Configure(EntityTypeBuilder<LoginAttemptBucket> builder)
    {
        builder.ToTable("login_attempt_buckets");

        builder.HasKey(b => b.Id);
        // Id == normalized email (320-char limit per the User table).
        builder.Property(b => b.Id).HasColumnName("email_normalized").HasMaxLength(320).IsRequired();

        builder.Ignore(b => b.EmailNormalized); // computed property over Id

        builder.Property(b => b.AttemptCount).HasColumnName("attempt_count").IsRequired();
        builder.Property(b => b.LockedUntil).HasColumnName("locked_until");
        builder.Property(b => b.LastAttemptAt).HasColumnName("last_attempt_at").IsRequired();

        builder.Property(b => b.IsActive).HasColumnName("is_active").IsRequired();
        // No CountryCode / created_by / etc. — buckets inherit BaseEntity
        // only (anti-abuse infra, not transactional data; see entity doc).
    }
}
