---
id: T-0070
title: IShippingCarrier + PacketaShippingCarrier + IShippingCarrierFactory + public widget-config endpoint
status: ready
size: M
owner: dotnet-backend
created: 2026-06-08
updated: 2026-06-08
depends_on: [T-0010]
blocks: [T-0072, T-0074, T-0075, T-0078, T-0084]
user_stories: [US-customer-0010, US-maker-0007]
adrs: [0016, 0017]
phase: 4
manual_steps: [packeta-api-key-secret, packeta-public-widget-key-secret]
security_touching: true
layers: [domain, infra-clients, infra-database, config, web-public, frontend-i18n]
---

# T-0070 — IShippingCarrier + PacketaShippingCarrier + IShippingCarrierFactory + public widget-config endpoint

## Context

Foundation for the entire Phase 4 shipping path. T-0070 ships the IShippingCarrier seam (mirror of T-0065's IPaymentProvider): keyed-services adapter selected per country via `IShippingCarrierFactory`, error-classified per ADR 0016 §A.14, with a single Packeta implementation at MVP. Unblocks 5 downstream tickets — T-0072 (ShipOrder Zásilkovna path), T-0074 (GenerateLabel Function), T-0075 (label download endpoint), T-0078 (SyncShipmentStatuses Function), T-0084 (frontend /objednavka with Packeta widget v6).

The slice is the **non-mutating half** of the shipping integration: the adapter + factory + DI registration + public widget-config endpoint + 1 Order column for tracking URL + 1 CountryConfiguration seed update + supporting value objects + BusinessErrorMessage codes + Czech i18n. **NO state transitions** (those are T-0072 / T-0073). **NO shipment creation in production code paths** (T-0072 is the first caller). **NO label storage** (T-0074 owns the blob upload). **NO status sync** (T-0078 owns the timer).

The integration is **security-touching** because it introduces 2 new Key Vault secrets (`Packeta:ApiKey` private; `Packeta:PublicWidgetKey` public-but-secret-from-spam) and a new public unauthenticated endpoint with per-IP rate limiting.

ADR 0017 (shipping/Packeta) is the source of truth for most architectural choices. This ticket implements ADR 0017 verbatim with 7 additional user-locked decisions captured below.

## Locked design decisions

Captured per `docs/process/deliberation.md`. The user answered 7 blocking AskUserQuestion items before this ticket transitioned to ready. ADR 0017 pre-locked another 7 (interface shape, API-key model, blob container, error-classification, etc.) — those are noted as **ADR-locked** below to keep the implementer's source-of-truth list complete.

### A. User-locked at /feature step 3 (non-negotiable)

1. **`Order.ShippingCarrierTrackingUrl` column added.** New `varchar(500) NULL` column on `orders`. Pre-computed in T-0072 `CreateShipmentAsync` and stored via a new set-once Order method (`Order.RecordShipmentReady(carrierRef, trackingUrl, IClock)` or extending the existing `Order.Ship(...)` signature — implementer judges based on what fits the T-0060 state-machine). **T-0070 ships the schema; T-0072 wires the writer.** Mirrors the `payment_redirect_url` precedent (T-0065). EF migration adds one column. **Rejected:** compute on-demand from template (forces every consumer to know the URL template; Packeta domain pivot would need code deploy).

2. **Widget-config endpoint shape = query params.** `GET /api/v1/public/shipping/widget-config?country=CZ&locale=cs-CZ`. Matches existing public-endpoint patterns. NSwag generates a clean optional-param signature. **Rejected:** path params (forces both segments present; missing locale = 404; harder to default).

3. **Widget-config endpoint cache = 1 hour long-cache.** Response sets `Cache-Control: public, max-age=3600`. Body is static config (public widget key + script URL + country + locale); no secrets leaked. Reduces backend hits on checkout pages by ~99%. Cache invalidates within 1 hour after `PacketaOptions.PublicWidgetKey` rotation (acceptable — key rotation is rare + planned). **Rejected:** no-cache (backend hit on every page load for zero benefit); short 5min cache (middle ground, near-zero benefit since config rarely changes).

4. **Personal-pickup auto-deliver window = same 7 days as Zásilkovna.** Uniform window across shipping methods. Customer has same 7-day grace period to confirm delivery. Matches existing `Order.Ship(autoDeliverWindowDays=7)` constant. **NOTE:** T-0070 only ships the constant choice; T-0073 (personal-pickup ShipOrder) is the actual writer. Locked here to avoid relitigation when T-0073 lands. **Rejected:** 1-day window (risky UX — maker delay before clicking Ship → auto-deliver fires before customer gets item); no auto-deliver (locks completion behind manual customer action; many customers won't click).

5. **Controller location = new `ShippingController` in `Web.Public/Controllers/`.** Dedicated per-domain controller. Future shipping endpoints (e.g., T-0078 admin tracking view) live next to it. Matches existing per-domain organization (`CatalogController`, `ProductImageController`, etc). **Rejected:** extend existing `ConfigController` (mixes shipping with other concerns; the codebase doesn't currently have a `ConfigController` analog for this seam anyway).

6. **Per-IP rate limit on widget-config = 100/min.** Matches existing public-endpoint precedent. With 1-hour cache, customers hit this endpoint ~1–2x per checkout — 100/min is generous. Bots / scrapers blocked. **Rejected:** 60/min (could 429 a legit customer during checkout testing); 300/min (not justified for cached endpoint).

7. **Label blob path = `invoices/{cc}/orders/{orderId}/label.pdf`.** Flat path. Reuses existing `invoices` container per ADR 0017 (one container = one set of access controls = simpler). Matches T-0068b `invoices/{cc}/orders/{id}/{invoiceNumber}.pdf` precedent. One order = one shipping label (rare re-label case overwrites). **NOTE:** T-0070 only locks the path convention; T-0074 is the writer. **Rejected:** nested `shipping/label.pdf` subdir (unnecessary at MVP).

### B. ADR-locked (per ADR 0017, no relitigation)

- **Interface shape = 4 methods.** `IShippingCarrier`:
  - `string Code { get; }` (e.g., `"packeta"`)
  - `PickupPointWidgetConfig WidgetConfig(string locale, string countryCode)` (sync — pure data lookup, no I/O)
  - `Task<BusinessResult<Shipment>> CreateShipmentAsync(Order order, CancellationToken ct)` (writes via Packeta REST)
  - `Task<BusinessResult<ShipmentStatus>> GetStatusAsync(string carrierRef, CancellationToken ct)`
  - `Task<BusinessResult<Stream>> GetLabelPdfAsync(string carrierRef, CancellationToken ct)`
- **`Shipment.CarrierRef` = numeric Packeta id** (e.g., `"123456789"`). Barcode is display-only.
- **`PickupPointWidgetConfig.Options` = `Dictionary<string, string>`** (loose, future-proof for v6→v7).
- **Packeta API-key model = single platform-wide account at MVP.** Per-maker is Phase 5+.
- **`CreateShipmentAsync` accepts full `Order` aggregate** (matches Comgate precedent).
- **Label blob container = `invoices`** (reuses proven authorization).
- **Error classification = ADR §A.14 (Transient/Permanent/Configuration/Unknown).** New `BusinessErrorMessage` codes added under the `ShippingCarrier*` prefix.

### C. PM-absorbed (no user input needed)

- **T-0078 SyncShipmentStatuses polling frequency** stays at the INDEX-line default (6h). Tuning belongs in T-0078's grooming.
- **Packeta API v2 (JSON, modern)** is the assumed protocol. If implementer discovers v2 is sunset or requires a different format than ADR 0017 documented, flag as a deviation.
- **Locale parameter validation** — accept any IETF locale string (`cs-CZ`, `en-US`, `sk-SK`, `de-DE`) and forward to the widget. Packeta's widget falls back internally to English on unknown locales; we don't add an allowlist (cheap defense; v6→v7 may add locales).
- **Sender label** — platform-wide `SenderLabel = "makables-cz"` config. Per-maker customization is Phase 5+.

## Scope

### Domain layer

- **`Core.Domain/Shipping/IShippingCarrier.cs`** — 5-member interface (1 property + 4 methods) per ADR 0017 + locked decision B.
- **`Core.Domain/Shipping/IShippingCarrierFactory.cs`** — `Task<BusinessResult<IShippingCarrier>> ResolveAsync(string countryCode, CancellationToken ct)`. Mirrors `IPaymentProviderFactory` shape from T-0065.
- **`Core.Domain/Shipping/PickupPointWidgetConfig.cs`** — sealed record `(string ScriptUrl, string PublicKey, IReadOnlyDictionary<string, string> Options)`.
- **`Core.Domain/Shipping/Shipment.cs`** — sealed record `(string CarrierRef, string TrackingUrl)`. CarrierRef is the Packeta numeric id; TrackingUrl is the pre-computed customer-facing URL.
- **`Core.Domain/Shipping/ShipmentStatus.cs`** — sealed record `(ShipmentState State, DateTimeOffset? DeliveredAt)`.
- **`Core.Domain/Shipping/ShipmentState.cs`** — enum `Created = 0, InTransit = 1, Delivered = 2, Returned = 3, Failed = 4` (full Packeta state space — T-0078 maps Packeta-specific labels to these values).
- **`Core.Domain/Orders/Order.cs`** — add `string? ShippingCarrierTrackingUrl { get; private set; }` field with XML-doc TODO referencing T-0072 as the writer. Add a brief XML doc explaining set-once semantics + length cap (500 chars, matching the column). **NO setter method in T-0070** — T-0072 adds the state-transition method that writes it (likely extending `Order.Ship(...)` or introducing `Order.RecordShipmentReady(...)`).
- **`Core.Domain/Common/BusinessErrorMessage.cs`** — add 4 codes per ADR 0017 §error-classification:
  - `ShippingCarrierUnavailable = "shipping.carrierUnavailable"` (Transient — Packeta 5xx or timeout)
  - `ShippingCarrierInvalidWeight = "shipping.invalidWeight"` (Permanent — order weight out of range)
  - `ShippingCarrierAddressIdNotFound = "shipping.addressIdNotFound"` (Permanent — pickup point id deprecated)
  - `ShippingCarrierConfigurationError = "shipping.configurationError"` (Configuration — API key invalid, sender label wrong)

### Infrastructure clients

- **`Infra.Clients/Packeta/PacketaShippingCarrier.cs`** — sealed class implementing `IShippingCarrier`. `Code = "packeta"`. Each method wraps an `HttpClient` call (via `IHttpClientFactory.CreateClient("packeta")`) with try/catch for `HttpRequestException` / `TaskCanceledException` per ADR 0016 §A.14 error-classification, returning `BusinessResult.Failure(...)` with the right error type.
  - **WidgetConfig(locale, countryCode)** returns `new PickupPointWidgetConfig(ScriptUrl: options.WidgetScriptUrl, PublicKey: options.PublicWidgetKey, Options: new Dictionary<string, string> { ["country"] = countryCode, ["language"] = locale })`. No I/O.
  - **CreateShipmentAsync(Order, ct)** — POST `{baseUrl}/v6/createPacket` (or equivalent per ADR 0017's documented Packeta REST endpoint) with body: api password (in body, NOT header — Packeta convention), `number = order.OrderNumber`, `name + surname` derived from `order.ContactName`, `email`, `phone`, `addressId = order.ZasilkovnaPickupPointId`, `value = order.TotalAmountMinor / 100m` (decimal CZK), `weight = 1.0` (TODO — Product.Weight is not yet on the entity; weight is platform-default at MVP per ADR 0017 risk register). Response parses `id` + `barcode` + `barcodeText`. Returns `Shipment(CarrierRef: id, TrackingUrl: $"https://tracking.packeta.com/Z{id}")`.
  - **GetStatusAsync(carrierRef, ct)** — POST `/v6/packetStatus`; maps Packeta state strings to `ShipmentState` enum.
  - **GetLabelPdfAsync(carrierRef, ct)** — POST `/v6/packetLabelPdf`; returns the response body Stream (caller disposes).
- **`Infra.Clients/Packeta/PacketaOptions.cs`** — sealed class:
  - `string ApiKey` (Key Vault secret, required).
  - `string PublicWidgetKey` (Key Vault secret — public widget loads it client-side but we don't expose unnecessarily, required).
  - `string SenderLabel = "makables-cz"` (default, required).
  - `string BaseUrl = "https://api.packeta.com"` (default, required).
  - `string WidgetScriptUrl = "https://widget.packeta.com/v6/www/js/library.js"` (default, required).
  - `bool TestMode = false` (default — production posture; dev/test environments set to `true` to hit sandbox).
- **`Infra.Clients/Packeta/PacketaOptionsValidator.cs`** — `IValidateOptions<PacketaOptions>`. Validates: ApiKey non-empty, PublicWidgetKey non-empty, BaseUrl absolute http(s), WidgetScriptUrl absolute https. Registered with `ValidateOnStart()`.
- **`Infra.Clients/Packeta/ShippingCarrierFactory.cs`** — implements `IShippingCarrierFactory`. Constructor primary-ctor DI: `ICountryConfigurationRepository`, `IServiceProvider` (for keyed service resolution), `IMemoryCache`. Resolution flow:
  - Lookup `CountryConfiguration.DefaultShippingCarrier` from `ICountryConfigurationRepository` with `IMemoryCache` (5-min TTL, key `"shipping-carrier:" + countryCode`).
  - If null or empty → return `Error.Configuration(ShippingCarrierConfigurationError)`.
  - Resolve the keyed `IShippingCarrier` via `serviceProvider.GetKeyedService<IShippingCarrier>(carrierCode)`.
  - If not registered → return `Error.Configuration(ShippingCarrierConfigurationError)`.
  - Return `BusinessResult.Success(carrier)`.

### Database layer

- **EF migration `AddOrderShippingCarrierTrackingUrl`** — adds `shipping_carrier_tracking_url VARCHAR(500) NULL` to `orders`. Generated via `dotnet ef migrations add AddOrderShippingCarrierTrackingUrl --project Makables.Infra.Database --startup-project Makables.Web.Customer`.
- **EF migration `SetCzDefaultShippingCarrier`** (combined or separate — implementer judges; one migration is cleaner if both are atomic-with-deploy) — `UPDATE country_configurations SET default_shipping_carrier = 'packeta' WHERE country_code = 'CZ'`.
- **`Infra.Database/Configurations/OrderConfiguration.cs`** — add `builder.Property(o => o.ShippingCarrierTrackingUrl).HasColumnName("shipping_carrier_tracking_url").HasMaxLength(500);`.

### Web.Public host

- **`Web.Public/Controllers/ShippingController.cs`** — new controller. `[Route("api/v{version:apiVersion}/public/shipping")]`. `[AllowAnonymous]` on the action. `[ApiVersion("1.0")]`.
  - `[HttpGet("widget-config")]` action `GetWidgetConfig([FromQuery] string country = "CZ", [FromQuery] string locale = "cs-CZ", CancellationToken ct = default)`.
  - Inside: resolve `IShippingCarrierFactory.ResolveAsync(country, ct)` → `carrier.WidgetConfig(locale, country)` → return `200 OK` with `Cache-Control: public, max-age=3600` header set via `Response.Headers.Append`.
  - On factory failure: return `HandleResult(result)` per `MakablesApiController` mapping.
  - **Rate-limiting**: apply existing partitioned per-IP rate-limit policy at 100/min. Add new policy `shipping-widget-config` to `AddMakablesRateLimiting.cs` if no existing 100/min anonymous policy fits.

### Config / DI

- **`Config/Extensions/AddMakablesClients.cs`** — new Packeta registration block:
  - `services.AddOptions<PacketaOptions>().BindConfiguration("Packeta").ValidateOnStart()`.
  - `services.AddSingleton<IValidateOptions<PacketaOptions>, PacketaOptionsValidator>()`.
  - `services.AddHttpClient("packeta", ...)` with Polly retry pipeline (mirrors Comgate registration in the same file).
  - `services.AddKeyedScoped<IShippingCarrier, PacketaShippingCarrier>("packeta")`.
  - `services.AddScoped<IShippingCarrierFactory, ShippingCarrierFactory>()`.
- **`Config/Extensions/AddMakablesRateLimiting.cs`** — add or reuse a 100/min partitioned per-IP policy for the widget-config endpoint.

### i18n

- **`frontend/src/lib/i18n/cs-CZ.ts`** — add 4 Czech keys for the new BusinessErrorMessage codes. Surface in customer error toast (carrier unavailable) AND admin/log (invalid weight, address id not found, configuration error). Suggested wording:
  - `'shipping.carrierUnavailable': 'Doprava je momentálně nedostupná. Zkuste to prosím za chvíli.'`
  - `'shipping.invalidWeight': 'Hmotnost zásilky překračuje povolený limit. Tým byl informován.'`
  - `'shipping.addressIdNotFound': 'Vybrané výdejní místo již není dostupné. Vyberte prosím jiné.'`
  - `'shipping.configurationError': 'Konfigurace dopravce není správně nastavena. Tým byl informován.'`

### NSwag regen

The new `GET /api/v1/public/shipping/widget-config` endpoint is a public contract change → **NSwag regen REQUIRED in the same PR**. Per pre-commit hook (T-0013), `frontend/src/lib/api-client/` cannot be edited manually — `npm run generate:api` produces the diff.

### Manual deployment steps (NOT PR-open blockers)

1. **`packeta-api-key-secret`** — set `Packeta:ApiKey` in Azure App Configuration (Key Vault reference) before first production deploy. Dev environments use a sandbox key. **Owner:** user. **Blocker:** YES for production deploy; NO for PR merge (the validator surfaces a startup failure if missing).
2. **`packeta-public-widget-key-secret`** — set `Packeta:PublicWidgetKey` similarly. Public-but-secret-from-spam — the widget exposes it client-side, but exposing it in source control gives bots a fixed target. **Owner:** user. **Blocker:** YES for production; NO for PR.

For T-0070's CI build, both secrets ship with placeholders (`"placeholder-packeta-api-key"` / `"placeholder-packeta-public-widget-key"`) in `.github/workflows/ci.yml` env block — same pattern as T-0065 Comgate stubs.

### Tests

- **`Makables.Tests/Domain/Shipping/PickupPointWidgetConfigTests.cs`** (NEW, ~3 tests) — pin the record's immutability + Dictionary readonly view. *Test-first commit per TDD policy — pure logic.*
- **`Makables.Tests/Domain/Shipping/ShipmentTests.cs`** (NEW, ~2 tests) — record invariants. *Test-first.*
- **`Makables.Tests/Infra/Clients/Packeta/PacketaShippingCarrierTests.cs`** (NEW, ~10 tests):
  - WidgetConfig returns config with PublicKey + ScriptUrl + Options dict containing locale + country.
  - CreateShipmentAsync success path: POST body contains apiPassword, addressId, value, fields parsed correctly.
  - CreateShipmentAsync TrackingUrl format: `https://tracking.packeta.com/Z{id}`.
  - CreateShipmentAsync 5xx → Transient(ShippingCarrierUnavailable).
  - CreateShipmentAsync 4xx "address id not found" → Permanent(ShippingCarrierAddressIdNotFound).
  - CreateShipmentAsync 4xx "weight" → Permanent(ShippingCarrierInvalidWeight).
  - CreateShipmentAsync 401/403 → Configuration(ShippingCarrierConfigurationError).
  - CreateShipmentAsync timeout → Transient(ShippingCarrierUnavailable).
  - GetStatusAsync maps known Packeta states to ShipmentState enum.
  - GetLabelPdfAsync returns Stream on success, classifies failures.
- **`Makables.Tests/Infra/Clients/Packeta/ShippingCarrierFactoryTests.cs`** (NEW, ~6 tests):
  - ResolveAsync returns the keyed carrier for valid country (mocked CountryConfiguration with `DefaultShippingCarrier = "packeta"`).
  - ResolveAsync returns Configuration error when DefaultShippingCarrier is null.
  - ResolveAsync returns Configuration error when keyed carrier not registered.
  - ResolveAsync caches the country lookup in IMemoryCache (second call doesn't hit ICountryConfigurationRepository).
  - ResolveAsync invalidates after 5min TTL.
  - ResolveAsync respects CancellationToken.
- **`Makables.Tests/Infra/Clients/Packeta/PacketaOptionsValidatorTests.cs`** (NEW, ~6 tests) — validation cases (empty ApiKey, empty PublicWidgetKey, malformed BaseUrl, malformed WidgetScriptUrl, valid options pass, all-required-blank fails fast).
- **`Makables.IntegrationTests/Shipping/WidgetConfigEndpointTests.cs`** (NEW, 3 tests):
  - GET widget-config returns 200 with correct headers (Cache-Control: public, max-age=3600).
  - GET widget-config respects per-IP rate limit (101st request in 1 min returns 429).
  - GET widget-config with unknown country returns 4xx (configuration error → mapped per MakablesApiController).

### Docs

- **`docs/architecture/roles/shipping-carrier.md`** — promote from stub to full role doc. Mirror `payment-provider.md` shape: Responsibility, Collaborators, Knows, Does NOT know, Interface methods, Invariants, Implementation pointer, Related ADRs/tickets, Defense section per `deliberation.md`.
- **`docs/tickets/INDEX.md`** — flip T-0070 row to `**done**` after PR merge (PM does this).

## Alternatives Considered

- **Option A — Compute tracking URL on-demand from template.** *Rejected per A.1* — forces every consumer (T-0072 outbox payload, T-0078 admin view, T-0086 frontend timeline) to know the URL template. Packeta domain pivot would require a code deploy across all consumers.
- **Option B — Path-param widget-config route.** *Rejected per A.2* — forces both segments present; harder to default missing locale.
- **Option C — No-cache widget-config response.** *Rejected per A.3* — backend hit on every checkout page load for zero benefit (config is static for hours).
- **Option D — 1-day or no auto-deliver for personal pickup.** *Rejected per A.4* — risky UX (maker delay; auto-deliver fires before customer gets item) OR locks completion behind manual customer action.
- **Option E — Extend existing ConfigController.** *Rejected per A.5* — would mix shipping with other concerns; clean per-domain organization is the existing pattern.
- **Option F — 60/min rate limit (Mapbox pattern).** *Rejected per A.6* — could 429 a legit customer hitting checkout multiple times during testing.
- **Option G — Nested `shipping/label.pdf` blob path.** *Rejected per A.7* — unnecessary at MVP; flat `label.pdf` mirrors T-0068b's flat invoice path.
- **Option H — Per-maker Packeta API-key model.** *Rejected per ADR 0017 + locked decision B* — Phase 5+. Each maker would need their own ARES-verified Packeta business account; out of scope for MVP.
- **Option I — Async WidgetConfig method.** *Rejected per ADR 0017 + locked decision B* — pure data lookup, no I/O. Sync simplifies frontend.
- **Option J — Strongly-typed WidgetConfigOptions class.** *Rejected per ADR 0017 + locked decision B* — Packeta widget v6→v7 may add new config keys; loose dict is resilient.

## Out of scope

- **State transitions** (Paid → Accepted; Accepted → Shipped) — T-0071, T-0072, T-0073.
- **Calling CreateShipmentAsync from a production handler** — T-0072 (Zásilkovna ShipOrder).
- **Label storage in blob** — T-0074 (`GenerateLabel` Function).
- **Label download endpoint** for makers — T-0075.
- **Shipment status sync timer** — T-0078.
- **Outbox event for customer shipping notification** — T-0072.
- **Frontend Packeta widget v6 embed + pickup-point selection UI** — T-0084.
- **Per-maker Packeta accounts** — Phase 5+.
- **Product.Weight or Order.Weight field** — at MVP, weight is hard-coded to platform-default (1.0 kg) per ADR 0017 risk register. A future ticket adds the field + validation. T-0070 only includes the placeholder in `CreateShipmentAsync`.
- **Tracking URL writer on Order** — T-0072 owns the setter method (extending `Order.Ship(...)` or adding `Order.RecordShipmentReady(...)`). T-0070 ships only the column + getter.
- **Frontend i18n keys for shipping success** — T-0072 / T-0084 own customer-facing UI strings; T-0070 only ships error-code translations.
- **ReverseCharge / international invoicing parallels** — N/A for shipping.

## Acceptance criteria

- **AC-1** Given the codebase, when it builds, then `IShippingCarrier` interface exists at `Core.Domain/Shipping/IShippingCarrier.cs` with exactly: `Code` property + `WidgetConfig` + `CreateShipmentAsync` + `GetStatusAsync` + `GetLabelPdfAsync` per ADR 0017 + locked decision B (the implementer changes none of these signatures).
- **AC-2** Given `PacketaShippingCarrier`, when `Code` is read, then it equals `"packeta"`.
- **AC-3** Given a CountryConfiguration row with `DefaultShippingCarrier = "packeta"`, when `IShippingCarrierFactory.ResolveAsync("CZ", ct)` is called, then it returns `BusinessResult.Success` with a `PacketaShippingCarrier` instance. Second call within 5 minutes does not hit `ICountryConfigurationRepository` (asserted via mock `Received(1)`).
- **AC-4** Given `CountryConfiguration.DefaultShippingCarrier IS NULL`, when `ResolveAsync` is called, then it returns `BusinessResult.Failure(Error.Configuration(ShippingCarrierConfigurationError))`.
- **AC-5** Given valid PacketaOptions + a sandbox-responding HTTP client, when `WidgetConfig("cs-CZ", "CZ")` is called, then the returned `PickupPointWidgetConfig` has `PublicKey == options.PublicWidgetKey`, `ScriptUrl == options.WidgetScriptUrl`, `Options["language"] == "cs-CZ"`, `Options["country"] == "CZ"`.
- **AC-6** Given Packeta returns HTTP 503 to `CreateShipmentAsync`, when the caller awaits, then the result is `BusinessResult.Failure(Error.Transient(ShippingCarrierUnavailable))`. (Retry budget is the caller's responsibility.)
- **AC-7** Given Packeta returns a 4xx with body containing "address id not found", when the caller awaits, then the result is `BusinessResult.Failure(Error.Permanent(ShippingCarrierAddressIdNotFound))`.
- **AC-8** Given Packeta returns 401, when the caller awaits, then the result is `BusinessResult.Failure(Error.Configuration(ShippingCarrierConfigurationError))`.
- **AC-9** Given `CreateShipmentAsync` succeeds with response `id = "9876543210"`, when the caller awaits, then `Shipment.TrackingUrl == "https://tracking.packeta.com/Z9876543210"`.
- **AC-10** Given the EF migration `AddOrderShippingCarrierTrackingUrl` is applied to an empty postgres:16-alpine container, when `MigrateAsync()` completes, then the `orders` table has the `shipping_carrier_tracking_url VARCHAR(500) NULL` column.
- **AC-11** Given the CZ country_configurations row, when read after migration, then `default_shipping_carrier == "packeta"`.
- **AC-12** Given `GET /api/v1/public/shipping/widget-config?country=CZ&locale=cs-CZ`, when the customer hits the endpoint anonymously, then it returns `200 OK` with body matching the `PickupPointWidgetConfig` shape AND `Cache-Control: public, max-age=3600` header set.
- **AC-13** Given the same endpoint hit 101 times within 60 seconds from the same IP, when the 101st request lands, then it returns `429 Too Many Requests`.
- **AC-14** Build clean. Unit tests: baseline (1178 after T-0069 folds) + ~27 new. Integration tests: baseline (148) + 3 new.
- **AC-15** Consistency script exit 0 (no new T1–T7 violations vs 101-tracked baseline).
- **AC-16** NSwag regen committed in the same PR; `frontend/src/lib/api-client/` has the new endpoint typed.
- **AC-17** Role doc `docs/architecture/roles/shipping-carrier.md` promoted from stub to full per `payment-provider.md` template.

## Technical notes

### Why ShippingCarrierTrackingUrl ships in T-0070 (not T-0072)

The column is a schema decision; the writer is a state-transition decision. Splitting at the schema/behavior seam matches the T-0068a/T-0068b precedent (schema first, writer second). T-0072 has hard dep on T-0070; ordering is irrelevant for atomicity (UoW pipeline commits both row updates in T-0072).

### Why personal-pickup decision is locked here

T-0073 (personal-pickup ShipOrder) doesn't ship until later in the sprint. Locking the auto-deliver window now avoids relitigation. T-0070 only writes the constant choice (7 days); T-0073 wires it.

### Why a single platform-wide Packeta account at MVP

ADR 0017 §"The platform's own Packeta account" pre-locks this. Per-maker accounts require each maker to have an ARES-verified business account with Packeta; out of scope for MVP. The single-tenant key rotation playbook is T-0134 territory.

### Why pre-compute the tracking URL

`Order.PaymentRedirectUrl` is the precedent. Cached URL avoids second Packeta lookup on every email retry (T-0072 outbox) + every customer dashboard view (T-0086) + every admin view (T-0078). Single source of truth on the Order row.

### Why widget-config is a public endpoint with rate limit (not authenticated)

The Packeta widget v6 embeds in the customer checkout BEFORE auth (anonymous checkout flow is supported per US-customer-0010 — sign-up happens at order submit). Endpoint must be `[AllowAnonymous]`. Per-IP rate-limit (100/min) defends against scrapers without blocking legit checkout traffic.

### Why error classification mirrors Comgate exactly

T-0065 established the pattern: 5xx → Transient, malformed body → Permanent, 401/403 → Configuration. ADR 0016 §A.14 mandates it. New `ShippingCarrier*` error codes preserve the same ErrorType discipline so the outbox retry policy (T-0029) handles transient Packeta blips correctly without special-casing.

### Why TestMode default = false (production posture)

A misconfigured production deploy that defaults `TestMode = true` would silently route all shipments to Packeta sandbox. Production-default = false forces explicit override in dev environments instead. The validator catches missing ApiKey at startup; TestMode default is the second line of defense.

## Files touched (expected)

### New
- `backend/src/Makables.Core.Domain/Shipping/IShippingCarrier.cs`
- `backend/src/Makables.Core.Domain/Shipping/IShippingCarrierFactory.cs`
- `backend/src/Makables.Core.Domain/Shipping/PickupPointWidgetConfig.cs`
- `backend/src/Makables.Core.Domain/Shipping/Shipment.cs`
- `backend/src/Makables.Core.Domain/Shipping/ShipmentStatus.cs`
- `backend/src/Makables.Core.Domain/Shipping/ShipmentState.cs`
- `backend/src/Makables.Infra.Clients/Packeta/PacketaShippingCarrier.cs`
- `backend/src/Makables.Infra.Clients/Packeta/PacketaOptions.cs`
- `backend/src/Makables.Infra.Clients/Packeta/PacketaOptionsValidator.cs`
- `backend/src/Makables.Infra.Clients/Packeta/ShippingCarrierFactory.cs`
- `backend/src/Makables.Web.Public/Controllers/ShippingController.cs`
- `backend/src/Makables.Infra.Database/Migrations/<timestamp>_AddOrderShippingCarrierTrackingUrl.cs` (+ Designer)
- `backend/src/Makables.Tests/Domain/Shipping/PickupPointWidgetConfigTests.cs`
- `backend/src/Makables.Tests/Domain/Shipping/ShipmentTests.cs`
- `backend/src/Makables.Tests/Infra/Clients/Packeta/PacketaShippingCarrierTests.cs`
- `backend/src/Makables.Tests/Infra/Clients/Packeta/ShippingCarrierFactoryTests.cs`
- `backend/src/Makables.Tests/Infra/Clients/Packeta/PacketaOptionsValidatorTests.cs`
- `backend/src/Makables.IntegrationTests/Shipping/WidgetConfigEndpointTests.cs`

### Modified
- `backend/src/Makables.Core.Domain/Orders/Order.cs` — add `ShippingCarrierTrackingUrl` property.
- `backend/src/Makables.Core.Domain/Common/BusinessErrorMessage.cs` — 4 new codes.
- `backend/src/Makables.Infra.Database/Configurations/OrderConfiguration.cs` — column mapping.
- `backend/src/Makables.Infra.Database/Migrations/MakablesDbContextModelSnapshot.cs` — auto-regenerated.
- `backend/src/Makables.Config/Extensions/AddMakablesClients.cs` — Packeta registration block.
- `backend/src/Makables.Config/Extensions/AddMakablesRateLimiting.cs` — 100/min anonymous policy if not already present.
- `.github/workflows/ci.yml` — add `Packeta__ApiKey`, `Packeta__PublicWidgetKey`, `Packeta__BaseUrl`, `Packeta__SenderLabel`, `Packeta__WidgetScriptUrl` to spec-parity host env block.
- `frontend/src/lib/i18n/cs-CZ.ts` — 4 new keys.
- `frontend/src/lib/api-client/*` — NSwag-regenerated; committed in the same PR.
- `docs/architecture/roles/shipping-carrier.md` — promote to full role doc.

## Test plan reference

Inline above (see Scope > Tests). No separate `docs/test-plans/T-0070.md`.

## Status log

- 2026-06-08 `draft` by PM. Created from INDEX line; T-0069 merged.
- 2026-06-08 `draft → ready` by PM. User answered 7 blocking AskUserQuestion items per `/feature` workflow step 3 (tracking URL pre-compute + store; query-param route; 1-hour long-cache; personal-pickup 7-day window; new ShippingController; 100/min rate limit; flat label blob path). 7 additional decisions pre-locked by ADR 0017 (interface shape, API-key model, blob container, error-classification, etc.) noted in `## Locked design decisions §B`. 4 PM-absorbed (T-0078 frequency stays at INDEX default; Packeta API v2 assumed; loose locale validation; platform-wide sender). Two `manual_steps` flagged (packeta-api-key-secret, packeta-public-widget-key-secret) — NOT PR-open blockers but production-deploy blockers. **Ready for dotnet-backend.**
