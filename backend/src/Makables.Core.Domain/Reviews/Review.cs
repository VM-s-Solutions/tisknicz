using Makables.Core.Domain.Common;

namespace Makables.Core.Domain.Reviews;

/// <summary>
/// One customer review anchored to ONE delivered order (T-0100, Q1
/// user-locked 2026-06-14). The per-order grain IS the structural
/// anti-abuse defence — a review must trace to a real, paid, delivered
/// order the customer owns; the partial unique index
/// <c>UNIQUE (order_id) WHERE is_active</c> enforces one review per
/// order at the DB level. Mirrors the <see cref="OrderMessages.OrderMessage"/>
/// / <see cref="Orders.Dispute"/> child-entity precedent.
///
/// <para>
/// <b>Immutability asymmetry (Q4).</b> The customer's
/// <see cref="Rating"/> + <see cref="Body"/> are set-once at
/// <see cref="Create"/> and are NEVER mutated afterwards (no edit/delete
/// surfaced; the soft-delete hook is admin-only). The maker's
/// <see cref="MakerReply"/> is one overwritable reply per review —
/// <see cref="AddReply"/> overwrites the prior reply and bumps
/// <see cref="MakerReplyAt"/>.
/// </para>
///
/// <para>
/// <b>Denormalization.</b> <see cref="MakerId"/> is copied off the order
/// at <see cref="Create"/> time so the <c>(maker_id, created_on DESC)</c>
/// list query + the recompute <c>AVG(rating)</c> don't JOIN through
/// Order. <see cref="CustomerUserId"/> is the audit-trail identity and is
/// NEVER exposed to the maker (GDPR data-minimization, consistent with
/// T-0079 / T-0081).
/// </para>
///
/// <para>
/// <b>Soft-delete self-healing.</b> Deactivated rows are excluded by the
/// global <see cref="Auditable"/> query filter AND by the partial unique
/// index (a deactivated review frees the order for a new one). The
/// rating recompute is recompute-from-rows over the ACTIVE set, so a
/// later admin deactivation naturally drops out of the average.
/// </para>
/// </summary>
public sealed class Review : Auditable
{
    /// <summary>Wire-shape of the optional review body column.</summary>
    public const int MaxBodyLength = 1000;

    /// <summary>Wire-shape of the optional maker-reply column.</summary>
    public const int MaxReplyLength = 500;

    /// <summary>Lower bound of the 1..5 star rating.</summary>
    public const short MinRating = 1;

    /// <summary>Upper bound of the 1..5 star rating.</summary>
    public const short MaxRating = 5;

    /// <summary>FK to the reviewed <see cref="Orders.Order"/>. The per-order anchor; immutable.</summary>
    public string OrderId { get; private set; } = default!;

    /// <summary>
    /// FK to the reviewed <see cref="Makers.Maker"/>, denormalized off the
    /// order so the list query + the recompute don't JOIN through Order.
    /// Immutable.
    /// </summary>
    public string MakerId { get; private set; } = default!;

    /// <summary>FK to the reviewing <c>users.id</c> — audit-trail identity. Never exposed to the maker.</summary>
    public string CustomerUserId { get; private set; } = default!;

    /// <summary>1..5 stars. Set-once at <see cref="Create"/>; immutable (Q4).</summary>
    public short Rating { get; private set; }

    /// <summary>Optional ≤1000-char comment. Trimmed at <see cref="Create"/>; null when empty. Immutable (Q4).</summary>
    public string? Body { get; private set; }

    /// <summary>The maker's single public reply (≤500 chars). Null until the maker replies; overwritten by <see cref="AddReply"/>.</summary>
    public string? MakerReply { get; private set; }

    /// <summary>When the maker last set/overwrote <see cref="MakerReply"/>. Null until first reply; bumped on overwrite.</summary>
    public DateTimeOffset? MakerReplyAt { get; private set; }

    // EF Core needs a parameterless ctor.
    private Review() { }

    /// <summary>
    /// Build a new <see cref="Review"/> row. Validates required fields +
    /// the rating range (1..5, <see cref="ArgumentOutOfRangeException"/>)
    /// + the body length cap (1000, <see cref="ArgumentException"/>) as
    /// the programmer-error tail. User-input validation ALSO runs in the
    /// <c>SubmitReview</c> Validator so the range/length paths surface as
    /// typed <see cref="BusinessErrorMessage.ReviewRatingOutOfRange"/> /
    /// <see cref="BusinessErrorMessage.ReviewBodyTooLong"/> first.
    /// <see cref="MakerReply"/> / <see cref="MakerReplyAt"/> start null.
    /// </summary>
    public static Review Create(
        string id,
        string orderId,
        string makerId,
        string customerUserId,
        short rating,
        string? body,
        string countryCode)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Id is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(orderId))
            throw new ArgumentException("OrderId is required.", nameof(orderId));
        if (string.IsNullOrWhiteSpace(makerId))
            throw new ArgumentException("MakerId is required.", nameof(makerId));
        if (string.IsNullOrWhiteSpace(customerUserId))
            throw new ArgumentException("CustomerUserId is required.", nameof(customerUserId));
        if (rating is < MinRating or > MaxRating)
            throw new ArgumentOutOfRangeException(
                nameof(rating), $"Rating must be {MinRating}..{MaxRating} stars.");

        string? trimmedBody = null;
        if (!string.IsNullOrWhiteSpace(body))
        {
            trimmedBody = body.Trim();
            if (trimmedBody.Length > MaxBodyLength)
                throw new ArgumentException(
                    $"Body must be at most {MaxBodyLength} chars.", nameof(body));
        }

        if (string.IsNullOrWhiteSpace(countryCode) || countryCode.Length != 2)
            throw new ArgumentException("CountryCode must be 2 chars (ISO 3166-1 alpha-2).", nameof(countryCode));

        return new Review
        {
            Id = id,
            OrderId = orderId,
            MakerId = makerId,
            CustomerUserId = customerUserId,
            Rating = rating,
            Body = trimmedBody,
            CountryCode = countryCode.ToUpperInvariant(),
        };
    }

    /// <summary>
    /// Set (or overwrite) the maker's single public reply (Q4 — one reply
    /// per review). Trims <paramref name="reply"/>, rejects empty /
    /// whitespace and <c>&gt; 500</c> chars (<see cref="ArgumentException"/>;
    /// the <c>RespondToReview</c> Validator surfaces the typed
    /// <see cref="BusinessErrorMessage.ReviewReplyTooLong"/> /
    /// <see cref="BusinessErrorMessage.ReviewReplyEmpty"/> first). A second
    /// call simply overwrites and bumps <see cref="MakerReplyAt"/>. The
    /// customer's <see cref="Rating"/> / <see cref="Body"/> are NEVER
    /// mutated here.
    /// </summary>
    public Review AddReply(string reply, DateTimeOffset at)
    {
        if (string.IsNullOrWhiteSpace(reply))
            throw new ArgumentException("Reply is required.", nameof(reply));

        var trimmed = reply.Trim();
        if (trimmed.Length > MaxReplyLength)
            throw new ArgumentException(
                $"Reply must be at most {MaxReplyLength} chars.", nameof(reply));

        MakerReply = trimmed;
        MakerReplyAt = at;
        return this;
    }
}
