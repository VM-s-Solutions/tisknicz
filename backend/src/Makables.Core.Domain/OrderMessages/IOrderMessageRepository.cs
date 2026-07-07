namespace Makables.Core.Domain.OrderMessages;

/// <summary>
/// Write-side persistence access for <see cref="OrderMessage"/> (T-0079).
/// Surface shape follows ADR 0013: scoped read-modify (AddAsync) + scoped
/// bulk-update MarkAsRead methods that bake the audience predicate into
/// the SQL <c>WHERE</c> clause. The read-side projection lives in
/// <see cref="IOrderMessageQueries"/> per ADR 0023.
///
/// <para>
/// <b>No GetByIdAsync.</b> The handler loads the parent <see cref="Orders.Order"/>
/// via the existing scoped <see cref="Orders.IOrderRepository"/>; new
/// message rows are appended via <see cref="AddAsync"/> alongside the
/// counter increment on the order. Single-message reads are not a
/// product surface at MVP.
/// </para>
/// </summary>
public interface IOrderMessageRepository
{
    /// <summary>Track <paramref name="message"/> as a pending insert.</summary>
    Task AddAsync(OrderMessage message, CancellationToken cancellationToken);

    /// <summary>
    /// Bulk UPDATE every unread maker-authored message on
    /// <paramref name="orderId"/> belonging to the customer identified by
    /// <paramref name="customerUserId"/> to stamp
    /// <see cref="OrderMessage.ReadByCounterpartyAt"/> =
    /// <paramref name="asOf"/>. The customer-scope predicate IS baked
    /// into the SQL — a cross-tenant probe touches no rows. Returns the
    /// affected row count.
    ///
    /// <para>
    /// Calls <c>ExecuteUpdateAsync</c> for a single SQL round-trip
    /// regardless of thread length; returns 0 when no unread messages
    /// remain (idempotent MarkAsRead per AC-6).
    /// </para>
    /// </summary>
    Task<int> MarkAsReadForCustomerAsync(
        string orderId,
        string customerUserId,
        DateTimeOffset asOf,
        CancellationToken cancellationToken);

    /// <summary>Symmetric maker-side bulk MarkAsRead sweep.</summary>
    Task<int> MarkAsReadForMakerAsync(
        string orderId,
        string makerId,
        DateTimeOffset asOf,
        CancellationToken cancellationToken);

    /// <summary>
    /// True when a <see cref="OrderMessageAuthorRole.Maker"/>-authored
    /// message exists on <paramref name="orderId"/> with
    /// <see cref="Common.Auditable.CreatedAt"/> strictly after
    /// <paramref name="sinceUtc"/>. Backs the T-0145 7-day auto-escalation
    /// sweep's "did the maker reply?" check — a targeted <c>EXISTS</c>
    /// rather than loading the whole thread (Technical notes). The caller
    /// passes <c>Dispute.CreatedAt</c> as <paramref name="sinceUtc"/> per
    /// the locked anchor (Alternatives Considered Option C).
    /// </summary>
    Task<bool> HasMakerReplySinceAsync(
        string orderId,
        DateTimeOffset sinceUtc,
        CancellationToken cancellationToken);
}
