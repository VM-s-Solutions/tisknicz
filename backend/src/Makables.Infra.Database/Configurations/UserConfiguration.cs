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
        // Unique on (email_normalized, is_active=true) would be ideal but EF Core
        // doesn't natively model partial indexes; the application enforces
        // re-registration blocking via IUserRepository.EmailExistsAsync which
        // includes soft-deleted rows.
        builder.HasIndex(u => u.EmailNormalized).IsUnique();

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

        builder.Property(u => u.GoogleSub).HasColumnName("google_sub").HasMaxLength(100);
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
