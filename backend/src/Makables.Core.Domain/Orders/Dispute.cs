using Makables.Core.Domain.Common;

namespace Makables.Core.Domain.Orders;

/// <summary>
/// Child entity of <see cref="Order"/> carrying the WHY of a dispute —
/// category, the opener's own words, the source, and (once the admin
/// adjudicates) the resolution outcome + customer-visible notes. T-0106
/// user decision Q2: <c>Order.State = Disputed</c> is the escrow hold +
/// sweep exclusion; this row is the triage record.
///
/// <para>
/// <b>Open ⇔ <see cref="ResolvedAt"/> is null.</b> A partial unique
/// index (<c>UNIQUE (order_id) WHERE resolved_at IS NULL</c>) guarantees
/// at most one OPEN dispute per order — re-opens are Silent-Success at
/// the handler (§C.4) and the concurrent-open race loses at the index.
/// Resolved disputes accumulate as history.
/// </para>
///
/// <para>
/// Evidence lives in the T-0079 order-message thread (posting stays open
/// in <c>Disputed</c> — <c>PendingPayment</c> is the only blocked state);
/// this entity deliberately has no attachment / evidence surface.
/// </para>
/// </summary>
public sealed class Dispute : Auditable
{
    /// <summary>Matches <see cref="Order.MaxCustomerNotesLength"/> (§C.7).</summary>
    public const int MaxDescriptionLength = 2000;

    /// <summary>Customer-VISIBLE — rendered in the resolve email (§C.7).</summary>
    public const int MaxResolutionNotesLength = 2000;

    /// <summary>FK to the disputed order. Immutable.</summary>
    public string OrderId { get; private set; } = default!;

    public DisputeCategory Category { get; private set; }

    /// <summary>The opener's own words, trimmed at the factory. Immutable.</summary>
    public string Description { get; private set; } = default!;

    /// <summary>Who opened it — always stamped server-side.</summary>
    public DisputeSource Source { get; private set; }

    /// <summary>Admin's customer-visible resolution notes; null until resolved.</summary>
    public string? ResolutionNotes { get; private set; }

    /// <summary>Null == OPEN. Set exactly once by <see cref="Resolve"/>.</summary>
    public DateTimeOffset? ResolvedAt { get; private set; }

    public DisputeResolutionOutcome? ResolutionOutcome { get; private set; }

    // EF Core needs a parameterless ctor.
    private Dispute() { }

    /// <summary>
    /// Open a new dispute. Validates required fields + lengths with
    /// <see cref="ArgumentException"/> (programmer-error tail — user-input
    /// validation lives in the command Validators, including the
    /// carrier-reserved category gate §C.6).
    /// </summary>
    public static Dispute Open(
        string id,
        string orderId,
        DisputeCategory category,
        string description,
        DisputeSource source,
        string countryCode)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Id is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(orderId))
            throw new ArgumentException("OrderId is required.", nameof(orderId));
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Description is required.", nameof(description));

        var trimmedDescription = description.Trim();
        if (trimmedDescription.Length > MaxDescriptionLength)
            throw new ArgumentException(
                $"Description must be at most {MaxDescriptionLength} chars.", nameof(description));

        if (string.IsNullOrWhiteSpace(countryCode) || countryCode.Length != 2)
            throw new ArgumentException("CountryCode must be 2 chars (ISO 3166-1 alpha-2).", nameof(countryCode));

        return new Dispute
        {
            Id = id,
            OrderId = orderId,
            Category = category,
            Description = trimmedDescription,
            Source = source,
            CountryCode = countryCode.ToUpperInvariant(),
        };
    }

    /// <summary>
    /// Close the dispute with <paramref name="outcome"/> + the admin's
    /// customer-visible notes. Refuses a double-resolve with
    /// <see cref="BusinessErrorMessage.OrderDisputeNotOpen"/> — loud
    /// rather than Silent-Success because a silently "succeeding" second
    /// resolve with a DIFFERENT outcome would mask an admin race (§C.4).
    /// The first resolution is immutable.
    /// </summary>
    public BusinessResult Resolve(IClock clock, DisputeResolutionOutcome outcome, string resolutionNotes)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (string.IsNullOrWhiteSpace(resolutionNotes))
            throw new ArgumentException("ResolutionNotes is required.", nameof(resolutionNotes));

        var trimmedNotes = resolutionNotes.Trim();
        if (trimmedNotes.Length > MaxResolutionNotesLength)
            throw new ArgumentException(
                $"ResolutionNotes must be at most {MaxResolutionNotesLength} chars.", nameof(resolutionNotes));

        if (ResolvedAt is not null)
        {
            return BusinessResult.Failure(
                Error.Conflict("dispute", BusinessErrorMessage.OrderDisputeNotOpen));
        }

        ResolutionOutcome = outcome;
        ResolutionNotes = trimmedNotes;
        ResolvedAt = clock.UtcNow;
        return BusinessResult.Success();
    }
}
