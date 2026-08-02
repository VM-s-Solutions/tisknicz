using Asp.Versioning;
using Makables.Config.Controllers;
using Makables.Core.AppServices.Features.Catalog;
using Makables.Core.Domain.Catalog;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Makers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Makables.Web.Public.Controllers;

/// <summary>
/// Public catalog browse endpoint (US-customer-0007). Anonymous — the
/// catalog is the storefront. Filters (category / city / rating) +
/// pagination flow straight through to <see cref="GetPagedMakers"/>;
/// the response is the <c>PagedData&lt;MakerListItem&gt;</c> the
/// /katalog frontend (T-0046) renders.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/catalog")]
[AllowAnonymous]
public sealed class CatalogController : MakablesApiController
{
    /// <summary>
    /// Paged maker list. <paramref name="country"/> defaults to CZ (the
    /// launch market). <paramref name="page"/> is 1-based;
    /// <paramref name="pageSize"/> defaults to 24 and is capped at 48 by
    /// the handler.
    /// <para>
    /// <paramref name="category"/> is repeatable
    /// (<c>?category=3d-tisk&amp;category=laser-cnc</c>) and OR-ed by the
    /// query — the catalog filter is a multi-select. A single
    /// <c>?category=</c> value still binds, so pre-multi-select links keep
    /// working.
    /// </para>
    /// <para>
    /// <paramref name="legalType"/> narrows to companies
    /// (<c>?legalType=LegalEntity</c>) or individual traders
    /// (<c>?legalType=NaturalPerson</c>). Omitted means no constraint;
    /// makers whose legal form could not be classified match neither
    /// value and so appear only in the unfiltered list.
    /// </para>
    /// </summary>
    // No [ProducesResponseType(..., 400)] here: under [ApiController] the
    // framework rejects malformed query values (page/pageSize/minRating
    // not parseable as int) with a ValidationProblemDetails (RFC 7807)
    // body before the handler runs — that shape is NOT our domain Error.
    // The handler's own FluentValidation 400 IS the Error shape, but
    // declaring 400 -> Error would mislead generated clients about the
    // model-binding path. T-0046b Copilot review.
    [HttpGet("makers")]
    [ProducesResponseType(typeof(PagedData<MakerListItem>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMakers(
        [FromQuery] string country = "CZ",
        [FromQuery] string[]? category = null,
        [FromQuery] string? city = null,
        [FromQuery(Name = "minRating")] int? minRatingStars = null,
        [FromQuery] MakerLegalType? legalType = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = GetPagedMakers.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(new GetPagedMakers.Query(
            CountryCode: country,
            CategorySlugs: category,
            City: city,
            MinRatingStars: minRatingStars,
            LegalType: legalType,
            Page: page,
            PageSize: pageSize), cancellationToken);
        return HandleResult(result);
    }

    /// <summary>
    /// Active categories for the country (T-0119). Feeds the catalog
    /// filter dropdown and the maker product-creation form — replaces the
    /// frontend's hardcoded launch-category list so admin-created
    /// categories surface without a deploy. Reference data that changes
    /// rarely → short public cache (same pattern as the shipping
    /// widget-config endpoint, shorter TTL because admins expect edits
    /// to show up within minutes).
    /// </summary>
    [HttpGet("categories")]
    [ProducesResponseType(typeof(GetPublicCategories.GetPublicCategoriesResponse), StatusCodes.Status200OK)]
    [ResponseCache(Duration = 300, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> GetCategories(
        [FromQuery] string country = "CZ",
        CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(new GetPublicCategories.Query(country), cancellationToken);
        return HandleResult(result);
    }

    /// <summary>Public maker profile by slug (US-customer-0008).</summary>
    [HttpGet("makers/{slug}")]
    [ProducesResponseType(typeof(MakerProfile), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMakerProfile(string slug, CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(new GetMakerBySlug.Query(slug), cancellationToken);
        return HandleResult(result);
    }

    /// <summary>Public product detail by id (US-customer-0009).</summary>
    [HttpGet("products/{productId}")]
    [ProducesResponseType(typeof(ProductDetail), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProduct(string productId, CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(new GetProductById.Query(productId), cancellationToken);
        return HandleResult(result);
    }
}
