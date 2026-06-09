namespace Makables.Core.Domain.Orders;

/// <summary>
/// Why an order was flagged for dispute. T-0078 ships the two
/// carrier-sourced reasons it emits (the Packeta Returned + Failed
/// terminal states); T-0106 will append the customer-initiated +
/// maker-initiated reasons when the real dispute domain lands.
///
/// <para>
/// The JSON payload format encodes this as an integer so appending new
/// values (e.g. <c>CustomerInitiated = 2</c>) does not break existing
/// deserialization. Values are stable on the wire. T-0078 locked
/// decision A.1.
/// </para>
/// </summary>
public enum DisputeReason
{
    /// <summary>
    /// Packeta reported <see cref="Shipping.ShipmentState.Returned"/>:
    /// the customer never picked up the package and the carrier returned
    /// it to the maker. Carrier-sourced; T-0078.
    /// </summary>
    CarrierReturned = 0,

    /// <summary>
    /// Packeta reported <see cref="Shipping.ShipmentState.Failed"/>:
    /// the carrier flagged a delivery failure (lost / damaged / refused).
    /// Carrier-sourced; T-0078.
    /// </summary>
    CarrierFailed = 1,
}
