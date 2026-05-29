using Makables.Core.Domain.Makers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Makables.Infra.Database.Configurations;

/// <summary>
/// Maps <see cref="MakerCategory"/> onto the existing
/// <c>maker_categories</c> table (created by the T-0040 migration).
/// T-0043 added the domain type so the catalog category-filter can
/// query membership; no migration change is needed — the columns
/// already exist.
/// </summary>
internal sealed class MakerCategoryEntityConfiguration : IEntityTypeConfiguration<MakerCategory>
{
    public void Configure(EntityTypeBuilder<MakerCategory> builder)
    {
        builder.ToTable("maker_categories");

        builder.HasKey(mc => new { mc.MakerId, mc.CategoryId });

        builder.Property(mc => mc.MakerId).HasColumnName("maker_id").HasMaxLength(40).IsRequired();
        builder.Property(mc => mc.CategoryId).HasColumnName("category_id").HasMaxLength(40).IsRequired();
        builder.Property(mc => mc.CountryCode).HasColumnName("country_code").HasMaxLength(2).IsRequired();
        builder.Property(mc => mc.CreatedAt).HasColumnName("created_at").IsRequired();

        // created_by exists on the table (T-0040) but isn't on the
        // lightweight entity. Map it as a shadow property defaulting to
        // 'system' so an insert through this type is well-formed; the
        // future membership-management ticket can promote it to a real
        // property when makers start picking categories.
        builder.Property<string>("created_by")
            .HasColumnName("created_by")
            .HasMaxLength(200)
            .HasDefaultValue("system");
    }
}
