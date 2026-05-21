---
role: ShippingCarrier
kind: adapter
status: accepted
---

# ShippingCarrier

## Responsibility

Provide pickup-point selection metadata, create shipments, retrieve labels, and report shipment status. Adapter pattern: one implementation per carrier; selection per country.

## Collaborators

- **Order** (reads: customer contact, address, weight, value)
- **CountryConfiguration** (reads: credentials, sender label)
- **BlobStorage** (writes: cached label PDFs)

## Knows

- How to talk to its specific carrier (Packeta at launch)
- How to map carrier responses to `ShipmentState`
- How to classify errors

## Does NOT know

- Order pricing or fees
- Whether the maker actually shipped (state machine on Order)
- Customer notification (outbox handles)

## Interface

See ADR 0017. Methods:
- `WidgetConfig(locale, countryCode)` → `PickupPointWidgetConfig` (sync; pure)
- `CreateShipmentAsync(Order)` → `Shipment (CarrierRef, TrackingUrl)`
- `GetStatusAsync(carrierRef)` → `ShipmentStatus`
- `GetLabelPdfAsync(carrierRef)` → `Stream`

## Implementations

- **PacketaShippingCarrier** (`Infra.Clients/Packeta/`)
- Future: DPDShippingCarrier, CeskaPostaShippingCarrier, GLSShippingCarrier

## Implementation pointer

Interface: `backend/src/Makables.Core.Domain/Shipping/IShippingCarrier.cs`.

## Related

- ADRs: 0017 (this role's defining ADR)
- Roles: `order`, `country-configuration`, `blob-storage`
