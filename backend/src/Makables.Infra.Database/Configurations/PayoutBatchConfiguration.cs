using Makables.Core.Domain.Payouts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Makables.Infra.Database.Configurations;

/// <summary>
/// EF Core mapping for <see cref="PayoutBatch"/> per T-0101. Snake-case
/// columns; <see cref="PayoutBatchState"/> stored as a string via
/// <see cref="HasConversion"/> (matching <c>InvoiceConfiguration</c>).
///
/// <para>
/// <b>Indexes.</b>
/// <list type="bullet">
///   <item><c>ux_payout_batches_country_batch_number</c> — unique
///     <c>(country_code, batch_number)</c>. THIS is the ADR-0009
///     uniqueness enforcement the <c>VYP-{CC}-{YYYY}-W{ww}</c> generator
///     relies on (no NumberingSequence row).</item>
///   <item><c>ux_payout_batches_open_per_country</c> — partial unique on
///     <c>(country_code) WHERE state = 'Processing' AND is_active</c>.
///     Belt-and-braces against the Monday-02:00 timer + admin-click race:
///     the second commit fails at the DB, turning a read-then-write race
///     into a deterministic unique violation the handler maps to "return
///     the existing batch". <c>Completed</c> rows are unconstrained.</item>
/// </list>
/// </para>
/// </summary>
internal sealed class PayoutBatchConfiguration : IEntityTypeConfiguration<PayoutBatch>
{
    public void Configure(EntityTypeBuilder<PayoutBatch> builder)
    {
        builder.ToTable("payout_batches");

        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).HasColumnName("id").HasMaxLength(40).IsRequired();

        builder.Property(b => b.BatchNumber)
            .HasColumnName("batch_number")
            .HasMaxLength(PayoutBatch.MaxBatchNumberLength)
            .IsRequired();

        builder.Property(b => b.State)
            .HasColumnName("state")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(b => b.TotalAmountMinor)
            .HasColumnName("total_amount_minor")
            .IsRequired();
        builder.Property(b => b.Currency)
            .HasColumnName("currency").HasMaxLength(3).IsRequired();

        builder.Property(b => b.OrderCount).HasColumnName("order_count").IsRequired();
        builder.Property(b => b.MakerCount).HasColumnName("maker_count").IsRequired();

        builder.Property(b => b.ExcludedPartiallyRefundedOrderCount)
            .HasColumnName("excluded_partially_refunded_order_count").IsRequired();
        builder.Property(b => b.ExcludedNoBankAccountOrderCount)
            .HasColumnName("excluded_no_bank_account_order_count").IsRequired();
        builder.Property(b => b.ExcludedNoBankAccountMakerCount)
            .HasColumnName("excluded_no_bank_account_maker_count").IsRequired();

        builder.Property(b => b.CsvBlobPath)
            .HasColumnName("csv_blob_path")
            .HasMaxLength(PayoutBatch.MaxCsvBlobPathLength);

        builder.Property(b => b.CompletedAt).HasColumnName("completed_at");
        builder.Property(b => b.CompletedBy)
            .HasColumnName("completed_by").HasMaxLength(PayoutBatch.MaxCompletedByLength);

        // Unique (country_code, batch_number) — ADR 0009 numbering uniqueness.
        builder.HasIndex(b => new { b.CountryCode, b.BatchNumber })
            .IsUnique()
            .HasDatabaseName("ux_payout_batches_country_batch_number");

        // Partial unique on country_code WHERE state = 'Processing' AND is_active.
        // At most one open batch per country (the read-then-write race guard).
        builder.HasIndex(b => b.CountryCode)
            .IsUnique()
            .HasDatabaseName("ux_payout_batches_open_per_country")
            .HasFilter("state = 'Processing' AND is_active");

        ConfigureAuditable(builder);
    }

    private static void ConfigureAuditable(EntityTypeBuilder<PayoutBatch> b)
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
