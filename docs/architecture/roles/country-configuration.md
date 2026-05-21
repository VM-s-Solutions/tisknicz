---
role: CountryConfiguration
kind: aggregate
status: accepted
---

# CountryConfiguration

## Responsibility

Be the control plane for per-country variation. Hold every setting that differs between countries (currency, language, timezone, VAT rates, tax-ID formats, default provider codes, invoicing mode) so domain code consults configuration instead of branching on country.

## Collaborators

- **Country** (1:1; FK to country reference data)
- (Read by) every domain service that varies per country: `OrderPricing`, `InvoiceService`, `PaymentProviderFactory`, `ShippingCarrierFactory`, `CompanyRegistryFactory`, `EmailProviderFactory`, `AuthService` (for default language on emails)

## Knows

- `CountryCode` (FK, also PK)
- `DefaultCurrencyCode`, `DefaultLanguageCode`, `DateFormat`, `TimeZoneId`, `PhonePrefix`
- VAT: `StandardVatRateBp`, `ReducedVatRateBp`, `InvoicingMode`
- Tax IDs: labels and formats (regex) for `TaxId`, `VatId`, `RegistrationNumber`; required flags
- Provider defaults: `DefaultPaymentProvider`, `DefaultShippingCarrier`, `DefaultRegistry`, `DefaultEmailProvider`
- ZIP format regex
- Platform fee rate in basis points (e.g. `1500` = 15%)
- `LegalRequirementsJson` (free-form per-country rules)
- Audit columns

## Does NOT know

- The user, the order, the product — it's purely descriptive of a country, not of any transaction
- Whether a provider code is registered (that's the DI container's concern)
- Whether the country is currently serviced (that flag lives on `Country.IsServiced`)

## Lifecycle

- **Created by:** seed migration on launch (CZ); manual insert by admin for new countries (audited)
- **Modified by:** `UpdateCountryConfiguration.Command` (admin action; audited; sub-commands for VAT rates, provider defaults, invoicing mode)
- **Persisted by:** `ICountryConfigurationRepository`
- **Destroyed by:** never

## Invariants

- Exactly one configuration row per `CountryCode`.
- `DefaultPaymentProvider` must reference a code that is registered as a keyed `IPaymentProvider` service. Validation at write time queries the DI registry (admin UI form rejects unknown codes).
- Same for `DefaultShippingCarrier`, `DefaultRegistry`, `DefaultEmailProvider`.
- VAT rates are non-negative; `ReducedVatRateBp` ≤ `StandardVatRateBp`.
- `PlatformFeeRateBp` ∈ [0, 10000] (0–100%).

## Implementation pointer

`backend/src/Makables.Core.Domain/Configuration/CountryConfiguration.cs`.

## Related

- ADRs: 0003 (money is currency-aware via this config), 0004 (this ADR defined the entity), 0013 (Country.IsServiced separate), 0016–0019 (provider selection)
- Roles: `country`, `order-pricing`, `payment-provider`, `shipping-carrier`, `company-registry`, `email-provider`
