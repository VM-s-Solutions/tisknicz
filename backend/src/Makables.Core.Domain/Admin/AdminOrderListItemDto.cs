using Makables.Core.Domain.Orders;

namespace Makables.Core.Domain.Admin;

/// <summary>
/// Privileged admin order-list row (T-0111 / US-admin-0009). UNLIKE the
/// maker-facing T-0081 list (which omits customer email per GDPR data
/// minimisation), the admin row carries <see cref="CustomerEmail"/> +
/// <see cref="MakerName"/> — admin is the privileged actor and the story
/// names "customer email" as a filter (presupposing admin can see it).
/// <see cref="IsActive"/> surfaces soft-deleted / anonymised rows so the
/// frontend can tag T-0110 reconciliation orders.
///
/// Lives in <c>Core.Domain.Admin</c> (not AppServices) so the read-side
/// <see cref="IAdminQueries"/> seam can reference it without a
/// Domain→AppServices layering violation — mirrors the ADR-0023
/// <c>IOrderQueries</c> / <c>Orders.Queries</c> precedent.
/// </summary>
public sealed record AdminOrderListItemDto(
    string OrderId,
    string OrderNumber,
    OrderState State,
    string CountryCode,
    long TotalAmountMinor,
    string Currency,
    DateTimeOffset CreatedAt,
    string MakerId,
    string MakerName,
    string CustomerEmail,
    string? ProductTitle,
    bool IsActive);
