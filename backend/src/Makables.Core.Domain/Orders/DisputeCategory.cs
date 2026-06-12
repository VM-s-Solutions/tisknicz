namespace Makables.Core.Domain.Orders;

/// <summary>
/// What the dispute is about — the structured triage axis the admin
/// reads before the free-text description. T-0106 user decision Q2.
///
/// <para>
/// Explicit <c>: short</c> backing per the <see cref="OrderCancellationSource"/>
/// precedent: persisted as SMALLINT and serialized as a stable integer
/// in outbox payloads — a reorder would silently rewrite stored rows.
/// New categories APPEND.
/// </para>
///
/// <para>
/// <see cref="CarrierReturned"/> / <see cref="CarrierFailed"/> are
/// <b>carrier-reserved</b> (§C.6): the customer/maker open-dispute
/// Validators reject them with
/// <see cref="Common.BusinessErrorMessage.OrderDisputeCategoryNotAllowed"/>;
/// only the T-0078 carrier sweep (via the rewired <c>DisputeShipment</c>)
/// and the admin open endpoint (transcribing a phone report) may set them.
/// </para>
/// </summary>
public enum DisputeCategory : short
{
    /// <summary>Party-selectable: the package never arrived.</summary>
    NotDelivered = 0,

    /// <summary>Party-selectable: the item arrived damaged.</summary>
    DamagedItem = 1,

    /// <summary>Party-selectable: the item does not match the listing / agreement.</summary>
    NotAsDescribed = 2,

    /// <summary>Carrier-reserved (§C.6): Packeta reported the shipment Returned.</summary>
    CarrierReturned = 3,

    /// <summary>Carrier-reserved (§C.6): Packeta reported the shipment Failed.</summary>
    CarrierFailed = 4,

    /// <summary>Party-selectable: anything else — the description carries the detail.</summary>
    Other = 5,
}
