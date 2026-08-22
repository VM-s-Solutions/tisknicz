using Makables.Core.Domain.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Makables.Infra.Database.Configurations;

internal sealed class CountryEntityConfiguration : IEntityTypeConfiguration<Country>
{
    public void Configure(EntityTypeBuilder<Country> builder)
    {
        builder.ToTable("countries");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id").HasMaxLength(2).IsRequired();
        builder.Property(c => c.IsoCode).HasColumnName("iso_code").HasMaxLength(2).IsRequired();
        builder.HasIndex(c => c.IsoCode).IsUnique();
        builder.Property(c => c.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(c => c.IsServiced).HasColumnName("is_serviced").IsRequired();

        ConfigureAuditable(builder);
    }

    private static void ConfigureAuditable(EntityTypeBuilder<Country> b)
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

internal sealed class CountryConfigurationEntityConfiguration : IEntityTypeConfiguration<CountryConfiguration>
{
    public void Configure(EntityTypeBuilder<CountryConfiguration> builder)
    {
        builder.ToTable("country_configuration");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id").HasMaxLength(2).IsRequired();
        builder.Property(c => c.CountryId).HasColumnName("country_id").HasMaxLength(2).IsRequired();
        builder.HasIndex(c => c.CountryId).IsUnique();

        builder.Property(c => c.DefaultCurrencyCode).HasColumnName("default_currency_code").HasMaxLength(3).IsRequired();
        builder.Property(c => c.DefaultLanguageCode).HasColumnName("default_language_code").HasMaxLength(10).IsRequired();
        builder.Property(c => c.TimeZoneId).HasColumnName("time_zone_id").HasMaxLength(50).IsRequired();
        builder.Property(c => c.PhonePrefix).HasColumnName("phone_prefix").HasMaxLength(10).IsRequired();
        builder.Property(c => c.DateFormat).HasColumnName("date_format").HasMaxLength(50).IsRequired();
        builder.Property(c => c.ZipFormat).HasColumnName("zip_format").HasMaxLength(200);

        builder.Property(c => c.StandardVatRateBp).HasColumnName("standard_vat_rate_bp").IsRequired();
        builder.Property(c => c.ReducedVatRateBp).HasColumnName("reduced_vat_rate_bp");
        builder.Property(c => c.InvoicingMode).HasColumnName("invoicing_mode").HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(c => c.PlatformFeeRateBp).HasColumnName("platform_fee_rate_bp").IsRequired();
        // T-0181 (Q-0041): how long a maker may still refuse a paid order.
        // A tunable policy row, not a constant (ADR 0004).
        builder.Property(c => c.MakerRefusalWindowHours)
            .HasColumnName("maker_refusal_window_hours")
            .HasDefaultValue(48)
            .IsRequired();
        builder.Property(c => c.DefaultShippingPriceMinor)
            .HasColumnName("default_shipping_price_minor")
            .IsRequired();

        builder.Property(c => c.TaxIdLabel).HasColumnName("tax_id_label").HasMaxLength(50).IsRequired();
        builder.Property(c => c.TaxIdFormat).HasColumnName("tax_id_format").HasMaxLength(200);
        builder.Property(c => c.VatIdLabel).HasColumnName("vat_id_label").HasMaxLength(50).IsRequired();
        builder.Property(c => c.VatIdFormat).HasColumnName("vat_id_format").HasMaxLength(200);
        builder.Property(c => c.VatIdRequired).HasColumnName("vat_id_required").IsRequired();
        builder.Property(c => c.RegistrationNumberLabel).HasColumnName("registration_number_label").HasMaxLength(50).IsRequired();
        builder.Property(c => c.RegistrationNumberFormat).HasColumnName("registration_number_format").HasMaxLength(200);
        builder.Property(c => c.RegistrationNumberRequired).HasColumnName("registration_number_required").IsRequired();

        builder.Property(c => c.DefaultPaymentProvider).HasColumnName("default_payment_provider").HasMaxLength(50).IsRequired();
        builder.Property(c => c.DefaultShippingCarrier).HasColumnName("default_shipping_carrier").HasMaxLength(50).IsRequired();
        builder.Property(c => c.DefaultRegistry).HasColumnName("default_registry").HasMaxLength(50).IsRequired();
        builder.Property(c => c.DefaultEmailProvider).HasColumnName("default_email_provider").HasMaxLength(50).IsRequired();

        builder.Property(c => c.LegalRequirementsJson).HasColumnName("legal_requirements_json").HasColumnType("jsonb");

        // T-0068b: invoice issuer + platform IBAN columns (locked
        // decisions 4 + 8). Lengths chosen to match Invoice's
        // MaxIssuerNameLength (200) and MaxIssuerIcoLength (40), with
        // IBAN at 34 chars (ISO 13616 max).
        builder.Property(c => c.IssuerName).HasColumnName("issuer_name").HasMaxLength(200).IsRequired();
        builder.Property(c => c.IssuerIco).HasColumnName("issuer_ico").HasMaxLength(8).IsFixedLength().IsRequired();
        builder.Property(c => c.IssuerDic).HasColumnName("issuer_dic").HasMaxLength(15);
        builder.Property(c => c.PlatformIban).HasColumnName("platform_iban").HasMaxLength(34);

        ConfigureAuditable(builder);
    }

    private static void ConfigureAuditable(EntityTypeBuilder<CountryConfiguration> b)
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
