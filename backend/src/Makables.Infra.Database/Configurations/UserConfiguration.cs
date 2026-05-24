using Makables.Core.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Makables.Infra.Database.Configurations;

internal sealed class UserEntityConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");

        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).HasColumnName("id").HasMaxLength(40).IsRequired();

        builder.Property(u => u.Email).HasColumnName("email").HasMaxLength(320).IsRequired();
        builder.Property(u => u.EmailNormalized).HasColumnName("email_normalized").HasMaxLength(320).IsRequired();
        // Partial unique index on (email_normalized) WHERE is_active. This lets
        // an admin GDPR purge leave a soft-deleted row in place while a new
        // user can still register the same email AFTER an explicit purge.
        // Application-side re-registration blocking lives in
        // IUserRepository.EmailExistsAsync (which IgnoreQueryFilters); the
        // partial index keeps the DB consistent for ACTIVE rows.
        // Reviewer T-0020 BLOCKER B-2 fix.
        builder.HasIndex(u => u.EmailNormalized)
            .IsUnique()
            .HasFilter("is_active");

        builder.Property(u => u.PasswordHash).HasColumnName("password_hash").HasMaxLength(500);
        builder.Property(u => u.EmailConfirmedAt).HasColumnName("email_confirmed_at");

        builder.Property(u => u.Role)
            .HasColumnName("role")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(u => u.FullName).HasColumnName("full_name").HasMaxLength(200).IsRequired();
        builder.Property(u => u.Phone).HasColumnName("phone").HasMaxLength(40);
        builder.Property(u => u.CountryCodePrimary).HasColumnName("country_code_primary").HasMaxLength(2).IsRequired();

        // Google's `sub` claim is documented as up to 255 chars (reviewer T-0020 N-1).
        builder.Property(u => u.GoogleSub).HasColumnName("google_sub").HasMaxLength(255);
        builder.HasIndex(u => u.GoogleSub).IsUnique().HasFilter("google_sub IS NOT NULL");

        builder.Property(u => u.FailedLoginCount).HasColumnName("failed_login_count").IsRequired();
        builder.Property(u => u.LockedUntil).HasColumnName("locked_until");

        ConfigureAuditable(builder);
    }

    private static void ConfigureAuditable(EntityTypeBuilder<User> b)
    {
        b.Property(u => u.CountryCode).HasColumnName("country_code").HasMaxLength(2).IsRequired();
        b.Property(u => u.IsActive).HasColumnName("is_active").IsRequired();
        b.Property(u => u.CreatedBy).HasColumnName("created_by").HasMaxLength(200);
        b.Property(u => u.CreatedAt).HasColumnName("created_at");
        b.Property(u => u.UpdatedBy).HasColumnName("updated_by").HasMaxLength(200);
        b.Property(u => u.UpdatedAt).HasColumnName("updated_at");
        b.Property(u => u.DeactivatedBy).HasColumnName("deactivated_by").HasMaxLength(200);
        b.Property(u => u.DeactivatedAt).HasColumnName("deactivated_at");
    }
}
