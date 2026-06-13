using Makables.Core.Domain.Orders;

namespace Makables.Core.Domain.Payouts;

/// <summary>
/// A coarse payout-batch candidate: a Delivered, unbatched
/// <see cref="Orders.Order"/> paired with the slice of its maker the claim
/// + artifacts need (bank account for the Q5 exclusion + the CSV row; id +
/// company name for distinct-maker counting, fee-invoice recipient, and the
/// CSV message). The <see cref="Order"/> is the TRACKED aggregate so the
/// claim handler can call <see cref="Orders.Order.AssignToPayoutBatch"/> on
/// it inside the request UoW.
///
/// <para>
/// Order has no <c>Maker</c> EF navigation (the FK is a scalar
/// <c>MakerId</c>), so the repository materialises this projection with an
/// explicit join rather than an <c>Include</c>. The per-order verdict comes
/// from <see cref="PayoutEligibility.Classify"/>.
/// </para>
/// </summary>
public sealed record PayoutCandidate(
    Order Order,
    string MakerId,
    string MakerCompanyName,
    string? MakerBankAccount);
