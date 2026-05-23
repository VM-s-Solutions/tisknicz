using Microsoft.AspNetCore.Http;
using Serilog.Context;
using System.Security.Claims;

namespace Makables.Config.Middleware;

/// <summary>
/// Enriches every log entry within the request scope with structured
/// properties: <c>request_id</c>, <c>correlation_id</c> (from W3C traceparent),
/// <c>user_id</c>, <c>country_code</c>. Per ADR 0023 (NFRs).
///
/// Pushes the properties onto the Serilog <see cref="LogContext"/> stack so
/// any log statement during this request automatically carries them
/// without callers needing to opt in.
/// </summary>
public sealed class RequestEnrichmentMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var requestId = context.TraceIdentifier;
        var traceParent = context.Request.Headers["traceparent"].FirstOrDefault();
        var correlationId = ExtractTraceId(traceParent) ?? requestId;

        var userId = context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        var countryCode = context.User?.FindFirstValue("country_code");

        using (LogContext.PushProperty("request_id", requestId))
        using (LogContext.PushProperty("correlation_id", correlationId))
        using (LogContext.PushProperty("user_id", userId ?? "anonymous"))
        using (LogContext.PushProperty("country_code", countryCode ?? "unknown"))
        {
            await next(context);
        }
    }

    private static string? ExtractTraceId(string? traceParent)
    {
        // W3C traceparent format: "00-{32-hex-trace-id}-{16-hex-span-id}-{2-hex-flags}".
        if (string.IsNullOrEmpty(traceParent)) return null;
        var parts = traceParent.Split('-');
        return parts.Length >= 2 ? parts[1] : null;
    }
}
