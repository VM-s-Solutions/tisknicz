# T-0061 — OrderPricing domain service + PricingService orchestrator

**Phase:** 4 (Orders)
**Size:** M
**State:** `ready`
**Depends on:** T-0010 (`CountryConfiguration`), T-0041 (`Product`), T-0060 (`Order` snapshot fields)
**Owner:** `dotnet-backend`
**ADRs:** 0003 (Money + Currency), 0004 (CountryConfiguration), 0015 (Responsibility-Driven Design)
**Role doc:** [docs/architecture/roles/order-pricing.md](../architecture/roles/order-pricing.md)

## Why now

T-0060 plumbed every monetary field on `Order` and enforced the pricing-math invariants at the entity boundary (`Order.cs:258-271`). T-0061 produces the values that get fed into those fields. Until this lands, T-0063 (`CreateOrder` command) has no way to compute the pricing snapshot from `Product` + `CountryConfiguration`, so the entire Phase 4 ordering pipeline (T-0063 → T-0066 → T-0067 → T-0068) is blocked.

The math is also what the customer-facing checkout preview, the maker payout, the platform-fee invoice (T-0068), and the Comgate session amount (T-0065) all key off — every Phase-4 ticket downstream depends on this rule being right.

## Scope

### Domain layer (new files in `Core.Domain/Orders/`)

- **`PricingBreakdown` record** at `backend/src/Makables.Core.Domain/Orders/PricingBreakdown.cs` — immutable record with seven fields:
  - `Money ProductPrice`
  - `Money ShippingPrice`
  - `Money PlatformFee`
  - `Money MakerPayout`
  - `Money TotalPrice`
  - `Money VatAmount`
  - `int VatRateBp`

  Every `Money` field shares an identical `Currency` (enforced at construction). `VatRateBp == 0` when `InvoicingMode != StandardVat` and `VatAmount` is `Money.Zero(currency)`. Includes a static `Create(...)` factory that validates the currency triangle and the pricing-math invariants (`total == product + shipping`, `makerPayout + platformFee == product + shipping`) so callers cannot construct an inconsistent breakdown.

- **`OrderPricing` static class** at `backend/src/Makables.Core.Domain/Orders/OrderPricing.cs`:
  ```csharp
  internal static class OrderPricing
  {
      public static PricingBreakdown Compute(
          Money productPrice,
          Money shippingPrice,
          CountryConfiguration config);
  }
  ```
  Pure function: no I/O, no DB, no DI. Per the role doc (`order-pricing.md:58-60`) and the user decision logged in the status log. Asserts `productPrice.Currency == shippingPrice.Currency == config.DefaultCurrencyCode` (throws `InvalidOperationException` — programmer error, not user input). Internal accessibility — only callable from `PricingService` and tests (`InternalsVisibleTo` already configured for `Makables.Tests`).

  The formula, frozen per role doc and confirmed by user 2026-06-03:
  ```
  platformFee = productPrice.PercentOfBp(config.PlatformFeeRateBp)
  makerPayout = productPrice.Subtract(platformFee).Add(shippingPrice)
  totalPrice  = productPrice.Add(shippingPrice)
  vatAmount   = config.InvoicingMode == StandardVat
                  ? totalPrice.PercentOfBp(config.StandardVatRateBp)
                  : Money.Zero(productPrice.Currency)
  vatRateBp   = config.InvoicingMode == StandardVat ? config.StandardVatRateBp : 0
  ```

  Rounding is half-up at every `PercentOfBp` boundary (`Money.cs:60-72`); intermediate sums are exact (`order-pricing.md:56`).

### Application layer (new files in `Core.AppServices/Services/`)

- **`IPricingService` interface** at `backend/src/Makables.Core.AppServices/Services/IPricingService.cs`:
  ```csharp
  public interface IPricingService
  {
      Task<BusinessResult<PricingBreakdown>> ComputeForProductAsync(
          string productId,
          ShippingMethod shippingMethod,
          CancellationToken cancellationToken);
  }
  ```

