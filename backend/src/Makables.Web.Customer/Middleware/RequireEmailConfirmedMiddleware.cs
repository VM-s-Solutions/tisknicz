using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Makables.Core.Domain.Common;
using Microsoft.AspNetCore.Http;

namespace Makables.Web.Customer.Middleware;

/// <summary>
/// Customer-host gate that refuses authenticated requests whose
/// JWT does not carry an <see cref="MakablesClaimTypes.EmailConfirmedAt"/>
/// claim (i.e. the user has not yet confirmed their email).
///
/// <para>
/// Returns <c>403 Forbidden</c> with a typed
/// <see cref="BusinessErrorMessage.AuthEmailNotConfirmed"/> body so the
/// frontend's runtime error handler renders the existing i18n string.
/// Sits AFTER <c>UseAuthentication</c> + <c>UseAuthorization</c> so it
/// never runs on anonymous traffic (those are 401'd earlier).
/// </para>
///
/// <para>
/// <b>Why a claim, not a per-request DB hit.</b> Loading the
/// <c>User</c> row on every authenticated request would add a round-trip
/// to the hot path of every customer endpoint, just to read a single
/// boolean-equivalent column. <see cref="Infra.Common.Auth.JwtIssuer"/>
/// emits the claim on login / refresh; the 15-minute access-token
/// lifetime bounds how long a freshly-confirmed user has to wait for
/// the gate to open. Mirrors how the role / country_code claims drive
/// audience / scoping decisions without a DB hit.
/// </para>
///
/// <para>
/// <b>Bypasses.</b> Anonymous requests skip (they're handled by
/// <c>[Authorize]</c>); the SendEmailConfirmation endpoint at
/// <c>/api/v*/auth/*</c> must stay reachable while unconfirmed so the
/// customer can request a new confirmation link.
/// </para>
/// </summary>
public sealed class RequireEmailConfirmedMiddleware(RequestDelegate next)
{
    /// <summary>
    /// Wire-shape options for the typed <see cref="Error"/> body this
    /// middleware writes on a 403. Mirrors what <c>AddMakablesControllers</c>
    /// configures for every controller-returned <see cref="Error"/>:
    /// camelCase property names (<see cref="JsonSerializerDefaults.Web"/>)
    /// plus string-named enums (<see cref="JsonStringEnumConverter"/>).
    /// Without these options the middleware would emit
    /// <c>{"Field":"","Code":"auth.emailNotConfirmed","Type":2,...}</c>
    /// — PascalCase + numeric enum — and the frontend's
    /// <c>lib/runtime/api-fetch.ts</c> reader (which reads <c>payload.code</c>
    /// / <c>payload.type</c>) would fall through to a generic 403 message
    /// instead of resolving the typed i18n key. T-0063 Copilot review H-1.
    /// </summary>
    private static readonly JsonSerializerOptions ErrorJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task Invoke(HttpContext context)
    {
        // Anonymous → defer to [Authorize] / 401 elsewhere. We never
        // 403 a request that hasn't been authenticated; the 401 path
        // owns that response.
        if (context.User.Identity?.IsAuthenticated != true)
        {
            await next(context);
            return;
        }

        // Auth endpoints must stay reachable so an unconfirmed user can
        // resend the confirmation email, log out, etc. Path is
        // case-insensitive — request paths come in canonicalised but
        // mixed-case is legal in HTTP.
        if (IsAuthBypassPath(context.Request.Path))
        {
            await next(context);
            return;
        }

        // Confirmed if the claim is present at all; unconfirmed users
        // structurally omit it. The gate relies on JWT signature
        // validation: callers cannot add this claim to a token without
        // the server signing key (see MakablesClaimTypes.EmailConfirmedAt
        // remarks for the wider rationale).
        var confirmedClaim = context.User.FindFirstValue(MakablesClaimTypes.EmailConfirmedAt);
        if (!string.IsNullOrEmpty(confirmedClaim))
        {
            await next(context);
            return;
        }

        await WriteForbiddenAsync(context);
    }

    /// <summary>
    /// Returns true for paths shaped like <c>/api/auth/...</c> or
    /// <c>/api/v{N}/auth/...</c> (any version, including future v2/v3).
    /// Segment-based split rather than <see cref="PathString.StartsWithSegments"/>
    /// chains because the latter is whole-segment-anchored and would not
    /// match <c>/v1</c> against the pattern <c>/v</c> — T-0063 reviewer M-1.
    /// </summary>
    private static bool IsAuthBypassPath(PathString path)
    {
        if (!path.HasValue) return false;
        // Path.Value always starts with "/" for non-empty PathString values,
        // so [1..] safely skips that leading slash before splitting.
        var segments = path.Value!.TrimStart('/').Split(
            '/', StringSplitOptions.RemoveEmptyEntries);

        // Shapes to bypass:
        //   ["api", "auth", ...]                — version-less route
        //   ["api", "v1", "auth", ...]          — versioned route (canonical)
        //   ["api", "v{N}", "auth", ...]        — any future major version
        if (segments.Length < 2) return false;
        if (!string.Equals(segments[0], "api", StringComparison.OrdinalIgnoreCase))
            return false;

        if (string.Equals(segments[1], "auth", StringComparison.OrdinalIgnoreCase))
            return true;

        if (segments.Length < 3) return false;
        if (!IsVersionSegment(segments[1])) return false;
        return string.Equals(segments[2], "auth", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// True for "v1", "v2", "v10" — the API-versioning route pattern
    /// established in <c>AddMakablesApiVersioning</c>.
    /// </summary>
    private static bool IsVersionSegment(string segment)
    {
        if (segment.Length < 2) return false;
        if (segment[0] != 'v' && segment[0] != 'V') return false;
        for (var i = 1; i < segment.Length; i++)
        {
            if (segment[i] < '0' || segment[i] > '9') return false;
        }
        return true;
    }

    private static async Task WriteForbiddenAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        var error = Error.Forbidden(BusinessErrorMessage.AuthEmailNotConfirmed);
        // WriteAsJsonAsync sets Content-Type: application/json; charset=utf-8
        // and serialises with our explicit Web-shape options so the body
        // matches the controller wire shape exactly (camelCase fields +
        // string-named ErrorType). See ErrorJsonOptions remarks above.
        await context.Response.WriteAsJsonAsync(
            error, ErrorJsonOptions, context.RequestAborted);
    }
}
