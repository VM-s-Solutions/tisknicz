# Extension points

Every seam where we expect variation. Each has an interface in `Makables.Core.Domain` (or `Core.AppServices.Abstractions`) with one or more adapter implementations in `Makables.Infra.*`. Adding a new country, currency, or provider means **adding an adapter and a `CountryConfiguration` row** — not changing core code.

Patterns reference: [patterns.md §A.15](./patterns.md#a15-provider-adapter-pattern-keyed-services). Registration uses .NET keyed services per [ADR 0008](../adr/0008-dotnet-dependency-injection.md).

## 1. Payment provider
- Interface: `Makables.Core.Domain.Payments.IPaymentProvider`
- Adapter: `Makables.Infra.Clients.Comgate.ComgatePaymentProvider` (CZ)
- Future: `StripePaymentProvider`, `AdyenPaymentProvider`
- Selection: `CountryConfiguration.DefaultPaymentProvider` → `IPaymentProviderFactory.ResolveAsync(countryCode)`

Methods:
- `CreatePaymentAsync(Order order, CancellationToken) → BusinessResult<PaymentSession>`
- `VerifyPaymentAsync(string providerRef, CancellationToken) → BusinessResult<PaymentStatus>`
- `VerifyWebhookAsync(HttpRequest request, CancellationToken) → BusinessResult<WebhookPayload>`

## 2. Shipping carrier
- Interface: `Makables.Core.Domain.Shipping.IShippingCarrier`
- Adapter: `Makables.Infra.Clients.Packeta.PacketaShippingCarrier` (CZ)
- Future: DPD, Česká pošta, GLS, regional carriers
- Selection: `CountryConfiguration.DefaultShippingCarrier`

Methods:
- `PickupPointWidgetConfig(string locale, string countryCode)`
- `CreatePacketAsync(Order order, CancellationToken) → BusinessResult<PacketRef>`
- `GetLabelAsync(string packetRef, CancellationToken) → BusinessResult<byte[]>` (PDF)
- `GetStatusAsync(string packetRef, CancellationToken) → BusinessResult<ShippingStatus>`

## 3. Company registry
- Interface: `Makables.Core.Domain.Registry.ICompanyRegistry`
- Adapter: `Makables.Infra.Clients.Ares.AresCompanyRegistry` (CZ)
- Future: SK FinStat, PL CEIDG, DE Unternehmensregister
- Selection: `CountryConfiguration.DefaultRegistry`

Method: `LookupByRegistrationNumberAsync(string regNumber, CancellationToken) → BusinessResult<CompanyRecord>`

## 4. Tax / VAT regime
- Interface: `Makables.Core.Domain.Tax.ITaxRegime`
- Adapter: `Makables.Infra.Common.Tax.CzTaxRegime`
- Driven by `CountryConfiguration.InvoicingMode` enum: `None | StandardVat | ReverseCharge | StrictFiscalReporting`
- New mode = new branch in `InvoiceService` + new adapter. Existing modes never change.

See [patterns.md §A.13](./patterns.md#a13-enforcement-mode-pattern).

## 5. Address model + geocoding
- Address value object: `Makables.Core.Domain.Addresses.Address` with structured fields (street, house number, city, ZIP, country code)
- Per-country validators in `Core.Domain.Addresses.Validators.<CountryCode>AddressValidator`
- Geocoder interface: `Makables.Core.Domain.Addresses.IAddressGeocoder`
- Adapter: `Makables.Infra.Clients.Mapbox.MapboxAddressGeocoder` (used per Cleansia precedent)
- Method: `GeocodeAsync(Address address, CancellationToken) → BusinessResult<Coordinates>`

## 6. Money & currency
- Value object: `Makables.Core.Domain.Money.Money` (`AmountMinor: long`, `Currency: string`)
- Pure helpers in the type itself: `Add`, `Subtract`, `PercentOfBp`, equality
- Display: `MoneyFormatter` reads locale; CZK display strips haléře (`579 Kč`)
- See [ADR 0003](../adr/0003-money-and-currency.md) and [patterns.md §A.18](./patterns.md#a18-money--long-minor-units-currency-aware)

## 7. Numbering
- Interface: `Makables.Core.Domain.Numbering.IOrderNumberGenerator`, `IInvoiceNumberGenerator`, `IPayoutBatchNumberGenerator`
- Implementations namespaced per country, per concern (orders, invoices, payout batches)
- Format: `M-CZ-YYYYNNNN` (orders), `FV-CZ-YYYYNNNN` (invoices), `VYP-CZ-YYYY-Www` (payout batches)
- Sequence stored in DB tables `numbering_sequence` keyed by `(country_code, scope, year)` — gap-free (legal requirement for CZ invoices)

## 8. Email provider
- Interface: `Makables.Core.Domain.Email.IEmailProvider`
- Adapter: `Makables.Infra.Clients.Resend.ResendEmailProvider`
- Selection: `CountryConfiguration.DefaultEmailProvider`
- Templates: stored as code (React Email or Razor) or as Resend dynamic templates — TBD by Batch 4 ADR

Method: `SendAsync(string templateCode, string to, object data, CancellationToken)`

## 9. Authentication
- Interface: `Makables.Core.Domain.Authentication.IAuthService`
- Implementation: `Makables.Infra.Common.Authentication.AuthService`
- Methods: `RegisterAsync`, `LoginAsync`, `RefreshAsync`, `SendMagicLinkAsync`, `VerifyEmailAsync`, `ResetPasswordAsync`, `ChangePasswordAsync`
- See [patterns.md §A.17](./patterns.md#a17-authentication-custom)

## 10. File storage
- Interface: `Makables.Core.Domain.Storage.IBlobStorageClient`
- Adapter: `Makables.Infra.Azure.Storage.Blobs.AzureBlobStorageClient`
- All file access goes through the .NET backend — no direct browser → storage links
- Containers: `product-images`, `order-attachments`, `invoices`, `maker-documents`
- Public-read on `product-images`; private on the rest (URLs served via authenticated backend endpoints)

## 11. Background jobs
- Hosted in `Makables.Functions` (Azure Functions v4 on Docker)
- Trigger types: timer (cron), queue (Azure Storage Queue)
- Examples (final list in Batch 4 ADR):
  - `GenerateInvoice` — queue-triggered after order paid
  - `GenerateLabel` — queue-triggered after maker ships
  - `AutoDeliverOrders` — timer (daily 08:00)
  - `RetryFailedWebhooks` — timer (every 5 min)
  - `RunWeeklyPayoutBatch` — timer or admin-triggered

## 12. Observability
- OpenTelemetry traces and metrics via `.AddServiceDefaults()` (Aspire pattern)
- Application Insights as the sink in production
- Custom telemetry: request id, user id, country code on every log line
- See Batch 5 NFR ADR

## 13. Dispute resolution
- Entity: `Makables.Core.Domain.Orders.Dispute` — `Auditable` child of `Order` (Q2 lock, refund-dispute bundle, 2026-06-12)
- Shape: `Id`, `OrderId` (FK), `Category` (enum), `Description`, `Source` (enum), `ResolutionNotes` (nullable), `ResolvedAt` (nullable), `ResolutionOutcome` (enum, nullable)
- Order side: opening sets `Order.State = Disputed` and records `Order.PreDisputeState`; resolve restores it — see [patterns.md §A.22](./patterns.md#a22-state-machine-detour-with-restore-disputed--predisputestate)

**Extension surfaces (the enums):**
- `DisputeCategory` — the dispute taxonomy. New categories (e.g. damage-in-transit, quality, non-delivery subtypes) are enum additions + i18n keys; no flow changes.
- `DisputeSource` — `Customer | Maker | Carrier | Admin`. Customer + maker host endpoints exist from v1 (UI later); `Carrier` is fed by the T-0078 carrier-webhook stub wiring. New automated sources (fraud signals, payment-provider chargebacks) are new enum members + new ingress points, same entity.
- `ResolutionOutcome` — each outcome maps to an **outcome handler**, the unit of growth for resolution behavior. Provider-specific refund flows (e.g. a future provider's chargeback API) attach as a new outcome handler delegating to the payment-provider adapter (§A.15) — never as inline logic in ResolveDispute.

**Sanctioned-command dispatch rule:** `ResolveDispute` orchestrates only — it selects the outcome handler for `ResolutionOutcome`, and the handler dispatches the **sanctioned command** for any side effect (e.g. outcome `Refund` dispatches `RefundOrder` / T-0105 via `IMediator`). ResolveDispute never mutates payment state, never writes `refunded_amount_minor`, never transitions to `Refunded` itself. This keeps the T-0107 manual-transition allow-list authoritative: there is exactly one command per privileged transition, and dispute resolution reuses it instead of growing a parallel path.

## Rule for reviewers

If a PR adds code that **branches on country, currency, or provider** outside of `Infra.*` adapter classes, it is violating an extension point. Request changes.

The only legitimate `if (country == "CZ")` is inside a per-country adapter (like `CzAddressValidator`). Even there, prefer pattern-matching on `Country.IsoCode` over magic strings.
