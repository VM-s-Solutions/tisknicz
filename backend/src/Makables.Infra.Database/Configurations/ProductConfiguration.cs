using Makables.Core.Domain.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Makables.Infra.Database.Configurations;

internal sealed class ProductEntityConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("products");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id").HasMaxLength(40).IsRequired();

        builder.Property(p => p.MakerId).HasColumnName("maker_id").HasMaxLength(40).IsRequired();
        builder.HasIndex(p => p.MakerId).HasDatabaseName("ix_products_maker_id");

        builder.Property(p => p.CategoryId).HasColumnName("category_id").HasMaxLength(40).IsRequired();
        // ix_products_category_id supports the catalog filter-by-category
        // query in T-0043 (the most-trafficked Phase-3 read path).
        builder.HasIndex(p => p.CategoryId).HasDatabaseName("ix_products_category_id");

        builder.Property(p => p.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
        builder.Property(p => p.Description).HasColumnName("description").HasMaxLength(4000);

        // Money pair per ADR 0003. Currency stored as ISO 4217 char(3);
        // amount in minor units (haléře for CZK).
        builder.Property(p => p.PriceAmountMinor)
            .HasColumnName("price_amount_minor").IsRequired();
        builder.Property(p => p.PriceCurrency)
            .HasColumnName("price_currency").HasMaxLength(3).IsRequired();
        builder.Property(p => p.PriceType)
            .HasColumnName("price_type")
            .HasConversion<string>()  // string-storage so `dotnet ef database update` reads cleanly
            .HasMaxLength(20)
            .IsRequired();

        // T-0144: "na zakázku" vs. "skladem". String-storage, same
        // convention as PriceType. DEFAULT 'MadeToOrder' so the
        // migration backfills every pre-existing row to the safer
        // legal posture with no manual data fix (AC-6).
        builder.Property(p => p.FulfillmentType)
            .HasColumnName("fulfillment_type")
            .HasConversion<string>()
            .HasMaxLength(20)
            .HasDefaultValue(FulfillmentType.MadeToOrder)
            .IsRequired();

        builder.Property(p => p.WeightGrams).HasColumnName("weight_grams").IsRequired();

        // Owned collection: product_images in a separate table. The DB
        // FK is cascade-delete (the rows go when the product row is hard-
        // deleted). NOTE: blob cleanup is NOT performed here and is NOT
        // implied by soft-delete — DeleteProduct only flips IsActive and
        // leaves the blobs addressable (T-0041 Copilot review).
        builder.OwnsMany(p => p.Images, image =>
        {
            image.ToTable("product_images");

            image.WithOwner().HasForeignKey("product_id");
            image.HasKey(i => i.Id);

            image.Property(i => i.Id).HasColumnName("id").HasMaxLength(40).IsRequired();
            image.Property<string>("product_id").HasColumnName("product_id").HasMaxLength(40);
            image.Property(i => i.BlobPath).HasColumnName("blob_path").HasMaxLength(1024).IsRequired();
            image.Property(i => i.SortOrder).HasColumnName("sort_order").IsRequired();
        });

        // Always load images with the product: the image-cap check and
        // RemoveImage both depend on Images being populated, and a
        // partially-loaded aggregate would let >10 images through or
        // 404 an existing image. AutoInclude makes every GetByIdAsync
        // eager-load the collection (T-0041 Copilot review High).
        builder.Navigation(p => p.Images).AutoInclude();

        ConfigureAuditable(builder);
    }

    private static void ConfigureAuditable(EntityTypeBuilder<Product> b)
    {
        b.Property(p => p.CountryCode).HasColumnName("country_code").HasMaxLength(2).IsRequired();
        b.Property(p => p.IsActive).HasColumnName("is_active").IsRequired();
        b.Property(p => p.CreatedBy).HasColumnName("created_by").HasMaxLength(200);
        b.Property(p => p.CreatedAt).HasColumnName("created_at");
        b.Property(p => p.UpdatedBy).HasColumnName("updated_by").HasMaxLength(200);
        b.Property(p => p.UpdatedAt).HasColumnName("updated_at");
        b.Property(p => p.DeactivatedBy).HasColumnName("deactivated_by").HasMaxLength(200);
        b.Property(p => p.DeactivatedAt).HasColumnName("deactivated_at");
    }
}
