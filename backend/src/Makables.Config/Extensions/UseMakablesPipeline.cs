using Makables.Config.Extensions;
using Makables.Config.Middleware;
using Microsoft.AspNetCore.Builder;
using Serilog;

namespace Makables.Config;

/// <summary>
/// Application-pipeline wiring shared across the four Web hosts. The
/// individual <c>Program.cs</c> files call this after building the app.
/// Per ADR 0008 / patterns §A.16.
/// </summary>
public static class UseMakablesPipelineExtensions
{
    public static WebApplication UseMakablesPipeline(this WebApplication app)
    {
        // Order matters: ForwardedHeaders → CORS → AuthN → enrichment →
        // request log → AuthZ → RateLimiter.
        //
        // ForwardedHeaders must come first: it rewrites
        // Connection.RemoteIpAddress, and everything after it that reads the
        // address — the anonymous rate-limit partitions here, and
        // ComgateWebhookIpAllowlistFilter on the public host — would otherwise
        // see the reverse proxy instead of the client. The stage itself is a
        // no-op unless ForwardedHeaders:Enabled is set, which only deployed
        // environments do; being first is what is unconditional, not being on.
        //
        // Enrichment runs AFTER UseAuthentication so HttpContext.User is populated
        // (T-0014 reviewer M-4 — earlier wiring made user_id always anonymous).
        // Serilog request logging runs INSIDE the enrichment scope so the request
        // completion record carries the correlation/user/country fields too.
        app.UseMakablesForwardedHeaders();
        app.UseCors(MakablesCorsExtensions.PolicyName);
        app.UseAuthentication();
        app.UseMiddleware<RequestEnrichmentMiddleware>();
        app.UseSerilogRequestLogging();
        app.UseAuthorization();
        app.UseRateLimiter();

        return app;
    }
}
