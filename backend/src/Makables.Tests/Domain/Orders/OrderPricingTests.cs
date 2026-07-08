using FluentAssertions;
using Makables.Core.Domain.Configuration;
using Makables.Core.Domain.Money;
using Makables.Core.Domain.Orders;

namespace Makables.Tests.Domain.Orders;

/// <summary>
/// Pins the formula in <see cref="OrderPricing.Compute"/> per T-0061
/// role/order-pricing.md and the user decisions logged in the ticket
/// status log:
/// <list type="bullet">
///   <item>Q1: platform fee = 15% of product only (not shipping)</item>
///   <item>Q2: VAT = 21% of <see cref="PricingBreakdown.TotalPrice"/> when StandardVat, else zero</item>
///   <item>Q3: Zásilkovna shipping = <see cref="CountryConfiguration.DefaultShippingPriceMinor"/></item>
/// </list>
///
/// The breakdown also has to round-trip through <see cref="Order.Create"/>
/// without throwing — same pricing-math invariants on both sides.
/// </summary>
public class OrderPricingTests
{
    // === Fixtures ===

    private static CountryConfiguration BuildCz(
        InvoicingMode invoicingMode = InvoicingMode.StandardVat,
        int standardVatBp = 2100,
        int platformFeeRateBp = 1500,
        long defaultShippingPriceMinor = 7900) =>
        CountryConfiguration.Create(
            countryId: "CZ",
            defaultCurrencyCode: "CZK",
            defaultLanguageCode: "cs-CZ",
            timeZoneId: "Europe/Prague",
            phonePrefix: "+420",
            dateFormat: "d. M. yyyy",
            standardVatRateBp: standardVatBp,
            taxIdLabel: "DIČ",
            vatIdLabel: "DIČ",
            registrationNumberLabel: "IČO",
            defaultPaymentProvider: "comgate",
            defaultShippingCarrier: "packeta",
            defaultRegistry: "ares",
            defaultEmailProvider: "resend",
            issuerName: "JVM YORE s.r.o.",
            issuerIco: "00000000",
            invoicingMode: invoicingMode,
            platformFeeRateBp: platformFeeRateBp,
            defaultShippingPriceMinor: defaultShippingPriceMinor);

    // === Tests ===

    [Fact]
    public void Compute_with_personal_pickup_yields_zero_shipping_and_full_payout_minus_fee()
    {
        // Product 500 CZK, shipping 0 CZK (personal pickup), fee 15% → 75 CZK
        // maker = 500 − 75 + 0 = 425 CZK; total = 500 CZK.
        var product = Money.CZK(50000);
        var shipping = Money.Zero("CZK");
        var cfg = BuildCz();

        var b = OrderPricing.Compute(product, shipping, cfg, cfg.PlatformFeeRateBp);

        b.ProductPrice.Should().Be(Money.CZK(50000));
        b.ShippingPrice.Should().Be(Money.Zero("CZK"));
        b.PlatformFee.Should().Be(Money.CZK(7500));
        b.MakerPayout.Should().Be(Money.CZK(42500));
        b.TotalPrice.Should().Be(Money.CZK(50000));
    }

    [Fact]
    public void Compute_with_zasilkovna_includes_default_shipping_in_total_and_passes_to_maker()
    {
        // Product 500 CZK, shipping 79 CZK (Zásilkovna default), fee 15% of
        // product = 75 CZK → maker = 500 − 75 + 79 = 504 CZK; total = 579 CZK.
        var product = Money.CZK(50000);
        var shipping = Money.CZK(7900);
        var cfg = BuildCz();

        var b = OrderPricing.Compute(product, shipping, cfg, cfg.PlatformFeeRateBp);

        b.ShippingPrice.Should().Be(Money.CZK(7900));
        b.TotalPrice.Should().Be(Money.CZK(57900));
        // Shipping is pass-through to the maker (Q1 says fee is product-only).
        b.MakerPayout.Should().Be(Money.CZK(50400));
    }

    [Fact]
    public void Compute_platform_fee_is_15_percent_of_product_only_not_shipping()
    {
        // PROJEKT-VIZE.md:94-96 fixture: 500 CZK + 79 CZK → fee 75 CZK,
        // payout 504 CZK, total 579 CZK. If the fee were applied to
        // (product + shipping) we'd get 15% × 579 = 86.85 → 87 CZK; the
        // assertion guarantees we DON'T do that.
        var product = Money.CZK(50000);
        var shipping = Money.CZK(7900);
        var cfg = BuildCz();

        var b = OrderPricing.Compute(product, shipping, cfg, cfg.PlatformFeeRateBp);

        b.PlatformFee.Should().Be(Money.CZK(7500));
        b.MakerPayout.Should().Be(Money.CZK(50400));
        b.TotalPrice.Should().Be(Money.CZK(57900));
    }

