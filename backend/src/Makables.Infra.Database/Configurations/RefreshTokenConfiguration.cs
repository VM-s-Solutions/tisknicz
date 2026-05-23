using Makables.Core.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Makables.Infra.Database.Configurations;

internal sealed class RefreshTokenEntityConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id").HasMaxLength(40).IsRequired();

        builder.Property(t => t.UserId).HasColumnName("user_id").HasMaxLength(40).IsRequired();
        builder.HasIndex(t => t.UserId);

        // SHA-256 hex digest is 64 chars; reserve 128 for future migration to
        // longer hashes (SHA-512, BLAKE3) without altering the column type.
        builder.Property(t => t.TokenHash).HasColumnName("token_hash").HasMaxLength(128).IsRequired();
        builder.HasIndex(t => t.TokenHash).IsUnique();

        builder.Property(t => t.ExpiresAt).HasColumnName("expires_at").IsRequired();
        builder.Property(t => t.RevokedAt).HasColumnName("revoked_at");
        builder.Property(t => t.ReplacedByTokenId).HasColumnName("replaced_by_token_id").HasMaxLength(40);

        builder.Property(t => t.FamilyId).HasColumnName("family_id").HasMaxLength(40).IsRequired();
        builder.HasIndex(t => t.FamilyId);

        builder.Property(t => t.UserAgent).HasColumnName("user_agent").HasMaxLength(500);
        builder.Property(t => t.IpAddress).HasColumnName("ip_address").HasMaxLength(64);

        ConfigureAuditable(builder);
    }

    private static void ConfigureAuditable(EntityTypeBuilder<RefreshToken> b)
    {
        b.Property(t => t.CountryCode).HasColumnName("country_code").HasMaxLength(2).IsRequired();
        b.Property(t => t.IsActive).HasColumnName("is_active").IsRequired();
        b.Property(t => t.CreatedBy).HasColumnName("created_by").HasMaxLength(200);
        b.Property(t => t.CreatedAt).HasColumnName("created_at");
        b.Property(t => t.UpdatedBy).HasColumnName("updated_by").HasMaxLength(200);
        b.Property(t => t.UpdatedAt).HasColumnName("updated_at");
        b.Property(t => t.DeactivatedBy).HasColumnName("deactivated_by").HasMaxLength(200);
        b.Property(t => t.DeactivatedAt).HasColumnName("deactivated_at");
    }
}
