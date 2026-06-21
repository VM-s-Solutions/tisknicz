using System.Globalization;
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
///     simplicity. Mounted as the per-host <c>GlobalLimiter</c> (T-0136 /
///     Q-0011) so it applies to EVERY endpoint that doesn't carry a tighter
///     <c>[EnableRateLimiting(...)]</c> attribute (which overrides the
///     global). Partitioned per authenticated <c>sub</c> claim, else per
///     remote IP — mirrors <see cref="PartitionAutocomplete"/>.</description></item>
///   <item><description><see cref="AuthPolicyName"/> ("auth", T-0136 /
///     Q-0011) — tight per-IP envelope (10/min, no queue) mounted
///     class-level on the anonymous <c>AuthController</c>. The
///     brute-force / credential-stuffing / enumeration surface; tighter
///     than the global default and composes under it. Matches the
///     ADR 0023 §4 "failed login rate > 50/min from same IP" alert
///     intent with an actual enforcement control.</description></item>
///   <item><description><see cref="AddressesAutocompletePolicyName"/>
///     ("addresses-autocomplete", T-0031) — partitioned per
///     authenticated <c>sub</c> claim (20/min) or per remote IP
///     (5/min) for unauthenticated requests. Mounted on the
///     Mapbox-proxy endpoint(s) so a single user can't burn through
///     the per-host quota and DoS everyone else, per ADR 0010
///     §"Mapbox autocomplete + geocoding" / §"Compliance".</description></item>
/// </list>
///
/// <para>
/// <b>In-memory caveat:</b> all limiters are per-instance (no distributed
/// store). At single-region MVP scale this is adequate; a multi-instance
/// scale-out would need a Redis-backed partition store. Flagged as a v1.1
/// concern, out of scope for T-0136.
/// </para>
/// </summary>
public static class MakablesRateLimitingExtensions
{
    public const string PolicyName = "default";
    public const string AddressesAutocompletePolicyName = "addresses-autocomplete";

    /// <summary>
    /// T-0136 (Q-0011): tight per-IP fixed-window policy on the anonymous
    /// auth endpoints (login / register / refresh / confirm-* / request-* /
    /// consume-magic-link). 10 req/min/IP, no queue (reject immediately so a
    /// credential-stuffer gets an instant 429). Mounted class-level on
    /// <c>AuthController</c> via <c>[EnableRateLimiting(AuthPolicyName)]</c>.
    /// </summary>
    public const string AuthPolicyName = "auth";

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
            // T-0136 (Q-0011): mount the per-host "default" envelope as the
            // GlobalLimiter so it applies to EVERY endpoint without a tighter
            // [EnableRateLimiting(...)] attribute (PostMessage, the catalog
            // reads, etc.). Partitioned per authenticated sub / per remote IP
            // so the budget is per-caller, not a single shared host bucket.
            // An endpoint that carries its own policy (auth / autocomplete /
            // shipping-widget) overrides this — ASP.NET applies the endpoint
            // limiter AND the global limiter; the request must pass both.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
                http => DefaultPartition(http, permitLimit, window));

            // The named "default" policy stays registered for explicit opt-in
            // via [EnableRateLimiting("default")] should a future endpoint want
            // the envelope without inheriting it globally (kept for parity with
            // the documented policy name).
            options.AddFixedWindowLimiter(PolicyName, opt =>
            {
                opt.PermitLimit = permitLimit;
                opt.Window = window;
                opt.QueueLimit = 10;
                opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
            });

            // T-0136 (Q-0011): tight per-IP envelope on the anonymous auth
            // surface. 10/min/IP, no queue — a credential-stuffer gets an
            // immediate 429 rather than a queued slow-down.
            options.AddPolicy(AuthPolicyName, http => PartitionAuth(http));

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

            // T-0136: surface a Retry-After so a well-behaved client backs off
            // for the remainder of the window instead of hammering. The
            // limiter exposes the retry hint via metadata when the window is
            // fixed; fall back to the configured window length otherwise.
            options.OnRejected = (context, _) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }
                return ValueTask.CompletedTask;
            };
        });

        return services;
    }

    /// <summary>
    /// T-0136 (Q-0011) global-limiter partition: per authenticated
    /// <c>sub</c> claim when present, else per remote IP (X-Forwarded-For-aware
    /// via <see cref="ConnectionInfo.RemoteIpAddress"/>), with a fixed
    /// <c>ip:unknown</c> fallback so an attacker can't bypass by dropping the
    /// header. The per-audience <paramref name="permitLimit"/> /
    /// <paramref name="window"/> are the envelope computed in
    /// <see cref="AddMakablesRateLimiting"/>.
    /// </summary>
    private static RateLimitPartition<string> DefaultPartition(
        HttpContext http, int permitLimit, TimeSpan window)
    {
        var sub = http.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        var key = !string.IsNullOrWhiteSpace(sub)
            ? $"user:{sub}"
            : IpPartitionKey(http);

        return RateLimitPartition.GetFixedWindowLimiter(
            key,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = window,
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                AutoReplenishment = true,
            });
    }

    /// <summary>
    /// T-0136 (Q-0011) auth-policy partition: per remote IP, 10/min, no queue.
    /// The auth endpoints are <c>[AllowAnonymous]</c>, so there is no <c>sub</c>
    /// claim to partition on — IP is the only signal.
    /// </summary>
    private static RateLimitPartition<string> PartitionAuth(HttpContext http) =>
        RateLimitPartition.GetFixedWindowLimiter(
            IpPartitionKey(http),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                AutoReplenishment = true,
            });

    /// <summary>
    /// Remote-IP partition key with a fixed <c>ip:unknown</c> fallback so a
    /// dropped <c>X-Forwarded-For</c> can't be used to escape the bucket.
    /// </summary>
    private static string IpPartitionKey(HttpContext http)
    {
        var ip = http.Connection.RemoteIpAddress?.ToString();
        return string.IsNullOrWhiteSpace(ip) ? "ip:unknown" : $"ip:{ip}";
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
