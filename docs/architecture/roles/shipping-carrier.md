---
role: ShippingCarrier
kind: adapter
status: accepted
---

# ShippingCarrier

## Responsibility

Provide pickup-point selection metadata for the checkout widget, create
shipments at the carrier, fetch shipping-label PDFs, and report shipment
status. Adapter pattern: one implementation per carrier; selection per
country via `CountryConfiguration.DefaultShippingCarrier`.

## Collaborators

- **Order** (reads: customer contact, address, value, country)
- **CountryConfiguration** (reads: which carrier to use for the country)
- **BlobStorage** (writes: cached label PDFs at `invoices/{cc}/orders/{orderId}/label.pdf`)

## Knows

- How to talk to its specific carrier (Packeta v6 REST at launch)
- How to map carrier responses to `ShipmentState`
- How to classify errors (`Transient | Permanent | Configuration | Unknown`)
- The widget script URL + public widget key for the customer checkout

## Does NOT know

- Order pricing or fees
- Whether the maker actually shipped (state machine on Order)
- Customer notification (outbox handles)
- Per-maker carrier accounts (single platform-wide account at MVP per T-0070 §B)

## Interface

See ADR 0017. Methods (`IShippingCarrier`):
- `Code` (property) — carrier discriminator, e.g. `"packeta"`
- `WidgetConfig(locale, countryCode)` → `PickupPointWidgetConfig` (sync; pure data lookup, no I/O)
- `CreateShipmentAsync(Order)` → `Shipment (CarrierRef, TrackingUrl)`
- `GetStatusAsync(carrierRef)` → `ShipmentStatus`
- `GetLabelPdfAsync(carrierRef)` → `Stream` (caller disposes)

## Implementations

- **PacketaShippingCarrier** (`Infra.Clients/Packeta/`) — CZ launch
- Future: DPDShippingCarrier, CeskaPostaShippingCarrier, GLSShippingCarrier

Registered as keyed scoped services. Resolved via
`IShippingCarrierFactory.ResolveAsync(countryCode)` which reads
`CountryConfiguration.DefaultShippingCarrier` and looks up the keyed
`IShippingCarrier`. Country → code lookup cached in `IMemoryCache` with
a 5-minute TTL.

## Invariants

- Adapter never mutates the order. State transitions happen via Mediator
  commands inside the application layer (T-0072 `ShipOrder`).
- Adapter never writes to the database. The application layer persists
  via `Order.Ship(...)` under the UoW pipeline commit.
- Errors classified per ADR 0016 §A.14:
  - 5xx / timeouts → `Transient(ShippingCarrierUnavailable)`
  - 4xx with body keyword `address` → `Permanent(ShippingCarrierAddressIdNotFound)`
  - 4xx with body keyword `weight` → `Permanent(ShippingCarrierInvalidWeight)`
  - 401 / 403 → `Configuration(ShippingCarrierConfigurationError)` + Critical log
- `WidgetConfig` is sync and pure — it reads only adapter options.

## Consumers

- **T-0070 ShippingController** — calls `WidgetConfig` for the public
  `GET /api/v1/public/shipping/widget-config` endpoint (anonymous, IP
  rate-limited at 100/min/IP, `Cache-Control: public, max-age=3600`
  on success).
- **T-0072 ShipOrder.Handler** — calls `CreateShipmentAsync` on Zásilkovna
  orders during the Accepted → Shipped transition. Returns ref + tracking
  URL; the handler stamps both onto the Order via the extended
  `Order.Ship(...)` signature.
- **T-0074 FetchAndStoreShippingLabel.Handler** — calls `GetLabelPdfAsync`
  off the `generate-label` queue, uploads the PDF to blob storage at
  the deterministic path. Idempotent via blob HEAD-check.
- **T-0075 FilesController.GetShippingLabel** — reads the blob; on
  cache-miss falls back to live `GetLabelPdfAsync` + fire-and-forget
  cache-fill. 5xx → 503 + Retry-After; Permanent → 404.
- **T-0078 SyncShipmentStatuses** (future) — calls `GetStatusAsync` to
  transition Shipped → Delivered when the carrier reports a delivered
  signal ahead of `Order.AutoDeliverAt`.

## Implementation pointer

- Interface: `backend/src/Makables.Core.Domain/Shipping/IShippingCarrier.cs`
- Packeta impl: `backend/src/Makables.Infra.Clients/Packeta/`
- Factory: `backend/src/Makables.Infra.Clients/Packeta/ShippingCarrierFactory.cs`
- Options validation: `PacketaOptionsValidator` (startup `ValidateOnStart`)

## Related

- ADRs: 0017 (this role's defining ADR), 0016 (error classification), 0011 (blob path convention)
- Roles: `order`, `country-configuration`, `blob-storage`, `payment-provider` (sibling shape)
- Tickets: T-0070 (seam + factory + widget endpoint), T-0072 (ShipOrder writer),
  T-0073 (HandOverOrder — bypasses carrier), T-0074 (label storage Function),
  T-0075 (label download endpoint)
