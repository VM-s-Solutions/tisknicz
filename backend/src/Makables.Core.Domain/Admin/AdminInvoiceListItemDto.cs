using Makables.Core.Domain.Invoices;

namespace Makables.Core.Domain.Admin;

/// <summary>
/// Admin invoice-list row (T-0111 / US-admin-0012). Carries the recipient
/// + amount + the nullable <see cref="OrderId"/> / <see cref="PayoutBatchId"/>
/// links (exactly one is non-null per the invoice XOR invariant). The
/// before/after diff + PDF download are detail-page concerns (out of scope).
/// </summary>
public sealed record AdminInvoiceListItemDto(
    string InvoiceId,
    string InvoiceNumber,
    InvoiceType Type,
    string CountryCode,
    string RecipientName,
    long TotalMinor,
    string Currency,
    DateTimeOffset CreatedAt,
    string? OrderId,
    string? PayoutBatchId);