- **`PricingService` impl** at `backend/src/Makables.Core.AppServices/Services/PricingService.cs` (primary constructor DI):
  ```csharp
  public sealed class PricingService(
      IProductRepository products,
      ICountryConfigurationRepository configs) : IPricingService
  ```

  Steps:
  1. Load `Product` via `products.GetByIdAsync(productId, ct)`. If null → `BusinessResult.Failure(Error.NotFound("productId", BusinessErrorMessage.ProductNotFound))`.
  2. Reject `product.PriceType` of `OnRequest` (custom quote flow, not orderable via this path) → `BusinessResult.Failure(Error.Validation("priceType", BusinessErrorMessage.ProductNotOrderable))`.
  3. Load `CountryConfiguration` via `configs.GetByCodeAsync(product.CountryCode, ct)`. If null → `BusinessResult.Failure(Error.NotFound("countryCode", BusinessErrorMessage.CountryConfigurationNotFound))`.
  4. Resolve `shippingPrice`:
     - `ShippingMethod.PersonalPickup` ⇒ `Money.Zero(config.DefaultCurrencyCode)`.
     - `ShippingMethod.ZasilkovnaPickupPoint` ⇒ `new Money(config.DefaultShippingPriceMinor, config.DefaultCurrencyCode)` (see new column below).
  5. Build `productPriceMoney = product.Price` (existing `Money` accessor on `Product`, `Product.cs:60`).
  6. Delegate to `OrderPricing.Compute(productPriceMoney, shippingPrice, config)`.
  7. Wrap in `BusinessResult.Success(breakdown)`.

  Currency mismatch between `product.PriceCurrency` and `config.DefaultCurrencyCode` is a seeded-data integrity issue — throw (per user decision Q7); the seed migration + admin UI in T-0108 must guarantee they agree. Internal monitoring picks up the exception; users never see it.

  Quantity = 1 only at MVP (user decision Q4). The interface intentionally does not take a quantity parameter so the snapshot fields on `Order` stay scalar. Multi-line orders are out of scope; a future ticket will revisit if needed.

### Schema change (`CountryConfiguration`)

Per user decision Q3 — add `DefaultShippingPriceMinor` to `CountryConfiguration` so it's admin-editable and matches the existing `PlatformFeeRateBp` pattern.

- **Migration:** new column `default_shipping_price_minor BIGINT NOT NULL DEFAULT 0`. Seed value for CZ row: `7900` (79 CZK, midpoint of the 69–89 CZK Zásilkovna range per `PROJEKT-VIZE.md:87`).
- **Entity:** add `public long DefaultShippingPriceMinor { get; private set; }` to `CountryConfiguration.cs` with XML doc. Update the static factory + `Update*` admin methods to include the field. Admin update for this field is **T-0108** scope; T-0061 only adds the field + seed + entity-side validation (`>= 0`).
- **Tests:** add unit tests in `CountryConfigurationTests` for the new field + a migration smoke (`EnsureCreated` round-trip via `TestDbHarness`, same pattern as T-0060).
- **`BusinessErrorMessage`:** add `CountryConfigurationNotFound`, `ProductNotOrderable` (Czech i18n catalogue gets matching keys in T-0119; out of scope here per CLAUDE.md "no UI strings without i18n key — but error codes alone are fine").

### DI registration

- **`AddMakablesInfrastructure.cs`:** register `services.AddScoped<IPricingService, PricingService>()` next to `IEmailSendService` (the precedent at `:200`).

### Tests

