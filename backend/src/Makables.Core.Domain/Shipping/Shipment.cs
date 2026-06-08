namespace Makables.Core.Domain.Shipping;

/// <summary>
/// Result of <see cref="IShippingCarrier.CreateShipmentAsync"/> — the
/// carrier's numeric reference (Packeta's <c>id</c>) plus the pre-computed
/// customer-facing tracking URL.
///
/// <para>
/// <see cref="CarrierRef"/> is the Packeta numeric id (e.g.
/// <c>"123456789"</c>); the human-readable barcode is display-only and
/// NOT carried on the platform-side aggregate. T-0072 stamps both
/// <see cref="CarrierRef"/> and <see cref="TrackingUrl"/> onto the Order
/// row in a single <c>Order.Ship</c> call.
/// </para>
///
/// <para>
/// <see cref="TrackingUrl"/> is the canonical customer-facing tracking
/// link (<c>https://tracking.packeta.com/Z{id}</c> at MVP). Pre-computed
/// here so every downstream consumer (T-0072 outbox payload, T-0078
/// admin view, T-0086 frontend timeline) reads it from the Order row
/// — a Packeta-domain pivot only needs a backend code change, not a
/// re-deploy across every consumer. T-0070 locked decision A.1.
/// </para>
/// </summary>
public sealed record Shipment(string CarrierRef, string TrackingUrl);
