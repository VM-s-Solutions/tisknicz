namespace Makables.Core.Domain.Orders;

/// <summary>
/// Carrier-WIRE enum: why the T-0078 status sync flagged a shipment for
/// dispute (the Packeta Returned + Failed terminal states). Deliberately
/// NOT extended with party-initiated reasons — T-0106 §C.5 keeps this as
/// the stable two-value wire shape between
/// <c>SyncShipmentStatusesFunction</c> and <c>DisputeShipment.Command</c>;
/// the DOMAIN axis is <see cref="DisputeCategory"/>, which the rewired
/// handler maps onto (<see cref="CarrierReturned"/> →
/// <see cref="DisputeCategory.CarrierReturned"/>, <see cref="CarrierFailed"/>
/// → <see cref="DisputeCategory.CarrierFailed"/>).
///
/// <para>
/// The JSON payload format encodes this as an integer; values are stable
/// on the wire. T-0078 locked decision A.1.
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
