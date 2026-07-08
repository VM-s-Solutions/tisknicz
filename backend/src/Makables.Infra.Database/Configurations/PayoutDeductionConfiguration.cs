using Makables.Core.Domain.Payouts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Makables.Infra.Database.Configurations;

/// <summary>
/// EF Core mapping for <see cref="PayoutDeduction"/> (T-0146). Mirrors
/// <c>PayoutBatchConfiguration</c>'s snake-case + enum-as-SMALLINT style.
/// </summary>
internal sealed class PayoutDeductionConfiguration : IEntityTypeConfiguration<PayoutDeduction>
{
    public void Configure(EntityTypeBuilder<PayoutDeduction> builder)
    {
        builder.ToTable("payout_deductions");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).HasColumnName("id").HasMaxLength(40).IsRequired();

        builder.Property(d => d.MakerId).HasColumnName("maker_id").HasMaxLength(40).IsRequired();
        builder.Property(d => d.DisputeId).HasColumnName("dispute_id").HasMaxLength(40).IsRequired();

        builder.Property(d => d.Reason)
            .HasColumnName("reason")
            .HasConversion<short>()
            .IsRequired();

        builder.Property(d => d.AmountMinor).HasColumnName("amount_minor").IsRequired();
        builder.Property(d => d.Currency)
            .HasColumnName("currency").HasMaxLength(PayoutDeduction.CurrencyLength).IsRequired();

        builder.Property(d => d.PayoutBatchId).HasColumnName("payout_batch_id").HasMaxLength(40);

        // Fast lookup for CreatePayoutBatch's per-maker pending-deduction claim.
        builder.HasIndex(d => new { d.MakerId, d.PayoutBatchId })
            .HasDatabaseName("ix_payout_deductions_maker_pending");

        ConfigureAuditable(builder);
    }

    private static void ConfigureAuditable(EntityTypeBuilder<PayoutDeduction> b)
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
