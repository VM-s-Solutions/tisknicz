using Makables.Config.Extensions;
using Microsoft.AspNetCore.Builder;

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
        // Order matters: CORS → AuthN → AuthZ → RateLimiter → endpoints.
        app.UseCors(MakablesCorsExtensions.PolicyName);
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();

        return app;
    }
}
