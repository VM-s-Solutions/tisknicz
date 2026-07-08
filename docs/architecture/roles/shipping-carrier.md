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
- `CreateReturnShipmentAsync(Order, ReturnRecipient)` → `Shipment (CarrierRef, TrackingUrl)` (T-0146 — the reverse leg, customer → maker; see below)
- `GetStatusAsync(carrierRef)` → `ShipmentStatus`
- `GetLabelPdfAsync(carrierRef)` → `Stream` (caller disposes) — shared by both the forward AND reverse label; the caller supplies whichever `carrierRef` it holds

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
- **T-0146 GenerateReturnLabel.Handler** (admin-triggered, category-gated
  to `DamagedItem`/`NotAsDescribed`) — calls `CreateReturnShipmentAsync`
  with the customer's order + the maker's resolved `ReturnRecipient`
  (registered address; makers aren't Zásilkovna box holders, so this is a
  door-delivery address, not a pickup-point id). Stamps
  `Dispute.ReturnCarrierRef`/`ReturnTrackingUrl`, then enqueues the
  shared `generate-label` queue event (discriminated by event type) for
  `FetchAndStoreReturnLabel.Handler` to cache the PDF at
  `invoices/{cc}/disputes/{disputeId}/return-label.pdf` — same
  cache→carrier→cache-fill shape as T-0074/T-0075, just dispute-scoped.
  The customer-host `FilesController.GetReturnLabel` mirrors T-0075's
  cache-hit/miss download exactly.

## Reverse-shipment recipient shape (T-0146)

`ReturnRecipient` (`Core.Domain/Shipping/ReturnRecipient.cs`) decouples
the adapter from the `Makers`/`Addresses` aggregates — the caller resolves
`Name`/`Email`/`Phone`/`Street`/`HouseNumber`/`City`/`Zip`/`CountryCodeIso`
from the maker + its registered address and hands the adapter a flat
record, same "adapter reads only what it's given" discipline as
`Payments.IPaymentProvider`. Packeta's `createPacket` call is identical to
the forward path with sender/recipient swapped: the customer is the
conceptual sender (they physically drop the parcel off), the recipient
fields address the maker directly (door delivery, not a pickup point).
No itemized reverse-leg price from Packeta at MVP — the cost basis for
the `PayoutDeduction` (see `dispute.md` §Reverse shipment) is
`CountryConfiguration.DefaultShippingPriceMinor`.

## Implementation pointer

- Interface: `backend/src/Makables.Core.Domain/Shipping/IShippingCarrier.cs`
- Recipient shape: `backend/src/Makables.Core.Domain/Shipping/ReturnRecipient.cs` (T-0146)
- Packeta impl: `backend/src/Makables.Infra.Clients/Packeta/`
- Factory: `backend/src/Makables.Infra.Clients/Packeta/ShippingCarrierFactory.cs`
- Options validation: `PacketaOptionsValidator` (startup `ValidateOnStart`)

## Related

- ADRs: 0017 (this role's defining ADR), 0016 (error classification), 0011 (blob path convention)
- Roles: `order`, `country-configuration`, `blob-storage`, `payment-provider` (sibling shape), `dispute` (reverse-shipment consumer)
- Tickets: T-0070 (seam + factory + widget endpoint), T-0072 (ShipOrder writer),
  T-0073 (HandOverOrder — bypasses carrier), T-0074 (label storage Function),
  T-0075 (label download endpoint), T-0146 (reverse leg + return-label cache)
