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
        // Order matters: CORS → AuthN → enrichment → request log → AuthZ → RateLimiter.
        // Enrichment runs AFTER UseAuthentication so HttpContext.User is populated
        // (T-0014 reviewer M-4 — earlier wiring made user_id always anonymous).
        // Serilog request logging runs INSIDE the enrichment scope so the request
        // completion record carries the correlation/user/country fields too.
        app.UseCors(MakablesCorsExtensions.PolicyName);
        app.UseAuthentication();
        app.UseMiddleware<RequestEnrichmentMiddleware>();
        app.UseSerilogRequestLogging();
        app.UseAuthorization();
        app.UseRateLimiter();

        return app;
    }
}
