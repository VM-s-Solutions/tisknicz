using FluentAssertions;
using Makables.Core.Domain.Outbox;

namespace Makables.Tests.Domain.OutboxTests;

/// <summary>
/// Pure-logic classifier for the T-0029 outbox dispatcher routing branch.
/// Test-first per <c>docs/process/tdd-policy.md</c>: <see cref="OutboxEventTypes.IsInvoiceGenerate"/>
/// is a pure boolean classifier (no infra deps, no DB) and falls under the
/// "validation / specification" must-cover surface — the dispatcher's routing
/// table relies on this method matching the canonical event-type string.
///
/// <para>
/// A regression here would either silently route invoice events to the
/// send-email queue (no rendering ever happens) or stall every invoice
/// event as "unknown event type" (every paid order is missing its PDF).
/// Both are customer-trust events; the classifier is the cheapest place
/// to pin the invariant. T-0069 locked decision 2 (separate queue routing).
/// </para>
/// </summary>
public class OutboxEventTypesTests
{
    [Fact]
    public void IsInvoiceGenerate_returns_true_for_the_invoice_generate_constant()
    {
        OutboxEventTypes.IsInvoiceGenerate(OutboxEventTypes.InvoiceGenerate).Should().BeTrue();
        // Pin the literal too — adding the constant + classifier in the same
        // commit could silently rename the string without the classifier
        // noticing; this assertion catches that drift.
        OutboxEventTypes.IsInvoiceGenerate("invoice.generate").Should().BeTrue();
    }

    [Theory]
    [InlineData(OutboxEventTypes.AuthMagicLinkSend)]
    [InlineData(OutboxEventTypes.AuthEmailConfirmationSend)]
    [InlineData(OutboxEventTypes.AuthPasswordResetSend)]
    [InlineData(OutboxEventTypes.OrderPaidCustomerEmail)]
    [InlineData(OutboxEventTypes.OrderPlacedMakerEmail)]
    [InlineData("order.shipped.send")]
    [InlineData("future.unknown.event")]
    [InlineData("")]
    public void IsInvoiceGenerate_returns_false_for_other_event_types(string eventType)
    {
        OutboxEventTypes.IsInvoiceGenerate(eventType).Should().BeFalse();
    }

    [Fact]
    public void IsInvoiceGenerate_and_IsEmailSend_are_disjoint_for_invoice_generate()
    {
        // The dispatcher's routing branch depends on disjoint classifiers —
        // a single event type cannot route to both the send-email queue
        // AND the generate-invoice queue. T-0069 locked decision 2.
        OutboxEventTypes.IsInvoiceGenerate(OutboxEventTypes.InvoiceGenerate).Should().BeTrue();
        OutboxEventTypes.IsEmailSend(OutboxEventTypes.InvoiceGenerate).Should().BeFalse();
    }

    // ---- T-0072: IsGenerateLabel classifier (test-first per TDD policy) ----

    [Fact]
    public void IsGenerateLabel_returns_true_for_the_shipping_generate_label_constant()
    {
        OutboxEventTypes.IsGenerateLabel(OutboxEventTypes.ShippingGenerateLabel).Should().BeTrue();
        // Pin the literal too — adding the constant + classifier in the same
        // commit could silently rename the string without the classifier
        // noticing; this assertion catches that drift.
        OutboxEventTypes.IsGenerateLabel("shipping.generate.label").Should().BeTrue();
    }

    [Theory]
    [InlineData(OutboxEventTypes.AuthMagicLinkSend)]
    [InlineData(OutboxEventTypes.AuthEmailConfirmationSend)]
    [InlineData(OutboxEventTypes.AuthPasswordResetSend)]
    [InlineData(OutboxEventTypes.OrderPaidCustomerEmail)]
    [InlineData(OutboxEventTypes.OrderPlacedMakerEmail)]
    [InlineData(OutboxEventTypes.OrderAcceptedCustomerEmail)]
    [InlineData(OutboxEventTypes.OrderShippedCustomerEmail)]
    [InlineData(OutboxEventTypes.InvoiceGenerate)]
    [InlineData("future.unknown.event")]
    [InlineData("")]
    public void IsGenerateLabel_returns_false_for_other_event_types(string eventType)
    {
        OutboxEventTypes.IsGenerateLabel(eventType).Should().BeFalse();
    }

    [Fact]
    public void IsGenerateLabel_and_IsEmailSend_are_disjoint_for_shipping_generate_label()
    {
        // T-0072 locked decision: queue-per-event-class. The dispatcher
        // routes shipping.generate.label to its OWN queue, not the email
        // queue. Disjoint classifiers prevent silent mis-routing.
        OutboxEventTypes.IsGenerateLabel(OutboxEventTypes.ShippingGenerateLabel).Should().BeTrue();
        OutboxEventTypes.IsEmailSend(OutboxEventTypes.ShippingGenerateLabel).Should().BeFalse();
        OutboxEventTypes.IsInvoiceGenerate(OutboxEventTypes.ShippingGenerateLabel).Should().BeFalse();
    }