- **`backend/src/Makables.Tests/Domain/Orders/OrderPricingTests.cs`** (new, ~12 tests):
  - `Compute_with_personal_pickup_yields_zero_shipping_and_full_payout_minus_fee`
  - `Compute_with_zasilkovna_includes_default_shipping_in_total_and_passes_to_maker`
  - `Compute_platform_fee_is_15_percent_of_product_only_not_shipping` — fixture from `PROJEKT-VIZE.md:94-96`: product 500 CZK, shipping 79 CZK → fee 75 CZK, makerPayout 504 CZK, total 579 CZK.
  - `Compute_vat_when_StandardVat_uses_total_price_base` — 21% of 579 CZK = 122 CZK (half-up).
  - `Compute_vat_when_None_returns_zero_vat_and_zero_rate_bp`
  - `Compute_vat_when_ReverseCharge_returns_zero_vat_and_zero_rate_bp` (other modes covered in T-0068; T-0061 just stays `Zero`).
  - `Compute_satisfies_Order_Create_pricing_invariants` — round-trip the breakdown into `Order.Create` and assert it doesn't throw.
  - `Compute_throws_when_product_currency_does_not_match_config` — programmer-error path.
  - `Compute_rounding_half_up_at_PercentOfBp_boundary` — 1% of 1 CZK minor → 1; 50% of 1 minor → 1.
  - `Compute_intermediate_sums_are_exact_no_premature_rounding` — VAT computed on `totalPrice` not on `productPrice + shippingPrice` separately summed after rounding.
  - `PricingBreakdown_Create_rejects_currency_triangle_mismatch` — guards inside the record factory.
  - `PricingBreakdown_Create_rejects_invariant_violations` — `total != product + shipping`, `maker + fee != product + shipping`.

- **`backend/src/Makables.Tests/AppServices/Services/PricingServiceTests.cs`** (new, ~8 tests, NSubstitute over `IProductRepository` + `ICountryConfigurationRepository`):
  - `ComputeForProductAsync_returns_NotFound_when_product_missing`
  - `ComputeForProductAsync_returns_Validation_when_product_priceType_is_OnRequest`
  - `ComputeForProductAsync_returns_NotFound_when_country_configuration_missing`
  - `ComputeForProductAsync_zero_shipping_for_personal_pickup`
  - `ComputeForProductAsync_uses_default_shipping_price_for_zasilkovna`
  - `ComputeForProductAsync_throws_on_currency_mismatch` (programmer-error path; assert `InvalidOperationException`).
  - `ComputeForProductAsync_delegates_math_to_OrderPricing` — happy path returns `BusinessResult.Success` with the breakdown matching the fixture from the domain test.
  - `ComputeForProductAsync_propagates_CancellationToken_to_both_repos` (`Substitute.For` + `Received().Method(Arg.Any<string>(), token)`).

- **`backend/src/Makables.Tests/Domain/Configuration/CountryConfigurationTests.cs`** (extend, 2 tests):
  - `Create_sets_default_shipping_price_minor`
  - `Create_rejects_negative_default_shipping_price_minor`

## Acceptance criteria

