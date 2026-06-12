using Makables.Core.Domain.Orders;

namespace Makables.Core.Domain.Outbox;

/// <summary>
/// JSON payload for the admin-facing "dispute opened" notification,
/// enqueued by every open-dispute path (T-0106): the customer / maker
/// endpoints, the rewired carrier-sourced <c>DisputeShipment</c>, and
/// the admin open variant.
///
/// <para>
/// Enrichment-at-enqueue per the T-0067 pattern (pending Q-0012) with
/// ONE deliberate exception: the recipient address is NOT here — it
/// resolves at send time from <c>EmailOptions.AdminNotificationAddress</c>
/// so a missing config parks the outbox row (Configuration-class, ADR
/// 0020) instead of blocking the party's dispute open (§C.9).
/// <see cref="ActionUrl"/> is the pre-baked admin order-detail link.
/// </para>
///
/// <para>
/// PascalCase JSON property names match the order-email payload
/// convention; the enums serialize as stable integers (<c>: short</c>
/// backing).
/// </para>
/// </summary>
public sealed record OrderDisputedAdminEmailPayload(
    string OrderId,
    string OrderNumber,
    string DisputeId,
    DisputeCategory Category,
    string Description,
    DisputeSource Source,
    string LanguageCode,
    string ActionUrl);
