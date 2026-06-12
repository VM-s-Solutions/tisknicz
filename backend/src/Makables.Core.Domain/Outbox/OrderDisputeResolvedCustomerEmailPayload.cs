using Makables.Core.Domain.Orders;

namespace Makables.Core.Domain.Outbox;

/// <summary>
/// JSON payload for the customer-facing "your dispute was resolved"
/// email, enqueued by <c>ResolveDispute.Handler</c> (T-0106,
/// US-admin-0011 AC-2). <see cref="ResolutionNotes"/> is the admin's
/// CUSTOMER-VISIBLE explanation (§C.7) — rendered verbatim in the email
/// body alongside the outcome.
///
/// <para>
/// Enrichment-at-enqueue per the T-0067 pattern (pending Q-0012): full
/// payload incl. the pre-baked customer order-detail
/// <see cref="ActionUrl"/>. PascalCase JSON property names match the
/// order-email payload convention.
/// </para>
/// </summary>
public sealed record OrderDisputeResolvedCustomerEmailPayload(
    string OrderId,
    string OrderNumber,
    string Email,
    string ContactName,
    DisputeResolutionOutcome Outcome,
    string ResolutionNotes,
    string LanguageCode,
    string ActionUrl);
