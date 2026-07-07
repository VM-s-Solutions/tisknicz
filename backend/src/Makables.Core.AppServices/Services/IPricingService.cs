using Makables.Core.Domain.Common;
using Makables.Core.Domain.Orders;

namespace Makables.Core.AppServices.Services;

/// <summary>
/// Orchestrates a one-line order's pricing snapshot per
/// role/order-pricing.md + T-0061. Loads the
/// <see cref="Products.Product"/> + <see cref="Makers.Maker"/> +
/// <see cref="Configuration.CountryConfiguration"/> (the I/O side),
/// resolves the effective platform-fee rate (T-0140:
/// <c>maker.FeeRateOverrideBp ?? config.PlatformFeeRateBp</c>) and the
/// shipping price for the chosen <see cref="ShippingMethod"/>, and
/// delegates the math to the pure <c>OrderPricing.Compute</c> helper.
///
/// <para>
/// <b>Single source of truth.</b> Every Phase-4 consumer goes through
/// this seam — <c>CreateOrder.Validator</c> (T-0063) to populate the
/// snapshot, the customer-facing checkout preview (T-0099) to render the
/// price, the platform-fee invoice (T-0068) and the Comgate session
/// amount (T-0065). One service → one formula → no drift between
/// "what the customer saw" and "what the order persisted".
/// </para>
///
/// <para>
/// <b>Quantity = 1.</b> Per user decision Q4 the interface intentionally
/// does NOT take a quantity parameter so the snapshot fields on
/// <c>Order</c> stay scalar. Multi-line orders are out of scope at MVP.
/// </para>
/// </summary>
public interface IPricingService
{
    /// <summary>
    /// Compute the pricing snapshot for a single-line order on
    /// <paramref name="productId"/> shipped via
    /// <paramref name="shippingMethod"/>.
    ///
    /// <para>Failure modes:</para>
    /// <list type="bullet">
    ///   <item>Product missing → <see cref="ErrorType.NotFound"/>,
    ///   <see cref="BusinessErrorMessage.ProductNotFound"/>.</item>
    ///   <item>Product <see cref="Products.PriceType.OnRequest"/> →
    ///   <see cref="ErrorType.Validation"/>,
    ///   <see cref="BusinessErrorMessage.ProductNotOrderable"/>.</item>
    ///   <item>CountryConfiguration missing →
    ///   <see cref="ErrorType.NotFound"/>,
    ///   <see cref="BusinessErrorMessage.CountryConfigurationNotFound"/>.</item>
    ///   <item>Maker missing (T-0140, resolving the fee-rate override) →
    ///   <see cref="ErrorType.NotFound"/>,
    ///   <see cref="BusinessErrorMessage.MakerNotFound"/>.</item>
    /// </list>
    ///
    /// <para>
    /// A currency mismatch between the product and its country config is
    /// a seeded-data integrity bug — surfaces as
    /// <see cref="InvalidOperationException"/> (user decision Q7), not a
    /// <see cref="BusinessResult"/>. The customer never reaches this path
    /// in any sane production state.
    /// </para>
    /// </summary>
    Task<BusinessResult<PricingBreakdown>> ComputeForProductAsync(
        string productId,
        ShippingMethod shippingMethod,
        CancellationToken cancellationToken);
}