- **AC-1** Given the codebase, when the solution builds, then `PricingBreakdown` exists at `backend/src/Makables.Core.Domain/Orders/PricingBreakdown.cs` as a sealed record with the seven fields listed above; all `Money` fields share an identical `Currency`; static `Create(...)` factory validates the currency triangle and pricing-math invariants.
- **AC-2** Given `OrderPricing.Compute(productPrice, shippingPrice, config)` is called with valid inputs, when it runs, then the returned breakdown satisfies `total == product + shipping` and `makerPayout + platformFee == product + shipping` (mirrors `Order.cs:258-271`). Pure: no I/O, no DI.
- **AC-3** Given `productPrice = 500 CZK` and `shippingPrice = 79 CZK` and `config.PlatformFeeRateBp = 1500`, when `OrderPricing.Compute` runs, then `platformFee == 75 CZK`, `makerPayout == 504 CZK`, `totalPrice == 579 CZK`. The fee is **15% of product only**, not 15% of (product + shipping).
- **AC-4** Given `config.InvoicingMode == StandardVat` and `config.StandardVatRateBp = 2100`, when `OrderPricing.Compute` runs on a `totalPrice = 57 900 minor CZK` (579 CZK display), then `vatAmount.AmountMinor == 12 159` (121.59 CZK; 21% of 57 900 = 12 159 exactly — no rounding boundary hit here) and `vatRateBp == 2100`. Storage stays in minor units per ADR 0003 §2; the display-rounded "122 Kč" only happens at `MoneyFormatter`, never at the entity snapshot. Given `InvoicingMode == None | ReverseCharge | StrictFiscalReporting`, then `vatAmount == Money.Zero(currency)` and `vatRateBp == 0`.
- **AC-5** Given `OrderPricing.Compute` is run with any inputs, when rounding occurs, then it uses `Money.PercentOfBp` (half-up via `MidpointRounding.AwayFromZero` per `Money.cs:60-72`). Pin via tests at the 0.5-minor boundary in both positive and negative directions.
- **AC-6** Given `IPricingService.ComputeForProductAsync(productId, shippingMethod, ct)` is called, when the product + config both resolve and currencies agree, then it returns `BusinessResult.Success(PricingBreakdown)`. When the product is missing, returns `BusinessResult.Failure(Error.NotFound("productId", BusinessErrorMessage.ProductNotFound))`. When the product's `PriceType == OnRequest`, returns `BusinessResult.Failure(Error.Validation("priceType", BusinessErrorMessage.ProductNotOrderable))`. When the country config is missing, returns `BusinessResult.Failure(Error.NotFound("countryCode", BusinessErrorMessage.CountryConfigurationNotFound))`.
- **AC-7** Given `IPricingService` is registered as `Scoped` in `AddMakablesInfrastructure.cs`, when the host starts, then the DI smoke test (existing `HostStartupTestBase` pattern) resolves it successfully.
- **AC-8** Given `ShippingMethod == PersonalPickup`, when `PricingService.ComputeForProductAsync` runs, then `breakdown.ShippingPrice == Money.Zero(config.DefaultCurrencyCode)` and `breakdown.MakerPayout == productPrice - platformFee`. Given `ShippingMethod == ZasilkovnaPickupPoint`, then `breakdown.ShippingPrice == new Money(config.DefaultShippingPriceMinor, config.DefaultCurrencyCode)`.
- **AC-9** Given `CountryConfiguration.DefaultShippingPriceMinor` is added as a `long` field, when the migration runs on a clean DB, then the new column `default_shipping_price_minor BIGINT NOT NULL DEFAULT 0` is created and the CZ seed row has `7900` (79 CZK). The entity factory rejects a negative value with `ArgumentException`.
- **AC-10** Given the test suite runs, when complete, then build is clean and total test count exceeds 943 (current `master` baseline 927 + at minimum 16 new tests: ~12 domain + ~8 service + 2 entity). `dotnet build` clean; `dotnet test` clean.

## Out of scope

- Multi-line orders / `quantity > 1` (deferred per user decision Q4).
- Dynamic Zásilkovna tariff lookup by weight + branch (deferred per user decision Q3; current default is the admin-editable single rate).
- Reverse-charge VAT handling on the platform-fee invoice (T-0068 territory; `OrderPricing` returns `vatAmount = Zero` for non-`StandardVat` modes).
- Currency conversion (Money is single-currency; cross-currency arithmetic throws per ADR 0003).
- Frontend pricing preview (T-0063 + T-0099 will call `IPricingService` from the backend; the frontend never computes pricing).
- The Czech i18n catalogue keys for the two new `BusinessErrorMessage` codes — landed alongside the frontend wire-up in T-0063 / T-0119.

## Technical notes

### Why a static class for `OrderPricing`

Per user decision Q5. Pure math, no DI seam needed at this layer (the orchestrator `PricingService` already provides one). Static-class compile-time enforcement of "no state" matches the role-doc descriptor "pure static methods" (`order-pricing.md:60`). `internal` accessibility plus `InternalsVisibleTo` for `Makables.Tests` keeps the surface tight.

### Currency mismatch at the orchestrator entry — throw, not `BusinessResult`

Per user decision Q7. A product whose `PriceCurrency` doesn't match its country's `DefaultCurrencyCode` means seed data is broken (T-0010 + T-0041 invariant violation). The admin UI for `UpdateCountryConfiguration` (T-0108) will block the unsafe change. A `BusinessResult` failure here would mask the bug behind a generic Conflict; throwing surfaces it to the logger immediately. The customer never sees this path because it's unreachable in any sane production state.

