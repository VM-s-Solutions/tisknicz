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

    /// <summary>
    /// Per-caller envelope for the Public host.
    ///
    /// <para>
    /// Sized for aggregate site traffic, not for one visitor. Behind the
    /// T-0153 same-origin proxy the browser never talks to this host
    /// directly — every anonymous request arrives from the frontend App
    /// Service's single egress IP, so this one partition covers the WHOLE
    /// site's anonymous traffic. The previous 60/min was below a single
    /// catalog page view and broke with a couple of concurrent visitors:
    /// the server render of <c>/katalog</c> got a 429, which the frontend
    /// folds to a transient error, so the page returned HTTP 200 carrying
    /// "Katalog se nepodařilo načíst".
    /// </para>
    ///
    /// <para>
    /// The authenticated hosts do not have this problem — their partition
    /// key is the <c>sub</c> claim, so the proxy's shared IP never
    /// collapses them into one bucket.
    /// </para>
    /// </summary>
    public const int PublicEnvelopePermitLimit = 300;

    /// <summary>
    /// Separate envelope for the anonymous blob-streaming routes
    /// (<c>/api/v{n}/files/**</c> — maker logos, product photos, avatars,
    /// order attachments).
    ///
    /// <para>
    /// These are bulk by nature: one catalog page requests a full page of
    /// maker logos, a maker profile adds every product thumbnail, a
    /// product detail adds the whole gallery. Counting them against the
    /// same budget as the JSON API meant ordinary browsing spent the
    /// envelope on images and starved the very API call that renders the
    /// page referencing them.
    /// </para>
    ///
    /// <para>
    /// They are also the cheapest thing this host serves — anonymous,
    /// immutable, <c>Cache-Control: public, max-age=86400</c> byte
    /// streams. The limiter is a scraping/bandwidth bound here, not a
    /// fairness control, so it is set an order of magnitude above what a
    /// real session can produce while still capping an unattended
    /// crawler.
    /// </para>
    /// </summary>
    public const int BlobStreamPermitLimit = 1000;

    public static IServiceCollection AddMakablesRateLimiting(
        this IServiceCollection services,
        string audience)
    {
        var (permitLimit, window) = audience.ToLowerInvariant() switch
        {
            MakablesHosts.Customer => (100, TimeSpan.FromMinutes(1)),
            MakablesHosts.Maker    => (60, TimeSpan.FromMinutes(1)),
            MakablesHosts.Admin    => (30, TimeSpan.FromMinutes(1)),
            MakablesHosts.Public   => (PublicEnvelopePermitLimit, TimeSpan.FromMinutes(1)),
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
    /// <c>sub</c> claim when present, else per <see cref="ConnectionInfo.RemoteIpAddress"/>,
    /// with a fixed <c>ip:unknown</c> fallback so a request with no resolvable
    /// IP lands in one bucket rather than escaping the limit. The per-audience
    /// <paramref name="permitLimit"/> / <paramref name="window"/> are the
    /// envelope computed in <see cref="AddMakablesRateLimiting"/>.
    ///
    /// <para>
    /// <b>Reverse-proxy prerequisite (secops):</b> the IP partition is the RAW
    /// connection IP. In the current deploy (direct Azure App Service, no Front
    /// Door / App Gateway / WAF) that IS the real client. If a reverse proxy is
    /// EVER placed in front, <c>UseForwardedHeaders</c> with a restricted
    /// <c>KnownProxies</c>/<c>KnownNetworks</c> MUST be wired into
    /// <c>UseMakablesPipeline</c> in the same change (plus a regression test) —
    /// otherwise every request collapses to the proxy IP (one shared bucket =
    /// self-DoS) or an un-validated <c>X-Forwarded-For</c> becomes a trivial
    /// bypass. Tracked in the launch checklist; this code does NOT trust XFF.
    /// </para>
    /// </summary>
    internal static RateLimitPartition<string> DefaultPartition(
        HttpContext http, int permitLimit, TimeSpan window)
    {
        var sub = http.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        var caller = !string.IsNullOrWhiteSpace(sub)
            ? $"user:{sub}"
            : IpPartitionKey(http);

        // Blob streams get their OWN bucket for the same caller. Sharing
        // one envelope let a page's images starve the API call that
        // renders the page — see BlobStreamPermitLimit.
        var isBlobStream = IsBlobStreamPath(http.Request.Path);
        var key = isBlobStream ? $"files:{caller}" : caller;
        var limit = isBlobStream ? BlobStreamPermitLimit : permitLimit;

        return RateLimitPartition.GetFixedWindowLimiter(
            key,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = limit,
                Window = window,
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                AutoReplenishment = true,
            });
    }

    /// <summary>
    /// True for the versioned blob-streaming routes — <c>/api/v{n}/files/{+rest}</c>,
    /// the shape produced by the <c>api/v{version:apiVersion}/files</c> route
    /// templates on <c>ProductImageController</c>, <c>ProfileImageController</c>
    /// and the per-audience <c>FilesController</c>s.
    ///
    /// <para>
    /// Matched segment-wise rather than with <c>StartsWith("/api/v")</c> so
    /// <c>/api/v1/filesystem/...</c> or a future <c>/api/v1/files-export</c>
    /// cannot slip into the far larger image budget. A bare
    /// <c>/api/v1/files</c> streams nothing and stays on the API envelope.
    /// </para>
    /// </summary>
    internal static bool IsBlobStreamPath(PathString path)
    {
        if (!path.HasValue)
        {
            return false;
        }

        var span = path.Value.AsSpan();
        // segment 1: "api"
        if (!TryTakeSegment(ref span, out var api) || !api.Equals("api", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // segment 2: "v" + a version ("v1", "v2", "v1.0"). Anything else
        // is not the versioned API root.
        if (!TryTakeSegment(ref span, out var version)
            || version.Length < 2
            || (version[0] != 'v' && version[0] != 'V')
            || !char.IsAsciiDigit(version[1]))
        {
            return false;
        }

        // segment 3: "files"
        if (!TryTakeSegment(ref span, out var files)
            || !files.Equals("files", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // ...and at least one more segment, or there is nothing to stream.
        return TryTakeSegment(ref span, out _);
    }

    /// <summary>
    /// Pops the next non-empty <c>/</c>-delimited segment off
    /// <paramref name="remaining"/>. Empty segments (a leading slash, a
    /// doubled slash, a trailing slash) are skipped rather than treated as
    /// a segment, so <c>//api//v1//files//x</c> classifies the same as the
    /// canonical path.
    /// </summary>
    private static bool TryTakeSegment(ref ReadOnlySpan<char> remaining, out ReadOnlySpan<char> segment)
    {
        while (!remaining.IsEmpty)
        {
            var slash = remaining.IndexOf('/');
            if (slash < 0)
            {
                segment = remaining;
                remaining = default;
                return !segment.IsEmpty;
            }

            segment = remaining[..slash];
            remaining = remaining[(slash + 1)..];
            if (!segment.IsEmpty)
            {
                return true;
            }
        }

        segment = default;
        return false;
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
    /// Raw connection-IP partition key with a fixed <c>ip:unknown</c> fallback
    /// so a request whose <see cref="ConnectionInfo.RemoteIpAddress"/> is
    /// unresolvable lands in a single shared bucket rather than escaping the
    /// limit. Does NOT read <c>X-Forwarded-For</c> — see the reverse-proxy
    /// prerequisite on <see cref="DefaultPartition"/>.
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
