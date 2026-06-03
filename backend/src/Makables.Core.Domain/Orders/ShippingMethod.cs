namespace Makables.Core.Domain.Orders;

/// <summary>
/// How the order reaches the customer. Per role/order.md the launch
/// catalogue ships <see cref="ZasilkovnaPickupPoint"/> (Packeta pickup
/// branch) and <see cref="PersonalPickup"/> (collect at the maker's
/// premises — gated by <c>Maker.PersonalPickupEnabled</c> in T-0034).
///
/// <para>
/// Stored as the string name via <c>HasConversion&lt;string&gt;()</c>;
/// explicit ints are belt-and-braces for any caller that bypasses the
/// converter (same policy as <see cref="OrderState"/>).
/// </para>
/// </summary>
public enum ShippingMethod
{
    /// <summary>Drop off at a Zásilkovna / Packeta pickup branch.</summary>
    ZasilkovnaPickupPoint = 0,

    /// <summary>Customer collects directly from the maker. No carrier ref.</summary>
    PersonalPickup = 1,
}
