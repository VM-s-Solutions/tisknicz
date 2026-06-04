using Makables.Core.Domain.Configuration;

namespace Makables.Core.Domain.Orders;

/// <summary>
/// Pure pricing math per role/order-pricing.md. The single source of
/// truth for the formula that the customer checkout preview, the maker
/// payout, the platform-fee invoice (T-0068), and the Comgate session
/// amount (T-0065) all key off.
///
/// <para>
/// <b>Why static + internal.</b> Per T-0061 user decision Q5: no DI seam
/// is needed at this layer because <see cref="AppServices"/>'s
/// <c>PricingService</c> already provides one. Static-class
/// compile-time enforcement of "no state" matches the role-doc
/// descriptor "pure static methods". <c>internal</c> accessibility plus
/// <c>InternalsVisibleTo</c> for <c>Makables.Core.AppServices</c> +
/// <c>Makables.Tests</c> keeps the surface tight — controllers + feature
/// handlers go through <c>PricingService</c>, not here.
/// </para>
///
/// <para>
/// <b>Currency mismatch is a programmer error.</b> The orchestrator
/// pre-validates that <see cref="Products.Product.PriceCurrency"/> matches
/// <see cref="CountryConfiguration.DefaultCurrencyCode"/> at the I/O
/// boundary; getting here with mismatched currencies means seed data is
/// broken (T-0010 + T-0041 invariant violation). <see cref="Compute"/>
/// surfaces that with <see cref="InvalidOperationException"/> rather than
/// a <c>BusinessResult</c> so monitoring + logs flag it immediately.
/// </para>
/// </summary>
internal static class OrderPricing
{
    /// <summary>
    /// Compute the full pricing breakdown for a single-line order. Pure:
    /// no I/O, no DI, no DB. Rounding happens at every
    /// <see cref="Money.Money.PercentOfBp"/> boundary (half-up via
    /// <see cref="MidpointRounding.AwayFromZero"/> per <c>Money.cs:60-72</c>);
    /// intermediate sums are exact (no premature rounding).
    ///
    /// <para>
    /// Formula (frozen per role/order-pricing.md + user decisions
    /// Q1/Q2/Q3 logged in T-0061):
    /// <code>
    /// platformFee = productPrice.PercentOfBp(config.PlatformFeeRateBp)   // product only (Q1)
    /// makerPayout = productPrice.Subtract(platformFee).Add(shippingPrice)
    /// totalPrice  = productPrice.Add(shippingPrice)
    /// vatAmount   = config.InvoicingMode == StandardVat
    ///                 ? totalPrice.PercentOfBp(config.StandardVatRateBp) // on total (Q2)
    ///                 : Money.Zero(productPrice.Currency)
    /// vatRateBp   = config.InvoicingMode == StandardVat ? config.StandardVatRateBp : 0
    /// </code>
    /// </para>
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the currencies on the inputs disagree (programmer
    /// error — see class remarks).
    /// </exception>
    public static PricingBreakdown Compute(
        global::Makables.Core.Domain.Money.Money productPrice,
        global::Makables.Core.Domain.Money.Money shippingPrice,
        CountryConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var currency = productPrice.Currency;

        if (!string.Equals(shippingPrice.Currency, currency, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Currency mismatch: shippingPrice is {shippingPrice.Currency}, " +
                $"productPrice is {currency}. Cross-currency pricing is unsupported.");

        if (!string.Equals(config.DefaultCurrencyCode, currency, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Currency mismatch: CountryConfiguration.DefaultCurrencyCode " +
                $"is {config.DefaultCurrencyCode}, productPrice is {currency}. " +
                "Seed data is broken — fix the product or the country config.");

        // Per Q1: platform fee is computed on the product line only, NOT
        // on (product + shipping). Carrier costs are pass-through to the
        // maker.
        var platformFee = productPrice.PercentOfBp(config.PlatformFeeRateBp);
        var makerPayout = productPrice.Subtract(platformFee).Add(shippingPrice);
        var totalPrice = productPrice.Add(shippingPrice);

        // Per Q2: VAT base is totalPrice, NOT productPrice. Only the
        // StandardVat invoicing mode emits a non-zero VAT line at this
        // ticket's scope; other modes (None / ReverseCharge /
        // StrictFiscalReporting) zero out here and the platform-fee
        // invoice flow (T-0068) revisits the reverse-charge case.
        var (vatAmount, vatRateBp) = config.InvoicingMode == InvoicingMode.StandardVat
            ? (totalPrice.PercentOfBp(config.StandardVatRateBp), config.StandardVatRateBp)
            : (global::Makables.Core.Domain.Money.Money.Zero(currency), 0);

        return PricingBreakdown.Create(
            productPrice: productPrice,
            shippingPrice: shippingPrice,
            platformFee: platformFee,
            makerPayout: makerPayout,
            totalPrice: totalPrice,
            vatAmount: vatAmount,
            vatRateBp: vatRateBp);
    }
}
