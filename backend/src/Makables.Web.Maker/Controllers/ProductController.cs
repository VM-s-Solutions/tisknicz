using Asp.Versioning;
using Makables.Config.Controllers;
using Makables.Core.AppServices.Features.Products;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.Products;
using Makables.Core.Domain.Products.Validators;
using Makables.Core.Domain.Storage;
using Microsoft.AspNetCore.Authorization;
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
        long PriceAmountMinor, PriceType PriceType, int WeightGrams);

    public sealed record UpdateProductRequest(
        string CategoryId, string Title, string? Description,
        long PriceAmountMinor, PriceType PriceType, int WeightGrams);

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateProductRequest body, CancellationToken ct)
    {
        var result = await Mediator.Send(new CreateProduct.Command(
            CategoryId: body.CategoryId,
            Title: body.Title,
            Description: body.Description,
            PriceAmountMinor: body.PriceAmountMinor,
            PriceType: body.PriceType,
            WeightGrams: body.WeightGrams), ct);
        return HandleResult(result);
    }

    [HttpPut("{productId}")]
    public async Task<IActionResult> Update(string productId, [FromBody] UpdateProductRequest body, CancellationToken ct)
    {
        var result = await Mediator.Send(new UpdateProduct.Command(
            ProductId: productId,
            CategoryId: body.CategoryId,
            Title: body.Title,
            Description: body.Description,
            PriceAmountMinor: body.PriceAmountMinor,
            PriceType: body.PriceType,
            WeightGrams: body.WeightGrams), ct);
        return HandleResult(result);
    }

    [HttpDelete("{productId}")]
    public async Task<IActionResult> Delete(string productId, CancellationToken ct)
    {
        var result = await Mediator.Send(new DeleteProduct.Command(productId), ct);
        return HandleResult(result);
    }

    [HttpPost("{productId}/images")]
    [RequestSizeLimit(ImageUploadValidator.MaxSizeBytes + 4096)]  // file + small multipart overhead
    public async Task<IActionResult> UploadImage(string productId, IFormFile file, CancellationToken ct)
    {
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

        // Attach the blob path to the aggregate (IDOR-checked in the handler).
        var attach = await Mediator.Send(new AddProductImage.Command(productId, blobPath), ct);
        return HandleResult(attach);
    }

    [HttpDelete("{productId}/images/{imageId}")]
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
