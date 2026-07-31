using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Makables.Core.Domain.Common;

namespace Makables.Config.Controllers;

/// <summary>
/// Base controller for every API host. Provides <see cref="Mediator"/>
/// access and the <c>HandleResult</c> overloads that map
/// <see cref="BusinessResult"/> to HTTP responses. Per ADR 0002 and patterns §A.6.
/// </summary>
[ApiController]
public abstract class MakablesApiController : ControllerBase
{
    private ISender? _mediator;

    protected ISender Mediator =>
        _mediator ??= HttpContext.RequestServices.GetRequiredService<ISender>();

    protected IActionResult HandleResult(BusinessResult result)
    {
        if (result.IsSuccess)
        {
            return Ok();
        }

        return MapErrorToActionResult(result.Error!);
    }

    protected IActionResult HandleResult<T>(BusinessResult<T> result)
    {
        if (result.IsSuccess)
        {
            return Ok(result.Value);
        }

        return MapErrorToActionResult(result.Error!);
    }

    /// <summary>
    /// Read up to <paramref name="buffer"/>.Length bytes from an upload
    /// stream, tolerating short reads (a stream may return fewer bytes
    /// per call). Returns the number actually read — fewer than the
    /// buffer length only at genuine end-of-stream (a file shorter than
    /// the magic-byte header window, which the validator then rejects).
    ///
    /// <para>
    /// Lives on the base controller because every upload endpoint needs
    /// it before calling its per-concern validator (product images,
    /// order attachments, profile images).
    /// </para>
    /// </summary>
    protected static async Task<int> ReadAtLeastAsync(Stream stream, byte[] buffer, CancellationToken ct)
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

    private IActionResult MapErrorToActionResult(Error error) =>
        error.Type switch
        {
            ErrorType.Validation => BadRequest(error),
            ErrorType.Unauthorized => Unauthorized(error),
            ErrorType.Forbidden => StatusCode(StatusCodes.Status403Forbidden, error),
            ErrorType.NotFound => NotFound(error),
            ErrorType.Conflict => Conflict(error),
            ErrorType.Transient => StatusCode(StatusCodes.Status503ServiceUnavailable, error),
            ErrorType.Permanent => UnprocessableEntity(error),
            ErrorType.Configuration => StatusCode(StatusCodes.Status500InternalServerError, error),
            ErrorType.Unknown => StatusCode(StatusCodes.Status500InternalServerError, error),
            _ => StatusCode(StatusCodes.Status500InternalServerError, error)
        };
}
