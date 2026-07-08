using Makables.Core.Domain.Makers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Makables.Infra.Database.Configurations;

internal sealed class MakerEntityConfiguration : IEntityTypeConfiguration<Maker>
{
    public void Configure(EntityTypeBuilder<Maker> builder)
    {
        builder.ToTable("makers");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).HasColumnName("id").HasMaxLength(40).IsRequired();

        builder.Property(m => m.UserId).HasColumnName("user_id").HasMaxLength(40).IsRequired();
        // One Maker row per User. Partial unique index gates this on
        // active rows so an admin GDPR purge can leave a soft-deleted
        // row behind without blocking a future re-registration.
        // no-translator: one Maker row per User is a defence-in-depth
        // invariant — RegisterMaker adds exactly one Maker per call, so a
        // 23505 here means an unexpected concurrent insert the handler could
        // not have produced. Translating it to a generic conflict would mask a
        // real bug; let it rethrow. (T-0033 Copilot review.)
        builder.HasIndex(m => m.UserId)
            .IsUnique()
            .HasDatabaseName("ix_makers_user_id")
            .HasFilter("is_active");

        // Czech IČO — 8 digits, but reserve 32 chars for future SK / PL
        // registration numbers that may be longer or include letters.
        builder.Property(m => m.RegistrationNumber)
            .HasColumnName("registration_number").HasMaxLength(32).IsRequired();
        builder.HasIndex(m => m.RegistrationNumber)
            .IsUnique()
            .HasDatabaseName("ix_makers_registration_number")
            .HasFilter("is_active");

        builder.Property(m => m.VatId).HasColumnName("vat_id").HasMaxLength(32);
        builder.Property(m => m.CompanyName).HasColumnName("company_name").HasMaxLength(300).IsRequired();
        builder.Property(m => m.LegalForm).HasColumnName("legal_form").HasMaxLength(200);

        builder.Property(m => m.RegisteredAddressId)
            .HasColumnName("registered_address_id").HasMaxLength(40).IsRequired();
        // FK reference is informational; we keep the addresses table
        // independent (an Address row can be referenced by orders,
        // customer-saved-addresses, etc. as Makables grows).
        builder.HasIndex(m => m.RegisteredAddressId)
            .HasDatabaseName("ix_makers_registered_address_id");

        builder.Property(m => m.IncorporatedOn).HasColumnName("incorporated_on");
        builder.Property(m => m.IsActiveInRegistry).HasColumnName("is_active_in_registry").IsRequired();
        builder.Property(m => m.IsVerified).HasColumnName("is_verified").IsRequired();
        builder.Property(m => m.SourceRegistry).HasColumnName("source_registry").HasMaxLength(20).IsRequired();
        builder.Property(m => m.SnapshotFetchedAt).HasColumnName("snapshot_fetched_at").IsRequired();
        builder.Property(m => m.SnapshotIsStale).HasColumnName("snapshot_is_stale").IsRequired();

        // T-0034 maker-editable profile fields.
        builder.Property(m => m.Bio).HasColumnName("bio").HasMaxLength(500);
        builder.Property(m => m.BankAccount).HasColumnName("bank_account").HasMaxLength(40);
        builder.Property(m => m.PersonalPickupEnabled).HasColumnName("personal_pickup_enabled").IsRequired();
        builder.Property(m => m.PickupNote).HasColumnName("pickup_note").HasMaxLength(500);

        // T-0043 catalog fields.
        builder.Property(m => m.Slug).HasColumnName("slug").HasMaxLength(120).IsRequired();
        // Partial unique index WHERE is_active so a soft-deleted maker
        // frees its slug for re-registration (matches the user_id + IČO
        // index policy on this table).
        builder.HasIndex(m => m.Slug)
            .IsUnique()
            .HasDatabaseName("ix_makers_slug")
            .HasFilter("is_active");

        builder.Property(m => m.RatingAverageBp).HasColumnName("rating_average_bp").IsRequired();
        builder.Property(m => m.RatingCount).HasColumnName("rating_count").IsRequired();
        builder.Property(m => m.TotalOrders).HasColumnName("total_orders").IsRequired();

        // T-0140 admin-set loyalty fee-rate override (basis points). Null
        // means "no override — use CountryConfiguration.PlatformFeeRateBp".
        builder.Property(m => m.FeeRateOverrideBp).HasColumnName("fee_rate_override_bp");

        // T-0110 GDPR-erasure tombstone flag: true when the maker was
        // anonymized-but-legally-retained (IČO + bank account kept).
        builder.Property(m => m.IsRetainedForLegal)
            .HasColumnName("is_retained_for_legal")
            .HasDefaultValue(false)
            .IsRequired();
        // Composite index backing the default catalog sort
        // (rating_average_bp DESC, total_orders DESC) over active rows.
        builder.HasIndex(m => new { m.RatingAverageBp, m.TotalOrders })
            .HasDatabaseName("ix_makers_catalog_sort")
            .HasFilter("is_active");

        ConfigureAuditable(builder);
    }

    private static void ConfigureAuditable(EntityTypeBuilder<Maker> b)
    {
        b.Property(m => m.CountryCode).HasColumnName("country_code").HasMaxLength(2).IsRequired();
        b.Property(m => m.IsActive).HasColumnName("is_active").IsRequired();
        b.Property(m => m.CreatedBy).HasColumnName("created_by").HasMaxLength(200);
        b.Property(m => m.CreatedAt).HasColumnName("created_at");
        b.Property(m => m.UpdatedBy).HasColumnName("updated_by").HasMaxLength(200);
        b.Property(m => m.UpdatedAt).HasColumnName("updated_at");
        b.Property(m => m.DeactivatedBy).HasColumnName("deactivated_by").HasMaxLength(200);
        b.Property(m => m.DeactivatedAt).HasColumnName("deactivated_at");
    }
}