    [Fact]
    public void IsEmailSend_returns_true_for_OrderAcceptedCustomerEmail()
    {
        // T-0071: the customer-acceptance email rides the send-email queue.
        OutboxEventTypes.IsEmailSend(OutboxEventTypes.OrderAcceptedCustomerEmail).Should().BeTrue();
    }

    [Fact]
    public void IsEmailSend_returns_true_for_OrderShippedCustomerEmail()
    {
        // T-0072: the customer-shipped email rides the send-email queue.
        OutboxEventTypes.IsEmailSend(OutboxEventTypes.OrderShippedCustomerEmail).Should().BeTrue();
    }

    // ---- T-0076: OrderDeliveredCustomerEmail email-routing classifier (test-first per TDD policy) ----

    [Fact]
    public void IsEmailSend_returns_true_for_OrderDeliveredCustomerEmail()
    {
        // T-0076: the customer-delivered email rides the send-email queue.
        // Pin the constant + classifier in one assert so renaming either
        // side trips this test.
        OutboxEventTypes.IsEmailSend(OutboxEventTypes.OrderDeliveredCustomerEmail).Should().BeTrue();
        OutboxEventTypes.IsEmailSend("order.delivered.customerEmail").Should().BeTrue();
    }

    [Fact]
    public void OrderDeliveredCustomerEmail_constant_equals_canonical_string()
    {
        // Drift defence: if a future refactor renames the constant, this
        // test will fail loudly so the seed migration + EmailSendService
        // branch can be kept in sync.
        OutboxEventTypes.OrderDeliveredCustomerEmail.Should().Be("order.delivered.customerEmail");
    }

    [Fact]
    public void OrderDeliveredCustomerEmail_routes_to_email_queue_only()
    {
        // Disjoint classifier invariant from T-0069/T-0072: a single event
        // type cannot route to multiple queues. The delivered email goes
        // through send-email; never invoice-generate, never generate-label.
        OutboxEventTypes.IsEmailSend(OutboxEventTypes.OrderDeliveredCustomerEmail).Should().BeTrue();
        OutboxEventTypes.IsInvoiceGenerate(OutboxEventTypes.OrderDeliveredCustomerEmail).Should().BeFalse();
        OutboxEventTypes.IsGenerateLabel(OutboxEventTypes.OrderDeliveredCustomerEmail).Should().BeFalse();
    }

    // ---- T-0106: the retired carrierSourced stub event must be GONE ----

    [Fact]
    public void Retired_carrierSourced_event_is_not_routed_anywhere()
    {
        // T-0106 deleted OutboxEventTypes.OrderDisputedCarrierSourced (the
        // T-0078 stub event was never routed). Pin the literal so a
        // re-introduction under the old name is caught.
        OutboxEventTypes.IsEmailSend("order.disputed.carrierSourced").Should().BeFalse();
        OutboxEventTypes.IsInvoiceGenerate("order.disputed.carrierSourced").Should().BeFalse();
        OutboxEventTypes.IsGenerateLabel("order.disputed.carrierSourced").Should().BeFalse();
    }

    // ---- T-0079: OrderMessagePosted{Customer,Maker}Email routing (test-first) ----

    [Fact]
    public void IsEmailSend_returns_true_for_OrderMessagePostedCustomerEmail()
    {
        // T-0079: the maker-posted-message digest email rides the
        // send-email queue. Pin the constant + classifier in one assert
        // so renaming either side trips this test.
        OutboxEventTypes.IsEmailSend(OutboxEventTypes.OrderMessagePostedCustomerEmail).Should().BeTrue();
        OutboxEventTypes.IsEmailSend("order.message.posted.customerEmail").Should().BeTrue();
    }

    [Fact]
    public void IsEmailSend_returns_true_for_OrderMessagePostedMakerEmail()
    {
        OutboxEventTypes.IsEmailSend(OutboxEventTypes.OrderMessagePostedMakerEmail).Should().BeTrue();
        OutboxEventTypes.IsEmailSend("order.message.posted.makerEmail").Should().BeTrue();
    }

    [Fact]
    public void OrderMessagePostedCustomerEmail_constant_equals_canonical_string()
    {
        OutboxEventTypes.OrderMessagePostedCustomerEmail.Should().Be("order.message.posted.customerEmail");
    }

    [Fact]
    public void OrderMessagePostedMakerEmail_constant_equals_canonical_string()
    {
        OutboxEventTypes.OrderMessagePostedMakerEmail.Should().Be("order.message.posted.makerEmail");
    }

    [Fact]
    public void OrderMessagePostedCustomerEmail_routes_to_email_queue_only()
    {
        OutboxEventTypes.IsEmailSend(OutboxEventTypes.OrderMessagePostedCustomerEmail).Should().BeTrue();
        OutboxEventTypes.IsInvoiceGenerate(OutboxEventTypes.OrderMessagePostedCustomerEmail).Should().BeFalse();
        OutboxEventTypes.IsGenerateLabel(OutboxEventTypes.OrderMessagePostedCustomerEmail).Should().BeFalse();
    }

