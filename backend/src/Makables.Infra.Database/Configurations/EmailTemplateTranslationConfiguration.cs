using Makables.Core.Domain.Email;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Makables.Infra.Database.Configurations;

internal sealed class EmailTemplateTranslationEntityConfiguration
    : IEntityTypeConfiguration<EmailTemplateTranslation>
{
    public void Configure(EntityTypeBuilder<EmailTemplateTranslation> builder)
    {
        builder.ToTable("email_template_translations");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id").HasMaxLength(40).IsRequired();

        builder.Property(t => t.EmailTemplateId).HasColumnName("email_template_id").HasMaxLength(40).IsRequired();
        builder.Property(t => t.LanguageCode).HasColumnName("language_code").HasMaxLength(16).IsRequired();

        builder.Property(t => t.Subject).HasColumnName("subject").HasMaxLength(300).IsRequired();
        // Plain-text body — used as multipart/alternative + for admin previews.
        // No max length; lengths are bounded by reasonable email copy (~10 KB).
        builder.Property(t => t.PlainTextBody).HasColumnName("plain_text_body").IsRequired();

        // One translation per (template, language) on active rows.
        builder.HasIndex(t => new { t.EmailTemplateId, t.LanguageCode })
            .IsUnique()
            .HasFilter("is_active");

        ConfigureAuditable(builder);
    }

    private static void ConfigureAuditable(EntityTypeBuilder<EmailTemplateTranslation> b)
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
