using Makables.Core.Domain.Orders;

namespace Makables.Core.Domain.Admin;

/// <summary>
/// Privileged admin order-detail header (T-0127 / Q-0024 option a /
/// US-admin-0009). The FULL order header — number, state, the complete
/// amount breakdown, country, maker (id + name), customer email + contact
/// snapshot, and every lifecycle timestamp. UNLIKE the owner-scoped
/// T-0082 customer/maker detail DTOs (loaded via
/// <c>GetByIdForCustomerAsync</c>/<c>ForMakerAsync</c>, not reachable on
/// the admin host), this carries <see cref="CustomerEmail"/> + the full
/// contact snapshot with <b>NO GDPR redaction</b> — admin is the
/// privileged actor (mirrors the <see cref="AdminOrderListItemDto"/>
/// divergence from the maker surface).
///
/// <para>
/// Replaces T-0118b's list-row-scan header. The header (not line items /
/// message thread) is the scope: the existing audit-log trail covers
/// US-admin-0009 AC-2. <see cref="IsActive"/> surfaces soft-deleted /
/// anonymised reconciliation rows. Lives in <c>Core.Domain.Admin</c> so
/// the read-side <see cref="IAdminQueries"/> seam references it without a
/// Domain→AppServices layering violation (ADR-0023 precedent).
/// </para>
/// </summary>
public sealed record AdminOrderDetailDto(
    string OrderId,
    string OrderNumber,
    OrderState State,
    string CountryCode,
    string MakerId,
    string MakerName,
    string? ProductId,
    string? ProductTitle,
    // Contact snapshot (privileged — no redaction).
    string CustomerUserId,
    string CustomerEmail,
    string ContactName,
    string ContactPhone,
    string? CustomerNotes,
    // Amount breakdown (minor units, ADR 0003).
    long ProductPriceAmountMinor,
    long ShippingPriceAmountMinor,
    long PlatformFeeAmountMinor,
    long MakerPayoutAmountMinor,
    long TotalAmountMinor,
    long RefundedAmountMinor,
    string Currency,
    int VatRateBp,
    // Shipping choice.
    ShippingMethod ShippingMethod,
    string? ZasilkovnaPickupPointId,
    string? ShippingCarrierRef,
    string? ShippingCarrierTrackingUrl,
    // Payment.
    string? PaymentProviderRef,
    string? PaymentMethod,
    // Payout link.
    string? PayoutBatchId,
    // Lifecycle timestamps.
    DateTimeOffset CreatedAt,
    DateTimeOffset? PaidAt,
    DateTimeOffset? AcceptedAt,
    DateTimeOffset? ShippedAt,
    DateTimeOffset? DeliveredAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? CancelledAt,
    DateTimeOffset? RefundedAt,
    DateTimeOffset? DisputedAt,
    bool IsActive,
    // T-0146 — the order's OPEN dispute (null if none), so the admin UI
    // can gate the "Vygenerovat vratkový štítek" action to return-
    // warranting categories (AC-6) and show whether a label already exists.
    string? OpenDisputeId,
    Orders.DisputeCategory? OpenDisputeCategory,
    bool ReturnLabelGenerated);
