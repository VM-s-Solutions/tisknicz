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

    /// <summary>Wire-shape of <see cref="ReturnCarrierRef"/> (T-0146). Mirrors Order's ShippingCarrierRef cap.</summary>
    public const int MaxReturnCarrierRefLength = 40;

    /// <summary>Wire-shape of <see cref="ReturnTrackingUrl"/> (T-0146). Mirrors <c>Order.MaxShippingCarrierTrackingUrlLength</c>.</summary>
    public const int MaxReturnTrackingUrlLength = 500;

    /// <summary>Wire-shape of <see cref="ReturnReceivedBy"/> (T-0146).</summary>
    public const int MaxReturnReceivedByLength = 200;

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

    /// <summary>
    /// T-0146. Packeta's numeric reference for the customer→maker reverse
    /// shipment, set once by <see cref="SetReturnShipment"/> when an admin
    /// generates the return label. Null until a return label exists —
    /// its presence gates the customer-facing "Stáhnout vratkový štítek"
    /// link (AC-1).
    /// </summary>
    public string? ReturnCarrierRef { get; private set; }

    /// <summary>
    /// T-0146. Customer-facing tracking URL for the reverse shipment,
    /// set together with <see cref="ReturnCarrierRef"/>.
    /// </summary>
    public string? ReturnTrackingUrl { get; private set; }

    /// <summary>
    /// T-0146. When the maker (or admin on their behalf) manually
    /// acknowledged receiving the returned item. Null until acknowledged
    /// — no automated carrier-status sync for the reverse leg (Out of
    /// scope). Set exactly once by <see cref="MarkReturnReceived"/>.
    /// </summary>
    public DateTimeOffset? ReturnReceivedAt { get; private set; }

    /// <summary>
    /// T-0146. Free-text label identifying who recorded the
    /// acknowledgment — the maker's user id or an admin identifier —
    /// captured for the audit trail ahead of the eventual
    /// <c>ResolveDispute.Command</c>.
    /// </summary>
    public string? ReturnReceivedBy { get; private set; }

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

    /// <summary>
    /// T-0146. Attach the reverse Zásilkovna shipment's carrier ref +
    /// tracking URL, generated by <c>GenerateReturnLabel.Command</c>.
    /// Set-once with idempotent same-value semantics, mirroring
    /// <c>PayoutBatch.AttachCsvBlobPath</c>: a re-run with the SAME ref
    /// is a no-op success (Silent Success); a DIFFERENT ref is a loud
    /// conflict — a return label is not replaceable once issued.
    /// </summary>
    public BusinessResult SetReturnShipment(string carrierRef, string trackingUrl)
    {
        if (string.IsNullOrWhiteSpace(carrierRef))
            throw new ArgumentException("CarrierRef is required.", nameof(carrierRef));
        if (string.IsNullOrWhiteSpace(trackingUrl))
            throw new ArgumentException("TrackingUrl is required.", nameof(trackingUrl));

        var trimmedRef = carrierRef.Trim();
        var trimmedUrl = trackingUrl.Trim();
        if (trimmedRef.Length > MaxReturnCarrierRefLength)
            throw new ArgumentException(
                $"CarrierRef must be at most {MaxReturnCarrierRefLength} chars.", nameof(carrierRef));
        if (trimmedUrl.Length > MaxReturnTrackingUrlLength)
            throw new ArgumentException(
                $"TrackingUrl must be at most {MaxReturnTrackingUrlLength} chars.", nameof(trackingUrl));

        if (ReturnCarrierRef is not null)
        {
            if (string.Equals(ReturnCarrierRef, trimmedRef, StringComparison.Ordinal)
                && string.Equals(ReturnTrackingUrl, trimmedUrl, StringComparison.Ordinal))
            {
                return BusinessResult.Success();
            }

            return BusinessResult.Failure(
                Error.Conflict("returnCarrierRef", BusinessErrorMessage.DisputeReturnShipmentAlreadySet));
        }

        ReturnCarrierRef = trimmedRef;
        ReturnTrackingUrl = trimmedUrl;
        return BusinessResult.Success();
    }

    /// <summary>
    /// T-0146. Manual "the maker received the return" acknowledgment
    /// (AC-5) — no automated carrier-status sync for the reverse leg
    /// (Out of scope). Requires a return shipment to already exist.
    /// Set-once; a second call is a loud conflict (mirrors
    /// <see cref="Resolve"/>'s re-resolve posture — a silently
    /// "succeeding" second ack with a different recorder would mask a
    /// race).
    /// </summary>
    public BusinessResult MarkReturnReceived(IClock clock, string receivedBy)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (string.IsNullOrWhiteSpace(receivedBy))
            throw new ArgumentException("ReceivedBy is required.", nameof(receivedBy));

        var trimmed = receivedBy.Trim();
        if (trimmed.Length > MaxReturnReceivedByLength)
            throw new ArgumentException(
                $"ReceivedBy must be at most {MaxReturnReceivedByLength} chars.", nameof(receivedBy));

        if (ReturnCarrierRef is null)
        {
            return BusinessResult.Failure(
                Error.Conflict("returnCarrierRef", BusinessErrorMessage.DisputeReturnShipmentNotGenerated));
        }

        if (ReturnReceivedAt is not null)
        {
            return BusinessResult.Failure(
                Error.Conflict("returnReceivedAt", BusinessErrorMessage.DisputeReturnAlreadyReceived));
        }

        ReturnReceivedAt = clock.UtcNow;
        ReturnReceivedBy = trimmed;
        return BusinessResult.Success();
    }
}
