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
}
