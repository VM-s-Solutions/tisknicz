namespace Makables.Core.Domain.OrderMessages;

/// <summary>
/// Flat read-shape for one row in the order-message thread view (T-0079).
/// Projected directly from <see cref="OrderMessage"/> + the author's
/// display name (Customer → Order.ContactName snapshot;
/// Maker → Maker.CompanyName) via a single SQL round-trip in
/// <see cref="IOrderMessageQueries"/>.
///
/// <para>
/// <see cref="IsMine"/> is computed at projection time based on the
/// requesting session's audience:
/// <list type="bullet">
///   <item><description>Customer host: true when <see cref="AuthorRole"/>
///     == <see cref="OrderMessageAuthorRole.Customer"/>.</description></item>
///   <item><description>Maker host: true when <see cref="AuthorRole"/>
///     == <see cref="OrderMessageAuthorRole.Maker"/>.</description></item>
/// </list>
/// Lets the frontend right-align "my" bubbles vs left-align counterparty
/// bubbles without re-deriving the same predicate per-row.
/// </para>
///
/// <para>
/// Lives in <c>Core.Domain</c> alongside <see cref="IOrderMessageQueries"/>
/// per the T-0049a precedent — the DTO carries no domain behaviour.
/// </para>
/// </summary>
public sealed record OrderMessageDto(
    string Id,
    string OrderId,
    OrderMessageAuthorRole AuthorRole,
    string AuthorName,
    string Body,
    DateTimeOffset CreatedAt,
    bool IsMine);
