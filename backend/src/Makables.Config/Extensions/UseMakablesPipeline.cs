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
        // Serilog request logging first so every request gets a structured
        // completion record (method, path, status, elapsed). Then our enrichment
        // middleware pushes request_id / correlation_id / user_id / country_code
        // onto LogContext for all downstream logs.
        app.UseSerilogRequestLogging();
        app.UseMiddleware<RequestEnrichmentMiddleware>();

        // Order matters: CORS → AuthN → AuthZ → RateLimiter → endpoints.
        app.UseCors(MakablesCorsExtensions.PolicyName);
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();

        return app;
    }
}
