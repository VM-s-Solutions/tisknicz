using Makables.Core.Domain.Addresses;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Makables.Infra.Database.Configurations;

internal sealed class AddressEntityConfiguration : IEntityTypeConfiguration<Address>
{
    public void Configure(EntityTypeBuilder<Address> builder)
    {
        builder.ToTable("addresses");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id").HasMaxLength(40).IsRequired();

        // Field lengths sized per ADR 0010 + Czech address realities:
        //   - Street/City: 200 covers very long Czech street names
        //     (e.g. "Karlovarská třída u Sv. Bartoloměje") with headroom.
        //   - HouseNumber: 20 covers "123/45a" + the slash + letter suffix.
        //   - Zip: 16 covers "PSČ" formats up to "12345-1234" style.
        //   - State: 100 covers German Länder, US states, etc.
        builder.Property(a => a.Street).HasColumnName("street").HasMaxLength(200).IsRequired();
        builder.Property(a => a.HouseNumber).HasColumnName("house_number").HasMaxLength(20).IsRequired();
        builder.Property(a => a.City).HasColumnName("city").HasMaxLength(200).IsRequired();
        builder.Property(a => a.Zip).HasColumnName("zip").HasMaxLength(16).IsRequired();
        builder.Property(a => a.State).HasColumnName("state").HasMaxLength(100);
        builder.Property(a => a.CountryCodeIso).HasColumnName("country_code_iso").HasMaxLength(2).IsRequired();

        // Mapbox-supplied coordinates filled by T-0031. double precision
        // is excessive for street-level geocoding but matches the .NET
        // double type round-trip without lossy conversion.
        builder.Property(a => a.Latitude).HasColumnName("latitude");
        builder.Property(a => a.Longitude).HasColumnName("longitude");

        // Index supporting future "addresses needing geocoding" sweep
        // (T-0031 retry job). Partial filter so the index only covers the
        // rows that actually need the work.
        builder.HasIndex(a => a.Id)
            .HasDatabaseName("ix_addresses_pending_geocode")
            .HasFilter("latitude IS NULL AND is_active");

        ConfigureAuditable(builder);
    }

    private static void ConfigureAuditable(EntityTypeBuilder<Address> b)
    {
        b.Property(a => a.CountryCode).HasColumnName("country_code").HasMaxLength(2).IsRequired();
        b.Property(a => a.IsActive).HasColumnName("is_active").IsRequired();
        b.Property(a => a.CreatedBy).HasColumnName("created_by").HasMaxLength(200);
        b.Property(a => a.CreatedAt).HasColumnName("created_at");
        b.Property(a => a.UpdatedBy).HasColumnName("updated_by").HasMaxLength(200);
        b.Property(a => a.UpdatedAt).HasColumnName("updated_at");
        b.Property(a => a.DeactivatedBy).HasColumnName("deactivated_by").HasMaxLength(200);
        b.Property(a => a.DeactivatedAt).HasColumnName("deactivated_at");
    }
}
