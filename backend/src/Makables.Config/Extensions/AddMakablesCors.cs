using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Makables.Config.Extensions;

/// <summary>
/// Per-audience CORS policy. Frontend origin is configured per environment
/// in <c>Cors:AllowedOrigins:&lt;audience&gt;</c>. Per ADR 0005 (per-audience
/// route groups + hosts).
/// </summary>
public static class MakablesCorsExtensions
{
    public const string PolicyName = "MakablesDefault";

    public static IServiceCollection AddMakablesCors(
        this IServiceCollection services,
        IConfiguration configuration,
        string audience)
    {
        var configuredOrigins = configuration
            .GetSection($"Cors:AllowedOrigins:{audience}")
            .Get<string[]>() ?? Array.Empty<string>();

        services.AddCors(options =>
        {
            options.AddPolicy(PolicyName, policy =>
            {
                if (configuredOrigins.Length == 0)
                {
                    // Dev convenience: in the absence of configured origins,
                    // allow localhost. Production MUST configure
                    // Cors:AllowedOrigins:<audience>.
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