### Where `PricingService` lives

`Core.AppServices/Services/` per the role doc (`order-pricing.md:60`). Existing orchestrators (`EmailSendService`, `OneTimeTokenIssuer`) live under `Features/<Entity>/`, but the role doc is explicit about this one. The `Services/` folder is created here for the first time; subsequent orchestrators with cross-feature scope can join it.

### Reuse with the customer-facing price preview

`CreateOrder.Validator` (T-0063) will call `IPricingService.ComputeForProductAsync` to populate the snapshot. The customer-facing checkout preview endpoint (a separate query handler under T-0099, not in scope here) will call the same service. Single source of truth ensures the price the customer sees matches the price the order persists.

### `ProductNotOrderable` short-circuit

`PriceType.OnRequest` is the "ask for a quote" flow. T-0061 rejects it because the conversation/quote pricing path is a separate ticket (post-MVP per `TISKNI_MVP_SPEC.md`). `PriceType.Fixed` and `PriceType.From` both proceed normally; `From` is treated as a starting-from price that the customer pays directly at MVP (per the existing product role; no surcharge logic at T-0061).

## Test plan

Inline above (see Scope > Tests). No separate `docs/test-plans/` file — the test list is small enough to live in the ticket.

## Status log

- 2026-06-03 `draft → ready` by PM. Expanded from INDEX row after T-0060 merged. Four user decisions captured upfront via a research workflow (4 parallel readers + synthesis judge): (Q3) shipping price source — add `CountryConfiguration.DefaultShippingPriceMinor`, admin-editable; (Q4) quantity = 1 only at MVP, no multi-line; (Q1+Q2) fee + VAT bases confirmed canonical — fee 15% of product, VAT 21% on total; (Q5) `OrderPricing` is `internal static`, `PricingService` is the DI seam.
- 2026-06-03 done. `dotnet-backend` agent implemented per ticket; reviewer pass APPROVE with one Medium and two informational Lows. Build clean; 949 tests pass (865 unit + 84 integration; +22 new vs `master` baseline 843 — comfortably over AC-10's "+16 floor").
  - **M-1 (folded in the same commit) — AC-4 wording corrected.** The original AC text said `vatAmount == 122 CZK (21% half-up)` — but 21% of 57 900 minor CZK = 12 159 minor exactly (no rounding boundary), so the stored snapshot is 12 159 minor (121.59 CZK), not 122. The test asserts the right value (`Money.CZK(12159)`); only the AC text was display-rounded incorrectly. Rewrote AC-4 to be minor-unit-precise and to call out that the display-rounded "122 Kč" only happens at `MoneyFormatter`. No code change.
  - **L-1 — AC-5 negative-direction half-up coverage.** Reviewer noted the test pins `1% of 1 minor → 0` (half-down boundary) while AC-5 reads "1% of 1 CZK minor → 1". Both interpretations are correct for what they test; folding the wording is a follow-up doc-only edit not blocking the merge.
  - **L-2 — Belt-and-braces currency check in `PricingBreakdown.Create`.** Intentional and documented in the XML doc on the type. Informational.
  - **L-3 — `InvoicingMode` switch in `OrderPricing.Compute` silently zero-defaults non-`StandardVat` modes.** Intentional per ticket scope (T-0068 revisits the non-StandardVat branches); consider a `default => throw` arm if a future fifth mode lands. Informational.
  - **Four agent deviations** all confirmed sound by reviewer: (1) `InternalsVisibleTo` was not pre-configured (ticket was wrong) — added; (2) `Error.NotFound(field, code)` overload added so the orchestrator can emit `productId` field + canonical `product.notFound` code without the auto-derived form lowercasing the slug; (3) `BusinessErrorMessage` constants live in `Core.Domain/Common/` (ticket wrongly said `Core.AppServices/Common/`); (4) `defaultShippingPriceMinor` parameter slotted next to `platformFeeRateBp` in `CountryConfiguration.Create` (all callers use named args; no breakage).
