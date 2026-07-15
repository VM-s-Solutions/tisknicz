using Asp.Versioning;
using Makables.Config.Controllers;
using Makables.Core.AppServices.Features.Categories;
using Makables.Core.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Makables.Web.Admin.Controllers;

/// <summary>
/// Admin category endpoints (T-0119 / US-admin-0013). Wires the T-0040
/// CRUD commands that until now had no HTTP surface. <c>[Authorize]</c>
/// under the admin audience (ADR 0013); every write implements
/// <c>IAdminAuditableCommand</c> so the before/after JSONB audit rides
/// the pipeline (ADR 0014). Category names/slugs/descriptions are
/// screened against <c>ProhibitedContent</c> in the command validators
/// (<c>category.nameNotAllowed</c>).
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/categories")]
[Authorize]
public sealed class CategoriesController(IIdGenerator ids) : MakablesApiController
{
    /// <summary>Request body for <see cref="Create"/>. The id is allocated server-side.</summary>
    public sealed record CreateCategoryRequest(
        string Name,
        string? Slug,
        string? Icon,
        string? Description,
        int SortOrder,
        string CountryCode,
        string? Notes);

    /// <summary>Request body for <see cref="Update"/>. The category id rides the route; the slug never changes on rename (US-admin-0013 AC-2).</summary>
    public sealed record UpdateCategoryRequest(
        string Name,
        string? Icon,
        string? Description,
        int SortOrder,
        string? Notes);

    /// <summary>Request body for <see cref="Deactivate"/>.</summary>
    public sealed record DeactivateCategoryRequest(string? Notes);

    /// <summary>
    /// Every category including deactivated rows, ordered by sort order —
    /// the admin dashboard list.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(GetAdminCategories.GetAdminCategoriesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAll(CancellationToken ct) =>
        HandleResult(await Mediator.Send(new GetAdminCategories.Query(), ct));

    /// <summary>
    /// Create a category (US-admin-0013 AC-1). The id is pre-allocated
    /// here so the audit pipeline has a stable <c>TargetId</c> (see the
    /// CreateCategory command doc). Slug defaults to a diacritics-stripped
    /// derivation of the name when omitted.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(CreateCategory.CreateCategoryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateCategoryRequest request, CancellationToken ct) =>
        HandleResult(await Mediator.Send(new CreateCategory.Command(
            Id: ids.Next(),
            Name: request.Name,
            Slug: request.Slug,
            Icon: request.Icon,
            Description: request.Description,
            SortOrder: request.SortOrder,
            CountryCode: request.CountryCode,
            Notes: request.Notes), ct));

    /// <summary>
    /// Rename / re-describe a category (US-admin-0013 AC-2). The slug is
    /// intentionally untouched so public URLs and product FKs survive.
    /// </summary>
    [HttpPut("{categoryId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        string categoryId, [FromBody] UpdateCategoryRequest request, CancellationToken ct) =>
        HandleResult(await Mediator.Send(new UpdateCategory.Command(
            CategoryId: categoryId,
            Name: request.Name,
            Icon: request.Icon,
            Description: request.Description,
            SortOrder: request.SortOrder,
            Notes: request.Notes), ct));

    /// <summary>
    /// Soft-deactivate a category (US-admin-0013 AC-3) — hidden from the
    /// public filter and new-product forms; existing products keep their FK.
    /// </summary>
    [HttpPost("{categoryId}/deactivate")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Deactivate(
        string categoryId, [FromBody] DeactivateCategoryRequest request, CancellationToken ct) =>
        HandleResult(await Mediator.Send(new DeactivateCategory.Command(categoryId, request.Notes), ct));
}
