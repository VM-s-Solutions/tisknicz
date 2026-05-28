namespace Makables.Core.Domain.Products;

/// <summary>
/// How the price on a <see cref="Product"/> should be interpreted at
/// the catalog / order layer. Per role/product.md.
/// </summary>
public enum PriceType
{
    /// <summary>Fixed price — customer can place an order at this exact amount.</summary>
    Fixed = 0,

    /// <summary>"From X" — base price; final price negotiated per order (e.g. custom 3D prints sized to spec).</summary>
    From = 1,

    /// <summary>
    /// Price-on-request — the listed amount is informational only.
    /// Orders aren't placeable until the custom-quote flow ships
    /// (post-MVP). Allowed for free / zero-amount products.
    /// </summary>
    OnRequest = 2,
}
