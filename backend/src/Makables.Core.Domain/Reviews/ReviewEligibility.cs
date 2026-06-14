using Makables.Core.Domain.Orders;

namespace Makables.Core.Domain.Reviews;

/// <summary>
/// Pure eligibility predicate for the review write-side (T-0100 §C.8
/// step 3, Q3 user-locked): an order is reviewable iff its state is
/// <see cref="OrderState.Delivered"/> or <see cref="OrderState.Completed"/>,
/// with NO time limit. Delivery is the moment the customer can judge the
/// maker; <see cref="OrderState.Completed"/> is the later payout-side
/// bookkeeping step and also qualifies. Extracted as a pure check so it
/// can be pinned in isolation and reused by the read-side "reviewable
/// orders" query and the <c>SubmitReview</c> handler gate.
/// </summary>
public static class ReviewEligibility
{
    public static bool IsReviewableState(OrderState state) =>
        state is OrderState.Delivered or OrderState.Completed;
}
