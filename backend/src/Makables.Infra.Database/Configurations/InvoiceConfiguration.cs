using Makables.Core.Domain.Invoices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Makables.Infra.Database.Configurations;

/// <summary>
/// EF Core mapping for <see cref="Invoice"/> per T-0068a. Snake-case
/// columns matching the rest of the schema; <see cref="InvoiceType"/> +
/// <c>InvoicingMode</c> stored as string via <see cref="HasConversion"/>
/// (consistent with <c>ShippingMethod</c> / <c>OrderState</c> /
/// <c>PriceType</c>) so <c>dotnet ef database update</c> reads cleanly.
///
/// <para>
/// <b>Indexes.</b> Per ticket §Locked decisions:
/// <list type="bullet">
///   <item><c>ix_invoices_invoice_number</c> — unique on
///     <c>invoice_number</c>, partial <c>WHERE is_active</c>. Soft-deleted
///     rows free the number per locked decision absorption (a re-issuance
///     gets a new number from the sequence; this index is a belt-and-
///     braces shield against an accidental duplicate insert).</item>
///   <item><c>ix_invoices_order_id</c> — partial unique on
///     <c>order_id WHERE order_id IS NOT NULL AND is_active</c>. Enforces
///     "one Customer invoice per paid order" (role/invoice.md invariant)
///     at the database level; also backs the T-0068b idempotency check
///     on <c>MarkOrderPaid</c> webhook re-deliveries.</item>
///   <item><c>ix_invoices_maker_created</c> — composite
///     <c>(maker_id, created_at DESC)</c>. Backs the maker-host "my
///     invoices" list ordered newest-first.</item>
///   <item><c>ix_invoices_type</c> — single-column on <c>type</c>.
///     Backs the admin filter "Customer invoices only" / "Fee invoices
///     only" at low cost (two-value enum, so cardinality is trivial but
///     the index lets the planner skip the full scan on a SELECT
///     filtered to Fee invoices for the monthly admin reconciliation).</item>
/// </list>
/// </para>
///
/// <para>
/// <b>FKs.</b> <c>order_id → orders(id)</c> ON DELETE RESTRICT — an order
/// with an issued invoice is a legal record; physical-deleting the order
/// must fail noisily rather than orphan the invoice. The
/// <c>payout_batch_id</c> column ships in this migration without an FK;
/// the FK is added in T-0101 when <c>payout_batches</c> table lands.
/// </para>
///
/// <para>
/// <b>Blob path format (T-0068b consumer hot-path note).</b> Per
/// role/invoice.md the rendered PDF lands at
/// <c>invoices/{cc}/orders/{orderId}/{invoiceNumber}.pdf</c> for Customer
/// invoices. T-0068b's <c>IInvoiceService</c> writes that exact path via
/// <see cref="Invoice.AttachPdfBlobPath"/>; the deterministic shape is
/// what makes the set-once idempotent-on-retry behaviour work.
/// </para>
///
/// <para>
/// <b>§ 35 zákon o DPH 10-year retention.</b> Czech tax law requires
/// invoice PDFs + their underlying rows to survive for 10 years after
/// the close of the calendar year of issuance. Soft-delete + global
/// query filter handle the row side already (rows stay in Postgres,
/// hidden from non-admin queries); the blob-storage side (Azure
/// lifecycle policy preventing accidental delete on the
/// <c>invoices/</c> container before the 10-year window) is
/// infrastructure-as-code, owned by the
/// <c>docs/architecture/roles/blob-storage.md</c> role doc maintainer.
/// See T-0068a Locked design decisions §"§ 35 10-year retention policy"
/// for the absorption note. Remove this paragraph when the Azure
/// lifecycle policy is committed to infrastructure-as-code.
/// </para>
/// </summary>
internal sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.ToTable("invoices");

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).HasColumnName("id").HasMaxLength(40).IsRequired();

        // === Identity ===
        builder.Property(i => i.InvoiceNumber)
            .HasColumnName("invoice_number")
            .HasMaxLength(Invoice.MaxInvoiceNumberLength)
            .IsRequired();

        builder.Property(i => i.Type)
            .HasColumnName("type")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        // === Aggregate link (XOR enforced by Invoice.Issue factory) ===
        builder.Property(i => i.OrderId).HasColumnName("order_id").HasMaxLength(40);
        builder.Property(i => i.PayoutBatchId).HasColumnName("payout_batch_id").HasMaxLength(40);
        builder.Property(i => i.MakerId).HasColumnName("maker_id").HasMaxLength(40).IsRequired();

        // === Issuer snapshot ===
        builder.Property(i => i.IssuerName)
            .HasColumnName("issuer_name").HasMaxLength(Invoice.MaxIssuerNameLength).IsRequired();
        builder.Property(i => i.IssuerIco)
            .HasColumnName("issuer_ico").HasMaxLength(Invoice.MaxIssuerIcoLength).IsRequired();
        // Nullable per T-0068a locked decision 2 (platform not VAT-registered at MVP).
        builder.Property(i => i.IssuerDic)
            .HasColumnName("issuer_dic").HasMaxLength(Invoice.MaxIssuerDicLength);
        builder.Property(i => i.IssuerBankAccount)
            .HasColumnName("issuer_bank_account").HasMaxLength(Invoice.MaxIssuerBankAccountLength);

        // === Recipient snapshot ===
        builder.Property(i => i.RecipientName)
            .HasColumnName("recipient_name").HasMaxLength(Invoice.MaxRecipientNameLength).IsRequired();
        builder.Property(i => i.RecipientEmail)
            .HasColumnName("recipient_email").HasMaxLength(Invoice.MaxRecipientEmailLength).IsRequired();
        builder.Property(i => i.RecipientTaxId)
            .HasColumnName("recipient_tax_id").HasMaxLength(Invoice.MaxRecipientTaxIdLength);
        builder.Property(i => i.RecipientVatId)
            .HasColumnName("recipient_vat_id").HasMaxLength(Invoice.MaxRecipientVatIdLength);

        // === Dates (country-local DateOnly per ADR 0003) ===
        builder.Property(i => i.IssueDate).HasColumnName("issue_date").IsRequired();
        // Nullable for InvoicingMode.None rows (CZ law: § 29 zákon o DPH
        // does not require DUZP on non-tax receipts).
        builder.Property(i => i.TaxableSupplyDate).HasColumnName("taxable_supply_date");
        builder.Property(i => i.DueDate).HasColumnName("due_date").IsRequired();

        // === Invoicing mode snapshot ===
        builder.Property(i => i.InvoicingMode)
            .HasColumnName("invoicing_mode")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        // === Money breakdown (minor units; ADR 0003) ===
        builder.Property(i => i.AmountWithoutVatMinor)
            .HasColumnName("amount_without_vat_minor").IsRequired();
        builder.Property(i => i.VatRateBp)
            .HasColumnName("vat_rate_bp").IsRequired();
        builder.Property(i => i.VatAmountMinor)
            .HasColumnName("vat_amount_minor").IsRequired();
        builder.Property(i => i.AmountWithVatMinor)
            .HasColumnName("amount_with_vat_minor").IsRequired();
        builder.Property(i => i.Currency)
            .HasColumnName("currency").HasMaxLength(3).IsRequired();

        // === Rendered PDF reference (set-once) ===
        builder.Property(i => i.PdfBlobPath)
            .HasColumnName("pdf_blob_path")
            .HasMaxLength(Invoice.MaxPdfBlobPathLength);

        // === Indexes ===
        // Unique invoice number, partial WHERE is_active — same shape as
        // ix_orders_order_number. The numbering generator is gap-free so
        // this is a belt-and-braces shield: an accidental duplicate
        // surfaces as a 23505 / UniqueConstraintViolationException rather
        // than corrupting the legal record.
        builder.HasIndex(i => i.InvoiceNumber)
            .IsUnique()
            .HasDatabaseName("ix_invoices_invoice_number")
            .HasFilter("is_active");

        // Partial unique on order_id WHERE order_id IS NOT NULL AND is_active.
        // Enforces "one Customer invoice per paid order" at the DB level
        // (role/invoice.md invariant). Soft-deleted rows free the slot
        // for re-issuance after GDPR anonymisation per T-0068a locked
        // decision absorption.
        builder.HasIndex(i => i.OrderId)
            .IsUnique()
            .HasDatabaseName("ix_invoices_order_id")
            .HasFilter("order_id IS NOT NULL AND is_active");

        // Composite (maker_id, created_at DESC) backs the maker-host
        // "my invoices" list ordered newest-first (T-0068b reads it).
        builder.HasIndex(i => new { i.MakerId, i.CreatedAt })
            .HasDatabaseName("ix_invoices_maker_created")
            .IsDescending(false, true);

        // Single-column on type — admin reconciliation queries that
        // filter Customer vs Fee scan via this index even though
        // cardinality is low (two values), the planner is happy to use
        // it for the filtered SELECT.
        builder.HasIndex(i => i.Type)
            .HasDatabaseName("ix_invoices_type");

        // === FKs ===
        // order_id → orders(id) ON DELETE RESTRICT. An order with an
        // invoice cannot be physical-deleted — invoices are legal records
        // and orphaning is not acceptable. Soft-delete on the order does
        // not touch this FK (the global query filter hides both rows
        // from non-admin queries).
        builder.HasOne<Makables.Core.Domain.Orders.Order>()
            .WithMany()
            .HasForeignKey(i => i.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        // payout_batch_id has no FK at T-0068a — the payout_batches
        // table does not exist yet. T-0101 adds the FK + the Fee-side
        // ForMaker join. The XOR factory invariant prevents
        // payout_batch_id from being set in any T-0068a/b code path.

        ConfigureAuditable(builder);
    }

    private static void ConfigureAuditable(EntityTypeBuilder<Invoice> b)
    {
        b.Property(i => i.CountryCode).HasColumnName("country_code").HasMaxLength(2).IsRequired();
        b.Property(i => i.IsActive).HasColumnName("is_active").IsRequired();
        b.Property(i => i.CreatedBy).HasColumnName("created_by").HasMaxLength(200);
        b.Property(i => i.CreatedAt).HasColumnName("created_at");
        b.Property(i => i.UpdatedBy).HasColumnName("updated_by").HasMaxLength(200);
        b.Property(i => i.UpdatedAt).HasColumnName("updated_at");
        b.Property(i => i.DeactivatedBy).HasColumnName("deactivated_by").HasMaxLength(200);
        b.Property(i => i.DeactivatedAt).HasColumnName("deactivated_at");
    }
}
