using Asp.Versioning;
using Makables.Config.Controllers;
using Makables.Core.AppServices.Features.Catalog;
using Microsoft.AspNetCore.Authorization;
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
    /// </summary>
    [HttpGet("makers")]
    public async Task<IActionResult> GetMakers(
        [FromQuery] string country = "CZ",
        [FromQuery] string? category = null,
        [FromQuery] string? city = null,
        [FromQuery(Name = "minRating")] int? minRatingStars = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = GetPagedMakers.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(new GetPagedMakers.Query(
            CountryCode: country,
            CategorySlug: category,
            City: city,
            MinRatingStars: minRatingStars,
            Page: page,
            PageSize: pageSize), cancellationToken);
        return HandleResult(result);
    }
}
