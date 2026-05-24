using Makables.Core.Domain.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.RateLimiting;

namespace Makables.Config.Extensions;

/// <summary>
/// Per-audience rate-limit policy. Defaults: Customer 100/min, Maker 60/min,
/// Admin 30/min, Public 60/min. Tunable later via configuration; baked-in
/// for Phase 1 simplicity. Per ADR 0023 (NFRs).
/// </summary>
public static class MakablesRateLimitingExtensions
{
    public const string PolicyName = "default";

    public static IServiceCollection AddMakablesRateLimiting(
        this IServiceCollection services,
        string audience)
    {
        var (permitLimit, window) = audience.ToLowerInvariant() switch
        {
            MakablesHosts.Customer => (100, TimeSpan.FromMinutes(1)),
            MakablesHosts.Maker    => (60, TimeSpan.FromMinutes(1)),
            MakablesHosts.Admin    => (30, TimeSpan.FromMinutes(1)),
            MakablesHosts.Public   => (60, TimeSpan.FromMinutes(1)),
            _                      => (60, TimeSpan.FromMinutes(1)),
        };

        services.AddRateLimiter(options =>
        {
            options.AddFixedWindowLimiter(PolicyName, opt =>
            {
                opt.PermitLimit = permitLimit;
                opt.Window = window;
                opt.QueueLimit = 10;
                opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
            });

            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        });

        return services;
    }
}
