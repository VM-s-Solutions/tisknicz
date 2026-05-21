---
id: 0017
title: Shipping — Packeta (Zásilkovna) as the launch carrier; ShippingCarrier role; pickup-point widget; label PDFs streamed via backend
status: accepted
date: 2026-05-21
deciders: [Architect]
---

# 0017 — Shipping (Packeta / Zásilkovna)

## Context

Czech market expectation is Zásilkovna pickup points. Packeta (their parent company) exposes both a widget for pickup-point selection (frontend) and a REST API for packet creation + label retrieval (backend). The platform's own Packeta account ships everything — makers don't need their own.

The `ShippingCarrier` role from `docs/architecture/extension-points.md` needs its first implementation. The widget integration is the only frontend code that talks to a third party directly, but it talks to Packeta's CDN-hosted JS, not to our own backend's responsibility.

## Decision

### Role: ShippingCarrier

`docs/architecture/roles/shipping-carrier.md` (adapter role):

**Responsibility:** Provide pickup-point selection metadata, create shipments, retrieve labels, and report shipment status.

**Collaborators:**
- `Order` (read: customer name/email/phone, shipping address, weight, value)
- `CountryConfiguration` (read: provider credentials, sender label)
- `BlobStorage` (write: cache label PDFs)

**Does NOT know:**
- Order pricing or fees
- Whether the maker has actually shipped (state machine lives on Order)
- Customer notification (outbox handles that)

### Interface

```csharp
// Core.Domain/Shipping/IShippingCarrier.cs
public interface IShippingCarrier
{
    string Code { get; }   // "packeta", "dpd", "ceska-posta", ...

    /// Configuration the frontend needs to render the pickup-point widget.
    /// Returned as an opaque DTO; frontend renders whatever the carrier requires.
    PickupPointWidgetConfig WidgetConfig(string locale, string countryCode);

    Task<BusinessResult<Shipment>> CreateShipmentAsync(Order order, CancellationToken ct);

    Task<BusinessResult<ShipmentStatus>> GetStatusAsync(string carrierRef, CancellationToken ct);

    Task<BusinessResult<Stream>> GetLabelPdfAsync(string carrierRef, CancellationToken ct);
}

public record PickupPointWidgetConfig(string ScriptUrl, string PublicKey, IReadOnlyDictionary<string, string> Options);
public record Shipment(string CarrierRef, string TrackingUrl);
public record ShipmentStatus(ShipmentState State, DateTimeOffset? DeliveredAt);
public enum ShipmentState { Created, InTransit, Delivered, Returned, Failed }
```

### Packeta adapter

Lives in `Makables.Infra.Clients/Packeta/PacketaShippingCarrier.cs`.

**Configuration:**
- `Packeta:ApiKey` — REST API password (secret)
- `Packeta:PublicWidgetKey` — frontend widget public key (not secret but distinct from API key)
- `Packeta:SenderLabel` — registered e-shop identifier ("makables-cz")
- `Packeta:TestMode` — toggles sandbox endpoint

### Pickup-point widget (frontend)

The frontend calls `GET /api/customer/shipping/widget-config?countryCode=CZ` which returns the `PickupPointWidgetConfig` from `IShippingCarrierFactory.ResolveAsync("CZ").WidgetConfig(...)`. The frontend loads the script (`https://widget.packeta.com/v6/www/js/library.js`) and initializes the widget with `PublicWidgetKey`. When the user picks a point, the widget callback supplies `{ id, name, place }` which the frontend stores in the order-form state until submission.

The widget is the **only** third-party JS the frontend loads. Loaded via `<Script>` with `strategy="lazyOnload"`; failure to load → form falls back to "manual address only", with a friendly note. This keeps the page resilient.

### CreateShipmentAsync (backend, on maker "Ship" action)

POST to Packeta REST API (JSON, modern endpoint preferred over the legacy XML one):

```json
{
  "apiPassword": "<ApiKey>",
  "packetAttributes": {
    "number":     "<order.OrderNumber>",
    "name":       "<first part of customer name>",
    "surname":    "<rest of customer name>",
    "email":      "<order.CustomerEmail>",
    "phone":      "<order.CustomerPhone>",
    "addressId":  "<order.ZasilkovnaBranchId>",  // pickup point id
    "value":      "<order.TotalPriceMinor / 100 in CZK>",
    "weight":     "<order.Product.WeightGrams / 1000 (kg)>",
    "eshop":      "<SenderLabel>"
  }
}
```

Response: `{ "id": 123456789, "barcode": "Z1234567890" }`.

On success:
- Store the carrier id on the order as `ShippingCarrierRef`.
- Compute tracking URL: `https://tracking.packeta.com/cs/?id={id}`.
- Move order from `Accepted` to `Shipped`. Set `ShippedAt = now()`, `AutoDeliverAt = now() + 7 days`.
- Insert outbox events: customer notification with tracking URL, maker confirmation.

### Label retrieval

