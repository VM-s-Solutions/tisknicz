using Makables.Core.Domain.Common;
using Makables.Core.Domain.Configuration;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Products;

namespace Makables.Core.AppServices.Services;

/// <summary>
/// Default <see cref="IPricingService"/>. See the interface XML doc for
/// contract details; this file implements the seven-step recipe from
/// T-0061 §Scope.Application.PricingService.
/// </summary>
public sealed class PricingService(
    IProductRepository products,
    ICountryConfigurationRepository configs) : IPricingService
{
    public async Task<BusinessResult<PricingBreakdown>> ComputeForProductAsync(
        string productId,
        ShippingMethod shippingMethod,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productId);

        // === Step 1: load product. ===
        var product = await products.GetByIdAsync(productId, cancellationToken);
        if (product is null)
        {
            return BusinessResult.Failure<PricingBreakdown>(
                Error.NotFound("productId", BusinessErrorMessage.ProductNotFound));
        }

        // === Step 2: refuse OnRequest (custom-quote flow, post-MVP). ===
        // Fixed + From both proceed normally; From is treated as the
        // starting-from price the customer pays directly at MVP — no
        // per-order surcharge logic at T-0061.
        if (product.PriceType == PriceType.OnRequest)
        {
            return BusinessResult.Failure<PricingBreakdown>(
                Error.Validation("priceType", BusinessErrorMessage.ProductNotOrderable));
        }

        // === Step 3: load country config. ===
        var config = await configs.GetByCodeAsync(product.CountryCode, cancellationToken);
        if (config is null)
        {
            return BusinessResult.Failure<PricingBreakdown>(
                Error.NotFound("countryCode", BusinessErrorMessage.CountryConfigurationNotFound));
        }

        // === Step 4: resolve shipping price for the chosen method. ===
        // Per T-0061 user decision Q3: Zásilkovna uses the country-level
        // admin-editable default (admin path in T-0108). Personal pickup
        // is always free at the platform layer.
        var shippingPrice = shippingMethod switch
        {
            ShippingMethod.PersonalPickup =>
                global::Makables.Core.Domain.Money.Money.Zero(config.DefaultCurrencyCode),
            ShippingMethod.ZasilkovnaPickupPoint =>
                new global::Makables.Core.Domain.Money.Money(
                    config.DefaultShippingPriceMinor, config.DefaultCurrencyCode),
            _ => throw new InvalidOperationException(
                $"Unhandled ShippingMethod {shippingMethod}. Extend the switch when a new method ships."),
        };

        // === Step 5: pick up the product price as Money. ===
        var productPrice = product.Price;

        // === Step 6: delegate the math. OrderPricing handles the
        // currency-mismatch programmer-error path (throws
        // InvalidOperationException; unreachable in production but
        // surfaces seed-data corruption in monitoring per Q7).
        var breakdown = OrderPricing.Compute(productPrice, shippingPrice, config);

        // === Step 7: wrap. ===
        return BusinessResult.Success(breakdown);
    }
}
