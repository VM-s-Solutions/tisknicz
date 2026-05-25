using Makables.Core.Domain.Email;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Makables.Infra.Database.Configurations;

internal sealed class EmailTemplateEntityConfiguration : IEntityTypeConfiguration<EmailTemplate>
{
    public void Configure(EntityTypeBuilder<EmailTemplate> builder)
    {
        builder.ToTable("email_templates");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id").HasMaxLength(40).IsRequired();

        // Stored as the enum name (e.g. "AuthMagicLink") so DB grep / seed
        // inserts are readable. One row per type is the design — partial
        // unique index gates that on active rows.
        builder.Property(t => t.Type)
            .HasColumnName("type")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();
        builder.HasIndex(t => t.Type).IsUnique().HasFilter("is_active");

        builder.Property(t => t.ProviderTemplateId)
            .HasColumnName("provider_template_id").HasMaxLength(100).IsRequired();
        builder.Property(t => t.FromAddress).HasColumnName("from_address").HasMaxLength(320);
        builder.Property(t => t.FromName).HasColumnName("from_name").HasMaxLength(200);
        builder.Property(t => t.ReplyToAddress).HasColumnName("reply_to_address").HasMaxLength(320);

        ConfigureAuditable(builder);
    }

    private static void ConfigureAuditable(EntityTypeBuilder<EmailTemplate> b)
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
