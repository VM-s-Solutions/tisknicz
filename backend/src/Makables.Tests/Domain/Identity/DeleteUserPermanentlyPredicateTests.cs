using FluentAssertions;
using Makables.Core.AppServices.Features.Users;
using Makables.Core.Domain.Orders;

namespace Makables.Tests.Domain.Identity;

/// <summary>
/// Pure-logic predicates + anonymization transforms for the T-0110 GDPR
/// erasure (US-admin-0016). Written RED-FIRST per the TDD policy — the
/// in-flight state set and the per-aggregate <c>Anonymize*</c> transforms
/// are stateless, infra-free logic and must commit before the feature.
///
/// The in-flight set is the single source of truth shared by the test and
/// the handler's interlock query; the transforms live ON the aggregates so
/// the deletion service composes them rather than reaching into private
/// state. Locked Q-A / Q-B (2026-06-14 deliberation).
/// </summary>
public sealed class DeleteUserPermanentlyPredicateTests
{
    [Fact]
    public void InFlightOrderStates_contains_exactly_the_five_locked_states()
    {
        DeleteUserPermanently.InFlightOrderStates.Should().BeEquivalentTo(new[]
        {
            OrderState.PendingPayment,
            OrderState.Paid,
            OrderState.Accepted,
            OrderState.Shipped,
            // Disputed: escrowed money + an unresolved dispute — erasing the
            // subject mid-dispute is unsafe; the dispute must resolve first.
            OrderState.Disputed,
        });

        // Terminal states are safe to anonymize — they must NOT block the
        // erasure (the interlock only guards money/fulfilment in motion or an
        // unresolved dispute).
        DeleteUserPermanently.InFlightOrderStates.Should().NotContain(OrderState.Delivered);
        DeleteUserPermanently.InFlightOrderStates.Should().NotContain(OrderState.Completed);
        DeleteUserPermanently.InFlightOrderStates.Should().NotContain(OrderState.Cancelled);
        DeleteUserPermanently.InFlightOrderStates.Should().NotContain(OrderState.Refunded);
    }

    [Fact]
    public void AnonymizationSentinel_is_the_literal_Anonymized()
    {
        DeleteUserPermanently.AnonymizationSentinel.Should().Be("Anonymized");
    }
}
