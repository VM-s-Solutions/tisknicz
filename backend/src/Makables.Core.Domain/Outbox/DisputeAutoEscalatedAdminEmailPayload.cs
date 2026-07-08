using Makables.Core.Domain.Orders;

namespace Makables.Core.Domain.Outbox;

/// <summary>
/// JSON payload for the admin-facing "maker missed the 7-day response
/// window" notification, enqueued by <c>EscalateDispute.Handler</c>
/// (T-0145) from the daily sweep. Mirrors <see cref="OrderDisputedAdminEmailPayload"/>'s
/// enrichment-at-enqueue shape — recipient resolves at SEND time from
/// <c>EmailOptions.AdminNotificationAddress</c>, never baked in here.
/// Notification only: the dispute stays <c>Disputed</c> / unresolved.
/// </summary>
public sealed record DisputeAutoEscalatedAdminEmailPayload(
    string OrderId,
    string OrderNumber,
    string DisputeId,
    DisputeCategory Category,
    string Description,
    string LanguageCode,
    string ActionUrl);
