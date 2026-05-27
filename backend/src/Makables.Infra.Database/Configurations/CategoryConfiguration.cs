using Makables.Core.Domain.Categories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Makables.Infra.Database.Configurations;

internal sealed class CategoryEntityConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("categories");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id").HasMaxLength(40).IsRequired();

        builder.Property(c => c.Name).HasColumnName("name").HasMaxLength(100).IsRequired();

        builder.Property(c => c.Slug).HasColumnName("slug").HasMaxLength(100).IsRequired();
        // Partial unique index so a soft-deleted category frees its slug.
        // Backs the slug-uniqueness pre-check on CreateCategory; the
        // UniqueConstraintTranslator maps the constraint name back to
        // CategorySlugAlreadyExists on a TOCTOU race.
        builder.HasIndex(c => c.Slug)
            .IsUnique()
            .HasDatabaseName("ix_categories_slug")
            .HasFilter("is_active");

        builder.Property(c => c.Icon).HasColumnName("icon").HasMaxLength(64);
        builder.Property(c => c.Description).HasColumnName("description").HasMaxLength(500);
        builder.Property(c => c.SortOrder).HasColumnName("sort_order").IsRequired();

        ConfigureAuditable(builder);
    }

    private static void ConfigureAuditable(EntityTypeBuilder<Category> b)
    {
        b.Property(c => c.CountryCode).HasColumnName("country_code").HasMaxLength(2).IsRequired();
        b.Property(c => c.IsActive).HasColumnName("is_active").IsRequired();
        b.Property(c => c.CreatedBy).HasColumnName("created_by").HasMaxLength(200);
        b.Property(c => c.CreatedAt).HasColumnName("created_at");
        b.Property(c => c.UpdatedBy).HasColumnName("updated_by").HasMaxLength(200);
        b.Property(c => c.UpdatedAt).HasColumnName("updated_at");
        b.Property(c => c.DeactivatedBy).HasColumnName("deactivated_by").HasMaxLength(200);
        b.Property(c => c.DeactivatedAt).HasColumnName("deactivated_at");
    }
}
