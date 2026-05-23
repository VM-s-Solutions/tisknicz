using System.Security.Claims;
using System.Text.RegularExpressions;
using Makables.Core.Domain.Common;
using Microsoft.AspNetCore.Http;
using Serilog.Context;

namespace Makables.Config.Middleware;

/// <summary>
/// Enriches every log entry within the request scope with structured
/// properties: <c>request_id</c>, <c>correlation_id</c> (from W3C
/// <c>traceparent</c>), <c>user_id</c>, <c>country_code</c>. Per ADR 0023 §4.
///
/// The middleware must run AFTER <c>UseAuthentication</c> so the
/// <see cref="HttpContext.User"/> claims are populated (else <c>user_id</c>
/// and <c>country_code</c> are always anonymous/unknown — reviewer M-4 fix
/// to T-0014). The pipeline wiring in <c>UseMakablesPipeline</c> reflects
/// this ordering.
/// </summary>
public sealed partial class RequestEnrichmentMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var requestId = context.TraceIdentifier;
        var traceParent = context.Request.Headers["traceparent"].FirstOrDefault();
        var correlationId = ExtractTraceId(traceParent) ?? requestId;

        var userId = context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        var countryCode = context.User?.FindFirstValue(MakablesClaimTypes.CountryCode);

        using (LogContext.PushProperty("request_id", requestId))
        using (LogContext.PushProperty("correlation_id", correlationId))
        using (LogContext.PushProperty("user_id", userId ?? "anonymous"))
        using (LogContext.PushProperty("country_code", countryCode ?? "unknown"))
        {
            context.Response.Headers["x-correlation-id"] = correlationId;
            await next(context);
        }
    }

    /// <summary>
    /// W3C traceparent: <c>00-{32-hex-trace-id}-{16-hex-span-id}-{2-hex-flags}</c>.
    /// Strict regex so a malformed (or attacker-crafted) header doesn't
    /// poison our <c>correlation_id</c>.
    /// </summary>
    private static string? ExtractTraceId(string? traceParent)
    {
        if (string.IsNullOrEmpty(traceParent)) return null;
        var match = TraceParentRegex().Match(traceParent);
        return match.Success ? match.Groups[1].Value : null;
    }

    [GeneratedRegex(@"^00-([0-9a-f]{32})-[0-9a-f]{16}-[0-9a-f]{2}$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TraceParentRegex();
}
