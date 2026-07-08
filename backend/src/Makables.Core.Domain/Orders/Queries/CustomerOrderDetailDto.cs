namespace Makables.Core.Domain.Orders.Queries;

/// <summary>
/// Customer-facing order detail (T-0082). Lifecycle timestamps + full
/// price breakdown + contact snapshot + maker label + inline attachments
/// + invoice PDF URL. Distinct type from <see cref="MakerOrderDetailDto"/>
/// — compile-time IDOR shield (a customer DTO simply does not declare
/// maker-internal fields like <c>MakerPayoutAmountMinor</c>; leaking
/// them requires editing the DTO).
///
/// <para>
/// Nullable lifecycle timestamps mirror the state-machine reality (a
/// PendingPayment order has only <c>CreatedAt</c>; a Cancelled order has
/// <c>CancelledAt</c> but no <c>DeliveredAt</c>; etc.). Nullable
/// <see cref="ProductTitle"/> covers custom orders without a product
/// link. Nullable <see cref="ShippingCarrierTrackingUrl"/> covers
/// personal-pickup orders + Zasilkovna orders not yet shipped. Nullable
/// <see cref="InvoicePdfUrl"/> covers orders whose invoice has not been
/// generated yet (Invoice.PdfBlobPath still null per T-0068b). Nullable
/// <see cref="ReturnLabelUrl"/> (T-0146) covers the common case of no
/// dispute, or a dispute whose return label hasn't been generated yet —
/// present only once an admin has generated a reverse-shipment label.
/// </para>
/// </summary>
public sealed record CustomerOrderDetailDto(
    string OrderId,
    string OrderNumber,
    OrderState State,
    DateTimeOffset? PaidAt,
    DateTimeOffset? AcceptedAt,
    DateTimeOffset? ShippedAt,
    DateTimeOffset? DeliveredAt,
    DateTimeOffset? CancelledAt,
    long TotalAmountMinor,
    long ProductPriceMinor,
    long ShippingPriceMinor,
    long VatAmountMinor,
    int VatRateBp,
    string Currency,
    string ContactName,
    string ContactPhone,
    string MakerName,
    string? ProductTitle,
    ShippingMethod ShippingMethod,
    string? ShippingCarrierTrackingUrl,
    IReadOnlyList<OrderAttachmentSummaryDto> Attachments,
    string? InvoicePdfUrl,
    string? ReturnLabelUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);
