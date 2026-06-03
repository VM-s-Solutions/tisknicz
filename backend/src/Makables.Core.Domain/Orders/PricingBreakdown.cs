namespace Makables.Core.Domain.Orders;

// `Money` lives in `Makables.Core.Domain.Money`; the type's short name
// clashes with the sibling namespace from inside `Orders`, so signatures
// here use the fully-qualified form (same workaround as
// <see cref="Products.Product"/>). T-0061.

/// <summary>
/// Immutable result of <see cref="OrderPricing.Compute"/>: every monetary
/// line that <c>Order.Create</c> (T-0060) needs to capture as its pricing
/// snapshot, plus the VAT rate that applied at the moment of computation.
///
/// <para>
/// <b>Currency triangle.</b> Every <see cref="Money"/> field on this
/// record shares the same <see cref="Money.Currency"/>. The
/// <see cref="Create"/> factory rejects any mismatch — callers cannot
/// construct an inconsistent breakdown.
/// </para>
///
/// <para>
/// <b>Pricing-math invariants.</b> The same two invariants
/// <c>Order.Create</c> enforces hold here too:
/// <list type="number">
///   <item><see cref="TotalPrice"/> == <see cref="ProductPrice"/> + <see cref="ShippingPrice"/></item>
///   <item><see cref="MakerPayout"/> + <see cref="PlatformFee"/> ==
///   <see cref="ProductPrice"/> + <see cref="ShippingPrice"/></item>
/// </list>
/// Catching them here too means a future caller that bypasses
/// <see cref="OrderPricing.Compute"/> and hand-builds a breakdown for a
/// custom-quote flow still can't drift the invariants. Mirrors the
/// belt-and-braces layering on <c>Order</c>.
/// </para>
///
/// <para>
/// <b>VAT.</b> Per role/order-pricing.md + T-0061 user decision Q1+Q2,
/// VAT is 21% of <see cref="TotalPrice"/> when
/// <c>CountryConfiguration.InvoicingMode == StandardVat</c>; otherwise
/// <see cref="VatAmount"/> is <c>Money.Zero(currency)</c> and
/// <see cref="VatRateBp"/> is 0. The platform-fee invoice flow (T-0068)
/// will revisit the non-StandardVat branches; T-0061 just normalizes them
/// to zero.
/// </para>
/// </summary>
public sealed record PricingBreakdown
{
    /// <summary>Product line — what the maker charges for the item.</summary>
    public global::Makables.Core.Domain.Money.Money ProductPrice { get; }

    /// <summary>Shipping line — 0 for personal pickup, country default otherwise.</summary>
    public global::Makables.Core.Domain.Money.Money ShippingPrice { get; }

    /// <summary>Platform commission = <see cref="ProductPrice"/> × fee bp (product only — T-0061 Q1).</summary>
    public global::Makables.Core.Domain.Money.Money PlatformFee { get; }

    /// <summary>Maker net = <see cref="ProductPrice"/> − <see cref="PlatformFee"/> + <see cref="ShippingPrice"/>.</summary>
    public global::Makables.Core.Domain.Money.Money MakerPayout { get; }

    /// <summary>What the customer is charged = <see cref="ProductPrice"/> + <see cref="ShippingPrice"/>.</summary>
    public global::Makables.Core.Domain.Money.Money TotalPrice { get; }

    /// <summary>
    /// VAT due — <c>Zero(currency)</c> outside <c>InvoicingMode.StandardVat</c>.
    /// Computed on <see cref="TotalPrice"/> per T-0061 Q2.
    /// </summary>
    public global::Makables.Core.Domain.Money.Money VatAmount { get; }

    /// <summary>
    /// The VAT rate that applied at the moment of computation, basis
    /// points (2100 = 21%). Snapshot — written into <c>Order.VatRateBp</c>
    /// so an invoice rendered months later still reconciles.
    /// </summary>
    public int VatRateBp { get; }

    private PricingBreakdown(
        global::Makables.Core.Domain.Money.Money productPrice,
        global::Makables.Core.Domain.Money.Money shippingPrice,
        global::Makables.Core.Domain.Money.Money platformFee,
        global::Makables.Core.Domain.Money.Money makerPayout,
        global::Makables.Core.Domain.Money.Money totalPrice,
        global::Makables.Core.Domain.Money.Money vatAmount,
        int vatRateBp)
    {
        ProductPrice = productPrice;
        ShippingPrice = shippingPrice;
        PlatformFee = platformFee;
        MakerPayout = makerPayout;
        TotalPrice = totalPrice;
        VatAmount = vatAmount;
        VatRateBp = vatRateBp;
    }

    /// <summary>
    /// Validate the currency triangle + pricing-math invariants and return
    /// a sealed breakdown. Throws <see cref="ArgumentException"/> for any
    /// violation (programmer error — these are derived values, not user
    /// input).
    /// </summary>
    public static PricingBreakdown Create(
        global::Makables.Core.Domain.Money.Money productPrice,
        global::Makables.Core.Domain.Money.Money shippingPrice,
        global::Makables.Core.Domain.Money.Money platformFee,
        global::Makables.Core.Domain.Money.Money makerPayout,
        global::Makables.Core.Domain.Money.Money totalPrice,
        global::Makables.Core.Domain.Money.Money vatAmount,
        int vatRateBp)
    {
        // Currency triangle — every Money field must share the same code.
        // We cherry-pick `productPrice.Currency` as the anchor; the others
        // must match it.
        var anchor = productPrice.Currency;
        AssertCurrency(shippingPrice, anchor, nameof(shippingPrice));
        AssertCurrency(platformFee, anchor, nameof(platformFee));
        AssertCurrency(makerPayout, anchor, nameof(makerPayout));
        AssertCurrency(totalPrice, anchor, nameof(totalPrice));
        AssertCurrency(vatAmount, anchor, nameof(vatAmount));

        if (vatRateBp < 0)
            throw new ArgumentException("VatRateBp cannot be negative.", nameof(vatRateBp));

        // Pricing-math invariants — match Order.Create (Order.cs:258-271)
        // so a breakdown that round-trips into Order.Create never throws.
        var productPlusShipping = checked(productPrice.AmountMinor + shippingPrice.AmountMinor);
        if (totalPrice.AmountMinor != productPlusShipping)
            throw new ArgumentException(
                $"TotalPrice ({totalPrice.AmountMinor}) must equal ProductPrice ({productPrice.AmountMinor}) + ShippingPrice ({shippingPrice.AmountMinor}).",
                nameof(totalPrice));

        var feePlusPayout = checked(platformFee.AmountMinor + makerPayout.AmountMinor);
        if (feePlusPayout != productPlusShipping)
            throw new ArgumentException(
                $"PlatformFee ({platformFee.AmountMinor}) + MakerPayout ({makerPayout.AmountMinor}) must equal " +
                $"ProductPrice ({productPrice.AmountMinor}) + ShippingPrice ({shippingPrice.AmountMinor}).",
                nameof(makerPayout));

        return new PricingBreakdown(
            productPrice, shippingPrice, platformFee, makerPayout, totalPrice, vatAmount, vatRateBp);
    }

    private static void AssertCurrency(
        global::Makables.Core.Domain.Money.Money value,
        string anchor,
        string paramName)
    {
        if (!string.Equals(value.Currency, anchor, StringComparison.Ordinal))
            throw new ArgumentException(
                $"Currency mismatch: {paramName} is {value.Currency}, expected {anchor}.",
                paramName);
    }
}