Maker dashboard offers a "Download label" link. Backend endpoint: `GET /api/maker/orders/{orderId}/label`. Handler:
1. Verify the maker owns the order.
2. Check `BlobStorage` for a cached label PDF at `cz/orders/{orderId}/label.pdf`.
3. If absent: call `Packeta.GetLabelPdfAsync(carrierRef)`, write the result to blob storage, return the stream.
4. If present: stream from blob.

Caching avoids re-hitting Packeta every time the maker opens the page. Labels don't change after creation.

### Status sync (cron)

A timer-triggered Azure Function `SyncShipmentStatuses` runs every 6 hours. For each order in `Shipped` state with `AutoDeliverAt > now()` (not yet auto-delivered):

1. Call `Packeta.GetStatusAsync(carrierRef)`.
2. If status is `Delivered`: dispatch `MarkOrderDelivered.Command(orderId, source: "carrier")`. Sets `DeliveredAt`, transitions to `Delivered`.
3. If status is `Returned` or `Failed`: open an admin dispute (a new outbox event type, processed by admin notification).

This is an enrichment, not a substitute: the customer can still confirm receipt manually from the order page, and `AutoDeliverOrders` (the 7-day auto-deliver Function from `TISKNI_MVP_SPEC.md`) still runs.

### Personal pickup

Orders with `ShippingMethod = PersonalPickup` skip Packeta entirely. The maker marks the order `Shipped` after handover (UI labels this as "Předáno"). `AutoDeliverAt` still gets set to `now() + 7d` so escrow eventually releases.

### Error classification

Same `Transient | Permanent | Configuration | Unknown` taxonomy. Packeta-specific notes:
- HTTP 401 from Packeta → `Configuration` (API key wrong or expired)
- Packeta's `packetCreate` returning a validation error (e.g. unknown `addressId`) → `Permanent` for that order, requires admin/maker intervention
- HTTP 5xx / timeouts → `Transient`, scheduled for retry

### Multi-country future

`IShippingCarrier` is implemented per-carrier, not per-country. Packeta serves CZ, SK, HU, PL, RO and others — so the `packeta` adapter will serve multiple countries. When we add a market Packeta doesn't reach, we add a new adapter (DPD, GLS, …) and update `CountryConfiguration.DefaultShippingCarrier` for that country.

## Alternatives considered

- **Custom shipping address only, no pickup points** — rejected. CZ market expects pickup points; Zásilkovna is the dominant pattern.
- **Allow makers to use their own Packeta account** — rejected for MVP. Multi-tenant Packeta integration adds significant complexity (per-maker credentials, per-maker rate accounting); the platform's central account is simpler and the maker still gets a label PDF.
- **Backend re-renders the widget instead of loading Packeta's JS** — rejected. The widget hits Packeta's pickup-point API directly with their public key; reimplementing that risks staleness. Loading their JS is the documented approach.
- **Don't cache labels in blob storage** — rejected. Hitting Packeta every dashboard render is wasteful and rate-limit-risky.
- **No `SyncShipmentStatuses` cron; rely solely on the 7-day auto-deliver** — rejected. We can flip earlier-than-7-days when Packeta confirms delivery, improving the customer experience and shortening the escrow window. Cheap to add now.

## Consequences

### Positive
- Single widget integration covers the dominant CZ shipping expectation.
- Carrier swap is a new adapter + config row; handlers untouched.
- Label caching is cheap and resilient.
- Multi-step state (created → in transit → delivered) becomes observable via cron sync.

### Negative
- Packeta JS is a runtime dependency on their CDN. Graceful degradation if their CDN is down (manual address fallback).
- The widget script tracks users for analytics by default; we set widget options to minimize PII leakage (no UA, no customer name passed in).
- Personal pickup still requires escrow + auto-deliver; the maker has to remember to mark "Shipped" — UI nudges this.

## Compliance / verification

- SecOps: Packeta API key in Key Vault, never logged.
- SecOps: backend label endpoint authorization — only the maker who fulfills the order can fetch its label.
- Reviewer: no direct HTTP to Packeta outside `Infra.Clients/Packeta/`.
- Reviewer: order state transitions to `Shipped` only via `MarkOrderShipped.Command`; never inline.
- Integration test: maker A cannot fetch maker B's order label.
- Integration test: duplicate "ship" calls for the same order are rejected (the order is already in `Shipped`).

## Related

- Patterns: §A.14 error classification, §A.15 provider adapter, §A.20 idempotent webhooks (Packeta doesn't push webhooks at MVP scale — we pull via cron; the idempotency pattern still applies if we add webhook intake later)
- Roles: `docs/architecture/roles/shipping-carrier.md` (to be authored), `docs/architecture/roles/order.md`, `docs/architecture/roles/blob-storage.md`
- ADR 0011 (label PDFs stream via backend)
- ADR 0016 (Comgate webhook pattern is the template; Packeta's pull-based status sync mirrors it)
