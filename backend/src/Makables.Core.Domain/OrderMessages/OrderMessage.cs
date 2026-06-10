using Makables.Core.Domain.Common;

namespace Makables.Core.Domain.OrderMessages;

/// <summary>
/// One message posted to the two-party thread on a specific
/// <see cref="Orders.Order"/> (T-0079). Customer ↔ Maker only; admin gets
/// READ-ONLY access via the existing T-0111 admin tooling. Child of the
/// <see cref="Orders.Order"/> aggregate scoping; FK to <c>orders.id</c>
/// + FK to <c>users.id</c> for the author audit trail.
///
/// <para>
/// <b>Soft-delete</b> via <see cref="Auditable.IsActive"/> /
/// <see cref="Auditable.DeactivatedAt"/>: deactivated rows are excluded
/// from the read-side via the global query filter. Hard-delete only via
/// the GDPR right-to-erasure flow (T-0110).
/// </para>
///
/// <para>
/// <b>Read-receipts.</b> <see cref="ReadByCounterpartyAt"/> is stamped by
/// the <c>MarkAsRead</c> bulk-UPDATE sweep on each <see cref="OrderId"/>
/// — set when the counterparty reads the thread. Not exposed via the
/// API at MVP (no "seen at HH:MM" badge on the sender side); used inside
/// the platform for analytics + the unread-count denormalization on
/// <see cref="Orders.Order.CustomerUnreadMessageCount"/> /
/// <see cref="Orders.Order.MakerUnreadMessageCount"/>.
/// </para>
/// </summary>
public sealed class OrderMessage : Auditable
{
    /// <summary>Wire-shape of the message body column.</summary>
    public const int MaxBodyLength = 2000;

    /// <summary>FK to <see cref="Orders.Order.Id"/>. Immutable.</summary>
    public string OrderId { get; private set; } = default!;

    /// <summary>Which party posted this message.</summary>
    public OrderMessageAuthorRole AuthorRole { get; private set; }

    /// <summary>
    /// FK to <c>users.id</c> — denormalized audit-trail identity. For a
    /// <see cref="OrderMessageAuthorRole.Customer"/> post this equals
    /// <see cref="Orders.Order.CustomerUserId"/>; for a
    /// <see cref="OrderMessageAuthorRole.Maker"/> post this is the
    /// underlying user behind the maker row (<c>Maker.UserId</c>).
    /// </summary>
    public string AuthorUserId { get; private set; } = default!;

    /// <summary>The message body. Trimmed at construction; max 2000 chars.</summary>
    public string Body { get; private set; } = default!;

    /// <summary>
    /// When the counterparty (the opposite party from <see cref="AuthorRole"/>)
    /// last marked their inbox read. Set by the <c>MarkAsRead*</c> bulk
    /// UPDATE; null until first read. Set-once shape — once stamped,
    /// re-reads do not bump the timestamp.
    /// </summary>
    public DateTimeOffset? ReadByCounterpartyAt { get; private set; }

    // EF Core needs a parameterless ctor.
    private OrderMessage() { }

    /// <summary>
    /// Build a new <see cref="OrderMessage"/> row. Validates the body and
    /// throws <see cref="ArgumentException"/> on programmer-error inputs
    /// (mirrors <see cref="Orders.Order.Create"/> /
    /// <see cref="Orders.OrderAttachment.Create"/> precedent). User-input
    /// validation (body length / whitespace) ALSO runs in the feature's
    /// <c>Validator</c> at the command boundary so the empty-body /
    /// too-long-body paths surface as typed
    /// <see cref="BusinessErrorMessage.OrderMessageBodyEmpty"/> /
    /// <see cref="BusinessErrorMessage.OrderMessageBodyTooLong"/> errors
    /// before reaching the aggregate.
    /// </summary>
    public static OrderMessage Create(
        string id,
        string orderId,
        OrderMessageAuthorRole authorRole,
        string authorUserId,
        string body,
        string countryCode)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Id is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(orderId))
            throw new ArgumentException("OrderId is required.", nameof(orderId));
        if (string.IsNullOrWhiteSpace(authorUserId))
            throw new ArgumentException("AuthorUserId is required.", nameof(authorUserId));
        if (string.IsNullOrWhiteSpace(body))
            throw new ArgumentException("Body is required.", nameof(body));

        var trimmedBody = body.Trim();
        if (trimmedBody.Length == 0)
            throw new ArgumentException("Body cannot be whitespace-only.", nameof(body));
        if (trimmedBody.Length > MaxBodyLength)
            throw new ArgumentException(
                $"Body must be at most {MaxBodyLength} chars.", nameof(body));

        if (!Enum.IsDefined(authorRole))
            throw new ArgumentException(
                $"AuthorRole '{authorRole}' is not a defined enum value.", nameof(authorRole));

        if (string.IsNullOrWhiteSpace(countryCode) || countryCode.Length != 2)
            throw new ArgumentException("CountryCode must be 2 chars (ISO 3166-1 alpha-2).", nameof(countryCode));

        return new OrderMessage
        {
            Id = id,
            OrderId = orderId,
            AuthorRole = authorRole,
            AuthorUserId = authorUserId,
            Body = trimmedBody,
            CountryCode = countryCode.ToUpperInvariant(),
        };
    }

    /// <summary>
    /// Test seam — lets the integration / unit tests stamp the read
    /// timestamp without reaching into reflection. Not surfaced as a
    /// public mutation path; the production read sweep uses the
    /// repository's bulk <c>ExecuteUpdateAsync</c>.
    /// </summary>
    internal void MarkReadByCounterparty(DateTimeOffset at)
    {
        if (ReadByCounterpartyAt is null)
        {
            ReadByCounterpartyAt = at;
        }
    }
}
