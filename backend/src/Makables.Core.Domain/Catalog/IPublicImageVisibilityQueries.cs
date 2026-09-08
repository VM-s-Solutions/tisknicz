namespace Makables.Core.Domain.Catalog;

/// <summary>
/// Read-side gate for the anonymous image routes on the Public host
/// (<c>ProductImageController</c>, <c>ProfileImageController</c>).
///
/// <para>
/// <b>Why this exists (Q-0042).</b> Those routes streamed by blob path alone —
/// no product lookup, no soft-delete check, no maker-verification check. That
/// made <see cref="Makables.Core.Domain.Makers.Maker"/>'s verification gate
/// bypassable for image BYTES: every public read in <c>CatalogQueries</c> is
/// gated on <c>m.IsVerified</c>, so an unverified maker's products are invisible
/// in the catalog, but their image URLs still streamed 200 to anyone holding
/// one. The gate protected the listing and not the asset. Closing the anonymous
/// container ACL did not fix this, because the bypass was the backend's own
/// route, not the storage account.
/// </para>
///
/// <para>
/// <b>The predicates mirror <c>CatalogQueries</c> exactly</b>, and must keep
/// mirroring it: active row (the global soft-delete query filter) + confirmed
/// email + admin-verified maker. If the catalog gate changes, this changes with
/// it, or the storefront and the asset layer disagree again — which is the
/// original defect.
/// </para>
///
/// <para>
/// <b>No cache, deliberately.</b> These are primary-key lookups with one or two
/// joins, and the endpoints they guard already perform a network round-trip to
/// Azure Blob Storage — so the query is strictly cheaper than work the route
/// does anyway, and is not the hot-path cost worth optimising. The responses
/// also carry <c>Cache-Control: public, max-age=86400</c>, so repeat views are
/// absorbed by the client/CDN long before they reach the database. A cache here
/// would trade a correctness surface (a revoked product still served from a
/// stale entry) for a saving that has not been measured. If measurement ever
/// justifies one, it belongs behind this interface.
/// </para>
/// </summary>
public interface IPublicImageVisibilityQueries
{
    /// <summary>
    /// True when the product may be shown publicly: active product, active +
    /// email-confirmed owning user, active + admin-verified maker.
    /// </summary>
    Task<bool> IsProductImageVisibleAsync(string productId, CancellationToken cancellationToken);

    /// <summary>
    /// True when the maker's logo may be shown publicly — the same gate the
    /// maker profile and catalog cards use.
    /// </summary>
    Task<bool> IsMakerImageVisibleAsync(string makerId, CancellationToken cancellationToken);

    /// <summary>
    /// True when the user's avatar may be shown publicly: the user row is
    /// active and the email is confirmed.
    ///
    /// <para>
    /// Deliberately NOT gated on maker verification — an avatar belongs beside
    /// the reviews its owner wrote, and a reviewer is a customer, not a maker.
    /// The active check is what carries the weight here: self-service "Smazat
    /// účet" calls <c>MarkDeactivated</c>, which the global soft-delete filter
    /// then excludes, so an account deletion stops serving the photograph even
    /// though the blob itself survives (Q-0043).
    /// </para>
    /// </summary>
    Task<bool> IsUserAvatarVisibleAsync(string userId, CancellationToken cancellationToken);
}
