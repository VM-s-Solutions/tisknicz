using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Makables.Config.Extensions;

/// <summary>
/// Per-audience CORS policy. Frontend origin is configured per environment
/// in <c>Cors:AllowedOrigins:&lt;audience&gt;</c>. Per ADR 0005 (per-audience
/// route groups + hosts).
///
/// <para>
/// T-0035 sec reviewer B1: the localhost fallback is gated on
/// <see cref="IHostEnvironment.IsDevelopment"/>. Outside Development,
/// an empty <c>Cors:AllowedOrigins:&lt;audience&gt;</c> CRASHES the host
/// at startup rather than silently allowing <c>http://localhost:3000</c>
/// with credentials — which would expose authenticated cross-origin
/// POSTs from a developer's localhost to a misconfigured prod/staging
/// deploy.
/// </para>
/// </summary>
public static class MakablesCorsExtensions
{
    public const string PolicyName = "MakablesDefault";

    public static IServiceCollection AddMakablesCors(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment,
        string audience)
    {
        ArgumentNullException.ThrowIfNull(environment);

        var configuredOrigins = configuration
            .GetSection($"Cors:AllowedOrigins:{audience}")
            .Get<string[]>() ?? Array.Empty<string>();

        // Allow the localhost fallback in Development and the integration-
        // test environment (used by WebApplicationFactory). Every other
        // environment (Staging / Production) MUST configure explicit
        // origins — fail-closed avoids a missing prod env var silently
        // accepting localhost cross-origin requests with credentials.
        var allowLocalhostFallback =
            environment.IsDevelopment() ||
            string.Equals(environment.EnvironmentName, "IntegrationTest", StringComparison.OrdinalIgnoreCase);

        if (configuredOrigins.Length == 0 && !allowLocalhostFallback)
        {
            throw new InvalidOperationException(
                $"CORS allowed origins for audience '{audience}' are not configured. " +
                $"Set the Cors:AllowedOrigins:{audience} configuration array " +
                "to an explicit list of frontend origins (one entry per environment). " +
                "The localhost fallback is intentionally restricted to " +
                "Development / IntegrationTest so a missing prod env var fails closed.");
        }

        services.AddCors(options =>
        {
            options.AddPolicy(PolicyName, policy =>
            {
                if (configuredOrigins.Length == 0)
                {
                    // Development-only: in the absence of configured origins,
                    // allow localhost. The non-dev branch throws above, so
                    // reaching this code path on prod/staging is impossible.
                    policy.WithOrigins(
                            "http://localhost:3000",
                            "https://localhost:3000")
                        .AllowAnyHeader()
                        .AllowAnyMethod()
                        .AllowCredentials();
                }
                else
                {
                    policy.WithOrigins(configuredOrigins)
                        .AllowAnyHeader()
                        .AllowAnyMethod()
                        .AllowCredentials();
                }
            });
        });

        return services;
    }
}
