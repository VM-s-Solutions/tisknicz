using Makables.Core.Domain.Invoices;

namespace Makables.Core.Domain.Admin;

/// <summary>
/// Filter dimensions for the admin invoice list (T-0111 / US-admin-0012
/// AC-1): type, country, recipient, date range — no more (Q-E).
/// <see cref="Recipient"/> matches case-insensitively against the invoice
/// recipient name; <see cref="DateRangeStart"/>/<see cref="DateRangeEnd"/>
/// compare inclusively against <c>CreatedAt</c>.
/// </summary>
public sealed record AdminInvoiceFilter(
    InvoiceType? Type,
    string? CountryCode,
    string? Recipient,
    DateTimeOffset? DateRangeStart,
    DateTimeOffset? DateRangeEnd);