    [Fact]
    public void Compute_vat_when_StandardVat_uses_total_price_base()
    {
        // 21% of 579 CZK = 121.59 CZK → 122 CZK (half-up via
        // MidpointRounding.AwayFromZero in Money.PercentOfBp).
        var product = Money.CZK(50000);
        var shipping = Money.CZK(7900);
        var cfg = BuildCz(invoicingMode: InvoicingMode.StandardVat, standardVatBp: 2100);

        var b = OrderPricing.Compute(product, shipping, cfg, cfg.PlatformFeeRateBp);

        b.VatAmount.Should().Be(Money.CZK(12159));
        b.VatRateBp.Should().Be(2100);
    }

    [Fact]
    public void Compute_vat_when_None_returns_zero_vat_and_zero_rate_bp()
    {
        var product = Money.CZK(50000);
        var shipping = Money.CZK(7900);
        var cfg = BuildCz(invoicingMode: InvoicingMode.None);

        var b = OrderPricing.Compute(product, shipping, cfg, cfg.PlatformFeeRateBp);

        b.VatAmount.Should().Be(Money.Zero("CZK"));
        b.VatRateBp.Should().Be(0);
    }

    [Fact]
    public void Compute_vat_when_ReverseCharge_returns_zero_vat_and_zero_rate_bp()
    {
        // T-0068 will revisit reverse-charge; T-0061 just stays at zero.
        var product = Money.CZK(50000);
        var shipping = Money.CZK(7900);
        var cfg = BuildCz(invoicingMode: InvoicingMode.ReverseCharge);

        var b = OrderPricing.Compute(product, shipping, cfg, cfg.PlatformFeeRateBp);

        b.VatAmount.Should().Be(Money.Zero("CZK"));
        b.VatRateBp.Should().Be(0);
    }

    [Fact]
    public void Compute_satisfies_Order_Create_pricing_invariants()
    {
        // Round-trip: feed the breakdown into Order.Create and assert it
        // doesn't throw. If OrderPricing ever drifts from Order.Create's
        // invariants this test fires before the bug ships.
        var product = Money.CZK(50000);
        var shipping = Money.CZK(7900);
        var cfg = BuildCz();

        var b = OrderPricing.Compute(product, shipping, cfg, cfg.PlatformFeeRateBp);

        var act = () => Order.Create(
            id: "ord-roundtrip",
            orderNumber: "CZ-RT-1",
            customerUserId: "user-1",
            makerId: "maker-1",
            productId: "prod-1",
            contactName: "Jan Novák",
            contactEmail: "jan@example.cz",
            contactPhone: "+420777123456",
            productPriceAmountMinor: b.ProductPrice.AmountMinor,
            shippingPriceAmountMinor: b.ShippingPrice.AmountMinor,
            platformFeeAmountMinor: b.PlatformFee.AmountMinor,
            makerPayoutAmountMinor: b.MakerPayout.AmountMinor,
            totalAmountMinor: b.TotalPrice.AmountMinor,
            currency: b.ProductPrice.Currency,
            vatRateBp: b.VatRateBp,
            shippingMethod: ShippingMethod.ZasilkovnaPickupPoint,
            zasilkovnaPickupPointId: "branch-79",
            countryCode: "CZ");

        act.Should().NotThrow();
    }

