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
