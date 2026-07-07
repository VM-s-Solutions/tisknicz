namespace Makables.Core.Domain.Products;

/// <summary>
/// Whether a <see cref="Product"/> is produced to the customer's
/// specification ("na zakázku") or held ready to ship ("skladem"). Per
/// role/product.md + T-0144 (dopady §2.4). Drives the checkout-time
/// withdrawal-right notice: § 1837 písm. d) občanského zákoníku exempts
/// made-to-order goods from the standard 14-day right of withdrawal;
/// in-stock goods carry the normal 14-day right.
/// </summary>
public enum FulfillmentType
{
    /// <summary>
    /// Produced to the customer's specification after the order is
    /// placed. Default — matches the platform's dominant use case
    /// (custom 3D printing / textile / laser / CNC) and is the safer
    /// legal posture for pre-existing rows (T-0144 AC-6).
    /// </summary>
    MadeToOrder = 0,

    /// <summary>Held ready to ship; carries the standard 14-day withdrawal right.</summary>
    InStock = 1,
}