    [Fact]
    public void Compute_throws_when_product_currency_does_not_match_config()
    {
        // Seeded-data integrity bug → InvalidOperationException (Q7), NOT
        // a typed BusinessResult. Customer never reaches this path.
        var product = new Money(50000, "EUR");
        var shipping = Money.Zero("CZK");
        var cfg = BuildCz();

        var act = () => OrderPricing.Compute(product, shipping, cfg, cfg.PlatformFeeRateBp);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Compute_rounding_half_up_at_PercentOfBp_boundary()
    {
        // 1% of 1 minor → 0.01 → rounds half-away-from-zero to 0 (below the
        // 0.5 boundary). We assert the 50% case below — 0.5 → 1.
        // The Money helper rounds with MidpointRounding.AwayFromZero per
        // Money.cs:60-72; "half up" in the positive direction == AwayFromZero.
        var oneMinor = new Money(1, "CZK");

        oneMinor.PercentOfBp(100).Should().Be(Money.Zero("CZK"));   // 1% of 1 = 0.01 → 0
        oneMinor.PercentOfBp(5000).Should().Be(new Money(1, "CZK")); // 50% of 1 = 0.5 → 1 (half-up)

        // And in the negative direction: −1 × 50% = −0.5 → −1 (away from zero).
        var negOneMinor = new Money(-1, "CZK");
        negOneMinor.PercentOfBp(5000).Should().Be(new Money(-1, "CZK"));
    }

    [Fact]
    public void Compute_intermediate_sums_are_exact_no_premature_rounding()
    {
        // VAT must be computed on totalPrice, NOT on (productPrice rounded +
        // shippingPrice rounded) summed after rounding. Construct a pair
        // where the two strategies differ:
        //   product = 99 minor, shipping = 1 minor, total = 100 minor.
        //   VAT @ 21% of 100 = 21 minor (exact).
        //   VAT @ 21% of 99 + 21% of 1 = 20.79 + 0.21 → rounded:
        //     PercentOfBp(99, 2100) = 20.79 → 21 (half-up away)
        //     PercentOfBp(1,  2100) = 0.21  → 0
        //     Sum = 21 minor.
        // To get a real difference, use 50/50 split at the 0.5-minor edge:
        //   product 1 minor, shipping 1 minor, total 2 minor.
        //   VAT @ 50% of 2 = 1 minor (correct, on total).
        //   VAT @ 50% of 1 + 50% of 1 = 1 + 1 = 2 minor (premature rounding).
        // OrderPricing must produce 1 minor — the correct, on-total result.
        var product = new Money(1, "CZK");
        var shipping = new Money(1, "CZK");
        var cfg = BuildCz(
            invoicingMode: InvoicingMode.StandardVat,
            standardVatBp: 5000,
            platformFeeRateBp: 0);

        var b = OrderPricing.Compute(product, shipping, cfg, cfg.PlatformFeeRateBp);

        b.TotalPrice.Should().Be(new Money(2, "CZK"));
        b.VatAmount.Should().Be(new Money(1, "CZK"));
    }

    [Fact]
    public void Compute_uses_caller_resolved_rate_not_config_PlatformFeeRateBp()
    {
        // T-0140: Compute must use the explicit platformFeeRateBp argument,
        // NOT config.PlatformFeeRateBp, so a per-maker loyalty override
        // (resolved by the caller) actually takes effect. Config still
        // carries its own 1500 bp default; we pass 350 bp explicitly and
        // assert the fee reflects 350 bp, not 1500 bp.
        var product = Money.CZK(50000);
        var shipping = Money.Zero("CZK");
        var cfg = BuildCz(platformFeeRateBp: 1500);

        var b = OrderPricing.Compute(product, shipping, cfg, platformFeeRateBp: 350);

        b.PlatformFee.Should().Be(Money.CZK(1750)); // 3,5% of 500 CZK = 17,50 CZK
        b.MakerPayout.Should().Be(Money.CZK(48250)); // 500 − 17,50 = 482,50 CZK
    }

    [Fact]
    public void PricingBreakdown_Create_rejects_currency_triangle_mismatch()
    {
        // Same anchor (CZK) but a stray EUR in the platformFee line → reject.
        var act = () => PricingBreakdown.Create(
            productPrice: Money.CZK(50000),
            shippingPrice: Money.CZK(7900),
            platformFee: new Money(7500, "EUR"),
            makerPayout: Money.CZK(50400),
            totalPrice: Money.CZK(57900),
            vatAmount: Money.Zero("CZK"),
            vatRateBp: 0);

        act.Should().Throw<ArgumentException>()
            .Which.ParamName.Should().Be("platformFee");
    }

    [Fact]
    public void PricingBreakdown_Create_rejects_invariant_violations()
    {
        // total != product + shipping
        var actTotal = () => PricingBreakdown.Create(
            productPrice: Money.CZK(50000),
            shippingPrice: Money.CZK(7900),
            platformFee: Money.CZK(7500),
            makerPayout: Money.CZK(50400),
            totalPrice: Money.CZK(99999),                       // wrong
            vatAmount: Money.Zero("CZK"),
            vatRateBp: 0);
        actTotal.Should().Throw<ArgumentException>()
            .Which.ParamName.Should().Be("totalPrice");

        // maker + fee != product + shipping
        var actSplit = () => PricingBreakdown.Create(
            productPrice: Money.CZK(50000),
            shippingPrice: Money.CZK(7900),
            platformFee: Money.CZK(7500),
            makerPayout: Money.CZK(11111),                      // wrong split
            totalPrice: Money.CZK(57900),
            vatAmount: Money.Zero("CZK"),
            vatRateBp: 0);
        actSplit.Should().Throw<ArgumentException>()
            .Which.ParamName.Should().Be("makerPayout");
    }
}
