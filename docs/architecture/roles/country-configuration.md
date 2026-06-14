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

## Provider-code validation seam — `IProviderRegistry` (T-0108)

The entity does not know whether a provider code is registered ("that's the DI container's concern"). The write-time invariant ("`Default*Provider` must reference a registered keyed service") is enforced through the `IProviderRegistry` seam (`Core.Domain.Configuration`), which surfaces the registered keys WITHOUT leaking `IServiceProvider` into `Core.AppServices`:

- `GetRegisteredCodes(ProviderKind kind) → IReadOnlySet<string>` — case-insensitive (codes are lowercase constants; admin input matches leniently).
- Impl (`Infra.Database.Configuration.ProviderRegistry`) is built from the composition-root `IServiceCollection` at startup (the runtime `IServiceProvider` cannot enumerate keys). **Payment + shipping** codes are discovered from the keyed `IPaymentProvider` / `IShippingCarrier` service keys (T-0065 / T-0070). **Registry + email** fall back to a static known-codes set (`{ "ares" }` / `{ "sendgrid" }`) until they are keyed at **T-0124**.
  - **Known seam gap (Q-0023):** the static email fallback expects `"sendgrid"`, but the CZ seed sets `DefaultEmailProvider = 'resend'` — an admin changing the email provider today would be rejected as unregistered. Latent (no test exercises it; email isn't keyed until T-0124). T-0124 must reconcile the registry fallback + the seed.

## Admin update command — `UpdateCountryConfiguration` (T-0108 / US-admin-0006)

The single admin mutation of the control-plane row. Atomic, audited (`IAdminAuditableCommand`, before/after row), and guarded on the provider path because a bad write changes VAT math, fee splits, and provider selection for every subsequent order in the country:

- **Unregistered-code rejection (AC-2):** a CHANGED `Default*Provider` must match a registered keyed-service code (via `IProviderRegistry`) — else `country.providerNotRegistered`. Only changed fields are checked (editing VAT alone is not blocked by a pre-existing deprecated code).
- **Retype gate (AC-3, Q-C):** any provider change requires `ConfirmedProviderCode` to equal the new value (payment-first when payment changed) — else `country.providerConfirmationMismatch`.
- **In-flight advisory (Q-C):** in-flight orders keep their cached provider refs; the response carries an advisory `InFlightOrderCount` (WARN, never block).
- **No-op fast path (Q-0021):** all-values-unchanged returns 200 without touching mutators; the shared audit pipeline still writes the benign "attempted" row.

No `SaveChangesAsync` — the UoW pipeline commits the tracked entity + the audit row.

## Implementation pointer

`backend/src/Makables.Core.Domain/Configuration/CountryConfiguration.cs`. Seam: `backend/src/Makables.Core.Domain/Configuration/IProviderRegistry.cs` + `backend/src/Makables.Infra.Database/Configuration/ProviderRegistry.cs`. Command: `backend/src/Makables.Core.AppServices/Features/CountryConfigurations/UpdateCountryConfiguration.cs`.

## Related

- ADRs: 0003 (money is currency-aware via this config), 0004 (this ADR defined the entity), 0013 (Country.IsServiced separate), 0016–0019 (provider selection)
- Stories: US-admin-0006 (admin update country configuration)
- Open questions: Q-0023 (T-0124 provider-registry email-provider mismatch)
- Roles: `country`, `order-pricing`, `payment-provider`, `shipping-carrier`, `company-registry`, `email-provider`, `admin-audit-log-entry`
