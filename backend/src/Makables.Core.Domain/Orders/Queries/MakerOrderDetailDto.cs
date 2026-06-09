namespace Makables.Core.Domain.Orders.Queries;

/// <summary>
/// Maker-facing order detail (T-0082). Lifecycle timestamps + full price
/// breakdown including <see cref="MakerPayoutAmountMinor"/> (NOT
/// platform fee — irrelevant to the maker) + customer contact NAME +
/// PHONE (NOT email — T-0081 GDPR data-minimization lock A.2) + carrier
/// ref + Zasilkovna pickup point id + inline attachments + invoice PDF
/// URL.
///
/// <para>
/// <b>Distinct type from <see cref="CustomerOrderDetailDto"/></b> — the
/// compile-time IDOR / GDPR shield. A maker DTO simply does not declare
/// fields like <c>CustomerContactEmail</c> or <c>PlatformFeeAmountMinor</c>;
/// adding them in a future PR breaks the AC-4 reflection test, which
/// pins the absence.
/// </para>
/// </summary>
public sealed record MakerOrderDetailDto(
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
    long MakerPayoutAmountMinor,
    string Currency,
    string CustomerContactName,
    string CustomerContactPhone,
    string? ProductTitle,
    ShippingMethod ShippingMethod,
    string? ShippingCarrierRef,
    string? ShippingCarrierTrackingUrl,
    string? ZasilkovnaPickupPointId,
    IReadOnlyList<OrderAttachmentSummaryDto> Attachments,
    string? InvoicePdfUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);
