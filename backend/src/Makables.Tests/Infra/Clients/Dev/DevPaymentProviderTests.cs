using FluentAssertions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Payments;
using Makables.Infra.Clients.Dev;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Makables.Tests.Infra.Clients.Dev;

/// <summary>
/// Pins the non-production payment bypass contract: it hands the browser
/// a URL back into our own Customer host instead of a gateway, tags every
/// reference it issues so the confirm endpoint can tell a synthetic
/// session from a real one, and refuses to produce a redirect at all when
/// it is misconfigured.
/// </summary>
public class DevPaymentProviderTests
{
    private const string ConfirmBase = "http://localhost:5001";

    private readonly IClock _clock = Substitute.For<IClock>();

    private DevPaymentProvider BuildSut(string confirmBaseUrl = ConfirmBase)
    {
        _clock.UtcNow.Returns(new DateTimeOffset(2026, 8, 2, 10, 0, 0, TimeSpan.Zero));
        return new DevPaymentProvider(
            Options.Create(new DevPaymentOptions { Enabled = true, ConfirmBaseUrl = confirmBaseUrl }),
            _clock,
            NullLogger<DevPaymentProvider>.Instance);
    }

    private static Order BuildOrder() => Order.Create(
        id: "ord-1",
        orderNumber: "M-CZ-20260042",
        customerUserId: "user-1",
        makerId: "maker-1",
        productId: "prod-1",
        contactName: "Anna",
        contactEmail: "anna@example.cz",
        contactPhone: "+420 777 123 456",
        productPriceAmountMinor: 50000,
        shippingPriceAmountMinor: 7900,
        platformFeeAmountMinor: 7500,
        makerPayoutAmountMinor: 50400,
        totalAmountMinor: 57900,
        currency: "CZK",
        vatRateBp: 2100,
        shippingMethod: ShippingMethod.ZasilkovnaPickupPoint,
        zasilkovnaPickupPointId: "pp-42",
        countryCode: "CZ");

    [Fact]
    public async Task CreatePayment_returns_confirm_url_on_our_own_host_with_a_marked_ref()
    {
        var order = BuildOrder();

        var result = await BuildSut().CreatePaymentAsync(order, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var session = result.Value!;
        session.ProviderRef.Should().StartWith(DevPaymentProvider.ProviderRefPrefix);
        session.RedirectUrl.Should().Be(
            $"{ConfirmBase}/api/v1/orders/{order.Id}/dev-payment/confirm" +
            $"?providerRef={session.ProviderRef}");
    }

    [Fact]
    public async Task CreatePayment_issues_a_distinct_ref_per_call()
    {
        var sut = BuildSut();
        var order = BuildOrder();

        var first = await sut.CreatePaymentAsync(order, CancellationToken.None);
        var second = await sut.CreatePaymentAsync(order, CancellationToken.None);

        first.Value!.ProviderRef.Should().NotBe(second.Value!.ProviderRef);
    }

    [Fact]
    public async Task CreatePayment_keeps_an_origin_relative_base_relative()
    {
        // Deployed environments proxy through the frontend origin. Leaving
        // the URL relative is what keeps the confirm hop same-origin
        // regardless of which hostname the tester browsed — and the
        // SameSite=Strict session cookie depends on that.
        var order = BuildOrder();

        var result = await BuildSut("/api-proxy/customer")
            .CreatePaymentAsync(order, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.RedirectUrl.Should().StartWith(
            $"/api-proxy/customer/api/v1/orders/{order.Id}/dev-payment/confirm?providerRef=");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-url")]
    [InlineData("//evil.example/api-proxy/customer")]   // protocol-relative: looks relative, leaves the origin
    [InlineData("javascript:alert(1)")]
    public async Task CreatePayment_fails_as_misconfigured_when_confirm_base_url_is_unusable(string confirmBaseUrl)
    {
        var result = await BuildSut(confirmBaseUrl).CreatePaymentAsync(BuildOrder(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.PaymentProviderMisconfigured);
        result.Error.Type.Should().Be(ErrorType.Configuration);
    }

    [Fact]
    public async Task VerifyPayment_reports_pending_so_the_retry_path_re_serves_the_cached_url()
    {
        var result = await BuildSut().VerifyPaymentAsync("dev-abc", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.State.Should().Be(PaymentState.Pending);
        result.Value.PaidAt.Should().BeNull();
    }

    [Fact]
    public async Task Refund_settles_instantly()
    {
        var result = await BuildSut().RefundAsync("dev-abc", 58_900, "CZK", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.AmountMinor.Should().Be(58_900);
        result.Value.Currency.Should().Be("CZK");
        result.Value.RefundedAt.Should().Be(_clock.UtcNow);
    }

    [Theory]
    [InlineData("dev-abc", true)]
    [InlineData("dev-", true)]
    [InlineData("1234-5678-comgate-transid", false)]
    [InlineData("DEV-abc", false)]           // ordinal — case must match
    [InlineData("xdev-abc", false)]          // prefix, not substring
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsDevProviderRef_only_recognises_its_own_prefix(string? providerRef, bool expected)
    {
        DevPaymentProvider.IsDevProviderRef(providerRef).Should().Be(expected);
    }
}
