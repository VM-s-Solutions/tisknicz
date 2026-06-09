using Makables.Core.Domain.Common;

namespace Makables.Core.Domain.OrderMessages;

/// <summary>
/// Read-side projection seam for the order-message thread (T-0079).
/// Separate from <see cref="IOrderMessageRepository"/> per ADR 0023 —
/// projection-only reads (<c>AsNoTracking</c> + <c>IgnoreAutoIncludes</c>)
/// split from the write-scoped repository.
///
/// <para>
/// Both methods carry the audience scope WHERE-predicate (customer or
/// maker) baked into the EF expression tree — the predicate IS the
/// IDOR shield. A cross-tenant probe simply yields
/// <see cref="PagedData{T}.Empty"/>.
/// </para>
/// </summary>
public interface IOrderMessageQueries
{
    /// <summary>
    /// Paged customer-scoped message list for one order, sorted
    /// <c>CreatedAt DESC, Id DESC</c> (newest first; tiebreak on
    /// lexicographically-ordered ulid id). Returns
    /// <see cref="PagedData{T}.Empty"/> when the order belongs to
    /// another customer (no IDOR oracle).
    /// </summary>
    Task<PagedData<OrderMessageDto>> GetByOrderForCustomerAsync(
        string orderId,
        string customerUserId,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    /// <summary>Symmetric maker-scoped paged read.</summary>
    Task<PagedData<OrderMessageDto>> GetByOrderForMakerAsync(
        string orderId,
        string makerId,
        int page,
        int pageSize,
        CancellationToken cancellationToken);
}
