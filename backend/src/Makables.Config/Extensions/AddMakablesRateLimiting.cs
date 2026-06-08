using System.Security.Claims;
using System.Threading.RateLimiting;
using Makables.Core.Domain.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace Makables.Config.Extensions;

/// <summary>
/// Rate-limit policies per ADR 0023 (NFRs).
///
/// <list type="bullet">
///   <item><description><see cref="PolicyName"/> ("default") — per-host
///     envelope. Defaults: Customer 100/min, Maker 60/min, Admin 30/min,
///     Public 60/min. Tunable later via configuration; baked-in for
///     simplicity. Applied via <c>[EnableRateLimiting("default")]</c>
///     or as the global policy.</description></item>
///   <item><description><see cref="AddressesAutocompletePolicyName"/>
///     ("addresses-autocomplete", T-0031) — partitioned per
///     authenticated <c>sub</c> claim (20/min) or per remote IP
///     (5/min) for unauthenticated requests. Mounted on the
///     Mapbox-proxy endpoint(s) so a single user can't burn through
///     the per-host quota and DoS everyone else, per ADR 0010
///     §"Mapbox autocomplete + geocoding" / §"Compliance".</description></item>
/// </list>
/// </summary>
public static class MakablesRateLimitingExtensions
{
    public const string PolicyName = "default";
    public const string AddressesAutocompletePolicyName = "addresses-autocomplete";

    /// <summary>
    /// T-0070: per-IP rate-limit policy on the public Packeta widget-config
    /// endpoint. 100 req/min/IP — with the 1h client cache on success, a
    /// legitimate customer hits this 1–2x per checkout. Bots / scrapers
    /// get blocked.
    /// </summary>
    public const string ShippingWidgetConfigPolicyName = "shipping-widget-config";

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

            // T-0031: per-user (or per-IP) partition for the Mapbox proxy.
            // The autocomplete endpoint is the most abuse-prone surface in
            // the system — a runaway scrape could burn the Mapbox monthly
            // free quota in minutes.
            options.AddPolicy(AddressesAutocompletePolicyName, http =>
                PartitionAutocomplete(http));

            // T-0070: per-IP 100/min on the public shipping widget-config.
            options.AddPolicy(ShippingWidgetConfigPolicyName, http =>
                PartitionShippingWidgetConfig(http));

            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        });

        return services;
    }

    private static RateLimitPartition<string> PartitionAutocomplete(HttpContext http)
    {
        // The JWT issuer mirrors the OAuth `sub` claim into
        // ClaimTypes.NameIdentifier (see Makables.Infra.Common/Auth/JwtIssuer.cs)
        // so partitioning here is equivalent to "per authenticated user".
        // T-0031 Copilot review: documenting the mirror so the policy
        // name (`addresses-autocomplete`) and the audited claim match
        // future readers' expectations.
        var sub = http.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!string.IsNullOrWhiteSpace(sub))
        {
            return RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: $"user:{sub}",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 20,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    AutoReplenishment = true,
                });
        }

        // Unauthenticated. Use the X-Forwarded-For-aware remote IP. The
        // ingress proxy in prod terminates TLS and sets X-Forwarded-For;
        // when ForwardedHeaders middleware is configured (a Phase-1
        // concern) RemoteIpAddress reflects the real client. If missing,
        // we fall back to a fixed bucket so an attacker can't bypass by
        // dropping the header.
        var ip = http.Connection.RemoteIpAddress?.ToString();
        var partitionKey = string.IsNullOrWhiteSpace(ip) ? "ip:unknown" : $"ip:{ip}";
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                AutoReplenishment = true,
            });
    }

    /// <summary>
    /// T-0070 partitioned per-IP limiter for the public shipping
    /// widget-config endpoint. 100/min/IP. The endpoint is anonymous,
    /// so JWT-claim partitioning never applies — we go straight to IP.
    /// </summary>
    private static RateLimitPartition<string> PartitionShippingWidgetConfig(HttpContext http)
    {
        var ip = http.Connection.RemoteIpAddress?.ToString();
        var partitionKey = string.IsNullOrWhiteSpace(ip) ? "ip:unknown" : $"ip:{ip}";
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                AutoReplenishment = true,
            });
    }
}
