using FluentAssertions;
using Makables.Core.AppServices.Features.CountryConfigurations;

namespace Makables.Tests.Domain.Configuration;

/// <summary>
/// Pure-logic predicates for the T-0108 provider-change retype gate
/// (US-admin-0006 AC-3). Written red-first per the TDD policy — both helpers
/// are stateless string comparisons shared by the handler and the test.
///
/// <para>
/// <see cref="UpdateCountryConfiguration.RequiresConfirmation"/> answers
/// "did any of the four provider fields change vs the current row?"; when it
/// does, <see cref="UpdateCountryConfiguration.ConfirmationMatches"/> answers
/// "did the admin retype the high-stakes (payment-first) new value exactly?".
/// </para>
/// </summary>
public sealed class ProviderChangeConfirmationTests
{
    [Fact]
    public void RequiresConfirmation_is_false_when_no_provider_field_changed()
    {
        var changed = UpdateCountryConfiguration.RequiresConfirmation(
            paymentChanged: false, shippingChanged: false,
            registryChanged: false, emailChanged: false);

        changed.Should().BeFalse();
    }

    [Fact]
    public void RequiresConfirmation_is_true_when_any_provider_field_changed()
    {
        UpdateCountryConfiguration.RequiresConfirmation(true, false, false, false).Should().BeTrue();
        UpdateCountryConfiguration.RequiresConfirmation(false, true, false, false).Should().BeTrue();
        UpdateCountryConfiguration.RequiresConfirmation(false, false, true, false).Should().BeTrue();
        UpdateCountryConfiguration.RequiresConfirmation(false, false, false, true).Should().BeTrue();
    }

    [Fact]
    public void ConfirmationMatches_requires_the_payment_new_value_when_payment_changed()
    {
        // Payment is the highest-stakes field — when it changes, the retype
        // must equal the NEW payment provider value, regardless of what else
        // changed.
        var ok = UpdateCountryConfiguration.ConfirmationMatches(
            confirmedProviderCode: "stripe",
            paymentChanged: true, newPayment: "stripe",
            shippingChanged: true, newShipping: "dpd",
            registryChanged: false, newRegistry: "ares",
            emailChanged: false, newEmail: "sendgrid");

        ok.Should().BeTrue();
    }

    [Fact]
    public void ConfirmationMatches_rejects_a_null_or_mismatched_retype()
    {
        UpdateCountryConfiguration.ConfirmationMatches(
            confirmedProviderCode: null,
            paymentChanged: true, newPayment: "stripe",
            shippingChanged: false, newShipping: "packeta",
            registryChanged: false, newRegistry: "ares",
            emailChanged: false, newEmail: "sendgrid").Should().BeFalse();

        UpdateCountryConfiguration.ConfirmationMatches(
            confirmedProviderCode: "comgate",
            paymentChanged: true, newPayment: "stripe",
            shippingChanged: false, newShipping: "packeta",
            registryChanged: false, newRegistry: "ares",
            emailChanged: false, newEmail: "sendgrid").Should().BeFalse();
    }

    [Fact]
    public void ConfirmationMatches_uses_the_single_changed_field_when_payment_unchanged()
    {
        // Only shipping changed → retype must equal the new shipping value.
        var ok = UpdateCountryConfiguration.ConfirmationMatches(
            confirmedProviderCode: "dpd",
            paymentChanged: false, newPayment: "comgate",
            shippingChanged: true, newShipping: "dpd",
            registryChanged: false, newRegistry: "ares",
            emailChanged: false, newEmail: "sendgrid");

        ok.Should().BeTrue();
    }
}
