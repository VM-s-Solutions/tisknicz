using Asp.Versioning;
using Makables.Config.Controllers;
using Makables.Core.AppServices.Features.Products;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.Products;
using Makables.Core.Domain.Products.Validators;
using Makables.Core.Domain.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Makables.Web.Maker.Controllers;

/// <summary>
/// Maker-host product CRUD + image upload (US-maker-0004). Lives on the
/// Maker host only — a customer JWT can't reach it (audience enforced by
/// <c>AddMakablesAuth</c>). Every endpoint is <c>[Authorize]</c>; the
/// underlying commands additionally resolve the owning maker from the
/// session and IDOR-shield by id.
///
/// <para>
/// The image-upload endpoint is the one place file I/O happens at the
/// controller layer (ADR 0011 §"Uploads"): it validates size + MIME +
/// magic bytes, streams the bytes to blob storage under
/// <c>{country}/products/{productId}/{filename}</c>, then dispatches
/// <see cref="AddProductImage"/> to attach the blob path to the
/// aggregate.
/// </para>
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/products")]
[Authorize]
public sealed class ProductController(
    IMakerRepository makers,
    IBlobStorageClient blobs,
    IUserSessionProvider session,
    IIdGenerator ids) : MakablesApiController
{
    public sealed record CreateProductRequest(
        string CategoryId, string Title, string? Description,
        long PriceAmountMinor, PriceType PriceType, FulfillmentType FulfillmentType, int WeightGrams);

    public sealed record UpdateProductRequest(
        string CategoryId, string Title, string? Description,
        long PriceAmountMinor, PriceType PriceType, FulfillmentType FulfillmentType, int WeightGrams);

    // Dedicated controller-level response shapes for the two endpoints
    // whose handler returns a typed payload. The handler responses
    // (CreateProduct.Response, AddProductImage.Response) are both nested
    // records named `Response` — Microsoft.AspNetCore.OpenApi names
    // schemas by unqualified type name, so both would collide on a
    // single `Response` schema and NSwag would emit `Promise<Response>`
    // with whichever shape won the collision. Wrapping into distinct
    // top-level records here gives the spec two unique schema names
    // (`CreateProductResponse`, `UploadProductImageResponse`) without
    // touching the CQRS nesting convention in Core.AppServices. T-0049b.
    public sealed record CreateProductResponse(string Id);

    public sealed record UploadProductImageResponse(string ImageId);

    /// <summary>
    /// Paged list of the authenticated maker's products, INCLUDING
    /// soft-deleted ones (the dashboard surfaces drafts and
    /// recently-deactivated items). T-0049a.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedData<MakerProductListItem>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct)
    {
        var result = await Mediator.Send(new GetMyProducts.Query(
            Page: page ?? 1,
            PageSize: pageSize ?? GetMyProducts.DefaultPageSize), ct);
        return HandleResult(result);
    }

    /// <summary>
    /// Single product detail for the authenticated maker's dashboard
    /// edit form. Includes soft-deleted products. IDOR-shielded: a
    /// product owned by another maker is reported as NotFound. T-0049a.
    /// </summary>
    [HttpGet("{productId}")]
    [ProducesResponseType(typeof(MakerProductDetail), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(string productId, CancellationToken ct)
    {
        var result = await Mediator.Send(new GetMyProductById.Query(productId), ct);
        return HandleResult(result);
    }

    // No [ProducesResponseType(..., 400)] on the two [FromBody] mutations
    // (Create / Update) or the multipart UploadImage below: under
    // [ApiController] the framework emits a ValidationProblemDetails
    // (RFC 7807) for model-binding / multipart parse failures BEFORE
    // HandleResult runs — that body is NOT the domain Error shape. The
    // handlers' own FluentValidation 400s ARE Error-shaped, but declaring
    // one schema for "400" would mislead generated clients about the
    // other. Same pattern as CatalogController GetMakers (T-0046b). The
    // Delete / RemoveImage actions take only path params so they don't
    // trip model-binding 400s; their failure surface is just the 401/404
    // declared inline. T-0049b.
    [HttpPost]
    [ProducesResponseType(typeof(CreateProductResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Create([FromBody] CreateProductRequest body, CancellationToken ct)
    {
        var result = await Mediator.Send(new CreateProduct.Command(
            CategoryId: body.CategoryId,
            Title: body.Title,
            Description: body.Description,
            PriceAmountMinor: body.PriceAmountMinor,
            PriceType: body.PriceType,
            FulfillmentType: body.FulfillmentType,
            WeightGrams: body.WeightGrams), ct);
        // Project the handler's nested Response into the controller-level
        // shape so the OpenAPI schema gets a unique top-level name (see
        // CreateProductResponse remark above).
        return result.IsSuccess
            ? HandleResult(BusinessResult.Success(new CreateProductResponse(result.Value!.Id)))
            : HandleResult(BusinessResult.Failure<CreateProductResponse>(result.Error!));
    }

    [HttpPut("{productId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(string productId, [FromBody] UpdateProductRequest body, CancellationToken ct)
    {
        var result = await Mediator.Send(new UpdateProduct.Command(
            ProductId: productId,
            CategoryId: body.CategoryId,
            Title: body.Title,
            Description: body.Description,
            PriceAmountMinor: body.PriceAmountMinor,
            PriceType: body.PriceType,
            FulfillmentType: body.FulfillmentType,
            WeightGrams: body.WeightGrams), ct);
        return HandleResult(result);
    }

    [HttpDelete("{productId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(string productId, CancellationToken ct)
    {
        var result = await Mediator.Send(new DeleteProduct.Command(productId), ct);
        return HandleResult(result);
    }

    [HttpPost("{productId}/images")]
    [RequestSizeLimit(ImageUploadValidator.MaxSizeBytes + 4096)]  // file + small multipart overhead
    // 409 declared here (but not on the other mutations) because
    // AddProductImage explicitly returns Conflict for the 10-image cap.
    // 503 / blob-upload-transient is intentionally omitted: it's an
    // infrastructure failure mode, not part of the documented contract.
    [ProducesResponseType(typeof(UploadProductImageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UploadImage(string productId, IFormFile file, CancellationToken ct)
    {
        // Bare IFormFile parameter — multipart schema rewritten to the
        // canonical { type: "string", format: "binary" } + required by
        // AddMakablesOpenApi's operation transformer (T-0049c). The
        // defensive null / zero-length check below stays: spec-level
        // `required: ["file"]` informs the client, but the server still
        // enforces the runtime contract.
        if (file is null || file.Length == 0)
        {
            return BadRequest(Error.Validation("file", BusinessErrorMessage.FileInvalid));
        }

        var userId = session.GetUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(Error.Unauthorized());
        }

        // Resolve the maker for the country prefix in the blob path. The
        // AddProductImage command re-resolves + IDOR-checks, so this read
        // is purely to build the path; a mismatched product is rejected
        // downstream as NotFound.
        var maker = await makers.GetByUserIdAsync(userId, ct);
        if (maker is null)
        {
            return NotFound(Error.NotFound("maker"));
        }

        // Buffer the header bytes for the magic-byte sniff.
        await using var stream = file.OpenReadStream();
        var header = new byte[ImageUploadValidator.RequiredHeaderBytes];
        var read = await ReadAtLeastAsync(stream, header, ct);

        var validation = ImageUploadValidator.Validate(
            file.ContentType, file.Length, header.AsSpan(0, read));
        if (validation != ImageUploadValidator.Result.Valid)
        {
            var code = validation switch
            {
                ImageUploadValidator.Result.TooLarge => BusinessErrorMessage.FileTooLarge,
                ImageUploadValidator.Result.UnsupportedType => BusinessErrorMessage.FileUnsupportedType,
                _ => BusinessErrorMessage.FileInvalid,  // MagicByteMismatch → generic "invalid"
            };
            return BadRequest(Error.Validation("file", code));
        }

        // Build the blob path: {country}/products/{productId}/{ulid}.{ext}.
        // Random id in the filename prevents collisions + guessing.
        var ext = ImageUploadValidator.ExtensionFor(file.ContentType);
        var filename = $"{ids.Next()}.{ext}";
        var blobPath = $"{maker.CountryCode.ToLowerInvariant()}/products/{productId}/{filename}";

        // Re-open the stream from the start (we consumed the header bytes).
        await using var uploadStream = file.OpenReadStream();
        var upload = await blobs.UploadAsync(
            BlobContainer.ProductImages, blobPath, uploadStream, file.ContentType, ct);
        if (!upload.IsSuccess)
        {
            return HandleResult(upload);
        }

        // Attach the blob path to the aggregate (IDOR-checked + cap-checked
        // in the handler). If the attach fails — wrong maker/product
        // (NotFound) or the 10-image cap (Conflict) — the blob we just
        // uploaded would be orphaned and could be abused to consume
        // storage. Best-effort delete it on failure so a rejected upload
        // leaves no residue. T-0041 Copilot review High.
        var attach = await Mediator.Send(new AddProductImage.Command(productId, blobPath), ct);
        if (!attach.IsSuccess)
        {
            await blobs.DeleteAsync(BlobContainer.ProductImages, blobPath, ct);
            return HandleResult(BusinessResult.Failure<UploadProductImageResponse>(attach.Error!));
        }
        // Project the handler's nested Response into the controller-level
        // shape so the OpenAPI schema gets a unique top-level name (see
        // UploadProductImageResponse remark above).
        return HandleResult(BusinessResult.Success(new UploadProductImageResponse(attach.Value!.ImageId)));
    }

    [HttpDelete("{productId}/images/{imageId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveImage(string productId, string imageId, CancellationToken ct)
    {
        var result = await Mediator.Send(new RemoveProductImage.Command(productId, imageId), ct);
        return HandleResult(result);
    }

    /// <summary>
    /// Read up to <paramref name="buffer"/>.Length bytes, tolerating
    /// short reads (a stream may return fewer bytes per call). Returns
    /// the number actually read — fewer than the buffer length only at
    /// genuine end-of-stream (a file shorter than the header window,
    /// which the magic-byte check then rejects).
    /// </summary>
    private static async Task<int> ReadAtLeastAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(total), ct);
            if (n == 0) break;
            total += n;
        }
        return total;
    }
}
