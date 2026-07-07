using Makables.Core.Domain.Common;

namespace Makables.Core.Domain.Payouts;

/// <summary>
/// A negative line item against a maker's next <see cref="PayoutBatch"/> —
/// resolves Q-0037: the maker-borne T-0146 return-shipping cost is
/// deducted from the maker's next payout batch rather than reflected on a
/// fee-invoice line. Born UNAPPLIED (<see cref="PayoutBatchId"/> null);
/// <c>CreatePayoutBatch.Handler</c> claims every maker's pending deductions
/// into whichever batch next pays that maker, subtracting the sum from
/// the maker's payout total and stamping <see cref="PayoutBatchId"/> —
/// mirrors the Order→PayoutBatch claim shape (<c>Order.AssignToPayoutBatch</c>)
/// so a deduction is claimed at most once (set-once FK, never reassigned).
///
/// <para>
/// Admin-only aggregate per ADR 0013 (money-adjacent record, same
/// visibility class as <see cref="PayoutBatch"/>).
/// </para>
/// </summary>
public sealed class PayoutDeduction : Auditable
{
    /// <summary>Wire-shape of <see cref="Currency"/>. ISO 4217.</summary>
    public const int CurrencyLength = 3;

    /// <summary>FK to the maker being charged. Immutable.</summary>
    public string MakerId { get; private set; } = default!;

    /// <summary>FK to the dispute whose return generated this cost. Immutable.</summary>
    public string DisputeId { get; private set; } = default!;

    public PayoutDeductionReason Reason { get; private set; }

    /// <summary>Positive minor-unit amount to SUBTRACT from the maker's payout total.</summary>
    public long AmountMinor { get; private set; }

    public string Currency { get; private set; } = default!;

    /// <summary>
    /// Null while unclaimed. Set exactly once by
    /// <see cref="ApplyToPayoutBatch"/> when a payout batch is created
    /// that includes this maker.
    /// </summary>
    public string? PayoutBatchId { get; private set; }

    // EF Core needs a parameterless ctor.
    private PayoutDeduction() { }

    public static PayoutDeduction Create(
        string id,
        string makerId,
        string disputeId,
        PayoutDeductionReason reason,
        long amountMinor,
        string currency,
        string countryCode)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Id is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(makerId))
            throw new ArgumentException("MakerId is required.", nameof(makerId));
        if (string.IsNullOrWhiteSpace(disputeId))
            throw new ArgumentException("DisputeId is required.", nameof(disputeId));
        if (amountMinor <= 0)
            throw new ArgumentException("AmountMinor must be positive.", nameof(amountMinor));
        if (string.IsNullOrWhiteSpace(currency) || currency.Length != CurrencyLength)
            throw new ArgumentException("Currency must be a 3-char ISO 4217 code.", nameof(currency));
        if (string.IsNullOrWhiteSpace(countryCode) || countryCode.Length != 2)
            throw new ArgumentException("CountryCode must be 2 chars (ISO 3166-1 alpha-2).", nameof(countryCode));

        return new PayoutDeduction
        {
            Id = id,
            MakerId = makerId,
            DisputeId = disputeId,
            Reason = reason,
            AmountMinor = amountMinor,
            Currency = currency.ToUpperInvariant(),
            CountryCode = countryCode.ToUpperInvariant(),
        };
    }

    /// <summary>
    /// Claim this deduction into <paramref name="payoutBatchId"/>.
    /// Set-once — refuses a re-claim of an already-applied row (a
    /// deduction is consumed by exactly one batch).
    /// </summary>
    public BusinessResult ApplyToPayoutBatch(string payoutBatchId)
    {
        if (string.IsNullOrWhiteSpace(payoutBatchId))
            throw new ArgumentException("PayoutBatchId is required.", nameof(payoutBatchId));

        if (PayoutBatchId is not null)
        {
            return BusinessResult.Failure(
                Error.Conflict("payoutBatchId", BusinessErrorMessage.PayoutDeductionAlreadyApplied));
        }

        PayoutBatchId = payoutBatchId;
        return BusinessResult.Success();
    }
}
