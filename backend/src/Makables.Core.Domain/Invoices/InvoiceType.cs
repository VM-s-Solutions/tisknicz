namespace Makables.Core.Domain.Invoices;

/// <summary>
/// Two-valued discriminator on <see cref="Invoice"/>. Per ADR 0009 +
/// role/invoice.md: the platform issues two invoice families:
/// <list type="bullet">
///   <item><see cref="Customer"/> — one per paid <c>Order</c>; linked
///   via <see cref="Invoice.OrderId"/>.</item>
///   <item><see cref="Fee"/> — one per maker per weekly
///   <c>PayoutBatch</c>; linked via <see cref="Invoice.PayoutBatchId"/>
///   (FK lands in T-0101).</item>
/// </list>
///
/// Both share a single <c>FV-{CC}-{YYYY}NNNN</c> sequence per ADR 0009 +
/// T-0068a locked decision 4 (single shared numbering sequence). Storage
/// as an explicit enum with stable ordinals so downstream admin SQL can
/// filter without an INNER JOIN on a string column.
/// </summary>
public enum InvoiceType
{
    Customer = 0,
    Fee = 1,
}
