---
role: OrderPricing
kind: domain-service
status: accepted
---

# OrderPricing

## Responsibility

Compute the customer total and the maker payout for a given product (or custom order) and shipping method, using the current `CountryConfiguration` for fee rate and VAT treatment. Pure function: no I/O beyond reading the config object passed in.

## Collaborators

- **Product** (asks: base price, currency)
- **CountryConfiguration** (asks: platform fee rate, VAT mode, default shipping price for the chosen carrier)
- **ShippingCarrier** (asks: shipping cost estimate for the chosen pickup point) — optional; for fixed-price products with standard shipping this can be config-driven
- **Money** (uses: arithmetic)

## Knows

- The platform fee rate (15% at launch; comes from config, not hardcoded)
- The VAT mode (`None | StandardVat | ReverseCharge | StrictFiscalReporting`)
- The rounding rule (half-up, half-up via `Money.PercentOfBp`)

## Does NOT know

- Which payment provider will process the payment
- Whether the order will succeed
- How invoices are numbered or rendered
- Promo codes, discounts, referral credits (post-MVP candidates with their own roles)
- The customer's identity beyond what's needed for pricing (i.e. nothing personal)

## Computation

Given inputs `Product`, `quantity`, `shippingMethod`, `CountryConfiguration`:

```
productPrice  = Product.Price.Multiply(quantity)
shippingPrice = ResolveShippingPrice(shippingMethod, CountryConfiguration)  // 0 for personal pickup; default Packeta price for Zásilkovna
platformFee   = productPrice.PercentOfBp(CountryConfiguration.PlatformFeeRateBp)   // 15% = 1500 bp
makerPayout   = productPrice.Subtract(platformFee).Add(shippingPrice)
totalPrice    = productPrice.Add(shippingPrice)
vatAmount     = CountryConfiguration.InvoicingMode is StandardVat
                  ? totalPrice.PercentOfBp(CountryConfiguration.StandardVatRateBp)
                  : Zero
```

Returns a `PricingBreakdown` record with all components. The Order stores the breakdown as a snapshot.

## Invariants

- All money values share the same currency as the product.
- `platformFee + makerPayout - shippingPrice == productPrice` (by construction).
- `totalPrice = productPrice + shippingPrice`.
- All rounding happens at `PercentOfBp` boundaries; intermediate sums are exact.

## Implementation pointer

`backend/src/Makables.Core.Domain/Orders/OrderPricing.cs` (pure static methods) plus `backend/src/Makables.Core.AppServices/Services/PricingService.cs` (orchestrator that fetches config and product).

## Related

- ADRs: 0003 (money), 0004 (CountryConfiguration), 0013 (config drives variation)
- Roles: `product`, `country-configuration`, `money`, `shipping-carrier`