    [Fact]
    public void OrderMessagePostedMakerEmail_routes_to_email_queue_only()
    {
        OutboxEventTypes.IsEmailSend(OutboxEventTypes.OrderMessagePostedMakerEmail).Should().BeTrue();
        OutboxEventTypes.IsInvoiceGenerate(OutboxEventTypes.OrderMessagePostedMakerEmail).Should().BeFalse();
        OutboxEventTypes.IsGenerateLabel(OutboxEventTypes.OrderMessagePostedMakerEmail).Should().BeFalse();
    }

    // ---- T-0083: OrderCancelledCustomerEmail routing (test-first) ----

    [Fact]
    public void IsEmailSend_returns_true_for_OrderCancelledCustomerEmail()
    {
        // T-0083: the auto-cancel customer notification rides the
        // send-email queue. Pin constant + classifier in one assert.
        OutboxEventTypes.IsEmailSend(OutboxEventTypes.OrderCancelledCustomerEmail).Should().BeTrue();
        OutboxEventTypes.IsEmailSend("order.cancelled.customerEmail").Should().BeTrue();
    }

    [Fact]
    public void OrderCancelledCustomerEmail_constant_equals_canonical_string()
    {
        OutboxEventTypes.OrderCancelledCustomerEmail.Should().Be("order.cancelled.customerEmail");
    }

    [Fact]
    public void OrderCancelledCustomerEmail_routes_to_email_queue_only()
    {
        OutboxEventTypes.IsEmailSend(OutboxEventTypes.OrderCancelledCustomerEmail).Should().BeTrue();
        OutboxEventTypes.IsInvoiceGenerate(OutboxEventTypes.OrderCancelledCustomerEmail).Should().BeFalse();
        OutboxEventTypes.IsGenerateLabel(OutboxEventTypes.OrderCancelledCustomerEmail).Should().BeFalse();
    }

    // ---- T-0105: OrderRefundedCustomerEmail routing (test-first) ----

    [Fact]
    public void IsEmailSend_returns_true_for_OrderRefundedCustomerEmail()
    {
        // T-0105: the refund customer notification rides the send-email
        // queue. Pin constant + classifier in one assert.
        OutboxEventTypes.IsEmailSend(OutboxEventTypes.OrderRefundedCustomerEmail).Should().BeTrue();
        OutboxEventTypes.IsEmailSend("order.refunded.customerEmail").Should().BeTrue();
    }

    [Fact]
    public void OrderRefundedCustomerEmail_constant_equals_canonical_string()
    {
        OutboxEventTypes.OrderRefundedCustomerEmail.Should().Be("order.refunded.customerEmail");
    }

    [Fact]
    public void OrderRefundedCustomerEmail_routes_to_email_queue_only()
    {
        OutboxEventTypes.IsEmailSend(OutboxEventTypes.OrderRefundedCustomerEmail).Should().BeTrue();
        OutboxEventTypes.IsInvoiceGenerate(OutboxEventTypes.OrderRefundedCustomerEmail).Should().BeFalse();
        OutboxEventTypes.IsGenerateLabel(OutboxEventTypes.OrderRefundedCustomerEmail).Should().BeFalse();
    }

    // ---- T-0106: dispute notification routing (test-first) ----

    [Fact]
    public void IsEmailSend_returns_true_for_OrderDisputedAdminEmail()
    {
        OutboxEventTypes.IsEmailSend(OutboxEventTypes.OrderDisputedAdminEmail).Should().BeTrue();
        OutboxEventTypes.IsEmailSend("order.disputed.adminEmail").Should().BeTrue();
    }

    [Fact]
    public void IsEmailSend_returns_true_for_OrderDisputeResolvedCustomerEmail()
    {
        OutboxEventTypes.IsEmailSend(OutboxEventTypes.OrderDisputeResolvedCustomerEmail).Should().BeTrue();
        OutboxEventTypes.IsEmailSend("order.disputeResolved.customerEmail").Should().BeTrue();
    }

    [Fact]
    public void Dispute_email_events_route_to_email_queue_only()
    {
        OutboxEventTypes.IsInvoiceGenerate(OutboxEventTypes.OrderDisputedAdminEmail).Should().BeFalse();
        OutboxEventTypes.IsGenerateLabel(OutboxEventTypes.OrderDisputedAdminEmail).Should().BeFalse();
        OutboxEventTypes.IsInvoiceGenerate(OutboxEventTypes.OrderDisputeResolvedCustomerEmail).Should().BeFalse();
        OutboxEventTypes.IsGenerateLabel(OutboxEventTypes.OrderDisputeResolvedCustomerEmail).Should().BeFalse();
    }
}
