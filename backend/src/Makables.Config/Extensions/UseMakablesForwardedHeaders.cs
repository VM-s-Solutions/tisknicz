using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;

namespace Makables.Config.Extensions;

/// <summary>
/// Non-secret options controlling <c>UseForwardedHeaders</c>. Bound from
/// the <c>ForwardedHeaders</c> configuration section.
/// </summary>
public sealed class MakablesForwardedHeadersOptions
{
    public const string SectionName = "ForwardedHeaders";

    /// <summary>
    /// Whether to rewrite <c>HttpContext.Connection.RemoteIpAddress</c> from
    /// the <c>X-Forwarded-For</c> header.
    ///
    /// <para>
    /// Defaults to <c>false</c> — trusting a forwarded header when nothing
    /// strips it is an IP-spoofing hole, so this is opt-in per environment
    /// rather than on by default. Deployed environments set
    /// <c>ForwardedHeaders__Enabled=true</c> from
    /// <c>infra/bicep/modules/app-service.bicep</c>; local development and
    /// the test hosts leave it off and keep the raw connection IP.
    /// </para>
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// How many proxy hops to walk back through <c>X-Forwarded-For</c>.
    ///
    /// <para>
    /// Defaults to <c>1</c>, and that value is the ENTIRE anti-spoofing
    /// control here. The middleware consumes entries right-to-left, and the
    /// App Service front end appends the real socket peer as the rightmost
    /// entry — so a client sending <c>X-Forwarded-For: 1.2.3.4</c> produces
    /// <c>1.2.3.4, &lt;their real ip&gt;</c> and we take the real one.
    /// Raising this without adding a real proxy hop lets the client choose
    /// which address we record, which would turn the Comgate allowlist into
    /// an open door. It must only grow in the same change that introduces
    /// the extra hop. Pinned by
    /// <c>ForwardedHeadersTests.Leftmost_Forged_Entry_Is_Not_Honoured</c>.
    /// </para>
    /// </summary>
    public int ForwardLimit { get; init; } = 1;
}

/// <summary>
/// Wires <c>UseForwardedHeaders</c> so the app sees the real client IP
/// rather than the reverse proxy's.
///
/// <para>
/// <b>Why this is load-bearing, not hygiene.</b> Three shipped surfaces read
/// <c>HttpContext.Connection.RemoteIpAddress</c> directly:
/// <list type="bullet">
/// <item><c>ComgateWebhookIpAllowlistFilter</c> — the first authentication
/// layer on the payment webhook. It is fail-closed, so behind a proxy it
/// compares Comgate's published ranges against the platform front-end
/// address and rejects every callback with 401. That is the only route an
/// order has to <c>Paid</c>. This is the surface the change exists for:
/// Comgate posts to the API host directly, with no other hop.</item>
/// <item>The anonymous rate-limit partitions in
/// <c>MakablesRateLimitingExtensions</c> — fixed for callers that reach a
/// host directly, but NOT for browser traffic, which arrives through the
/// frontend's <c>/api-proxy</c> rewrite and still collapses to the frontend
/// egress IP. See Q-0039 in <c>docs/questions/open.md</c>, which stays open.</item>
/// <item><c>AuthController</c> stamps the address into
/// <c>RefreshToken.IpAddress</c> / <c>OneTimeToken.IpAddress</c>. Recorded
/// only, never used as a validation predicate, so nothing invalidates — but
/// the values become meaningful rather than uniform after this change.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Trust model.</b> <c>KnownIPNetworks</c> and <c>KnownProxies</c> are
/// cleared, which disables the middleware's peer range-check — the platform
/// front end's address is neither stable nor enumerable, and this is
/// Microsoft's documented posture for App Service. Safety therefore rests on
/// <see cref="MakablesForwardedHeadersOptions.ForwardLimit"/>, not on the
/// cleared lists; see that property.
/// </para>
///
/// <para>
/// <b>Only <c>X-Forwarded-For</c> is honoured, deliberately.</b>
/// <c>XForwardedProto</c> is NOT enabled: nothing in this change needs the
/// scheme, and rewriting it would silently alter the <c>Request.Scheme</c>
/// that <c>AuthController</c> uses to rebuild the OAuth <c>redirect_uri</c>,
/// which is exact-match-verified against the signed state. That is a
/// separate change with its own test, not a rider on an IP fix.
/// </para>
///
/// <para>
/// <b>One call site, by construction.</b> Options are built inline here
/// rather than registered separately, so a host cannot end up calling the
/// pipeline half without the registration half — that split would silently
/// degrade to <c>ForwardedHeaders.None</c>, which is exactly the
/// nothing-happens failure this change exists to repair.
/// </para>
///
/// <para>
/// Never set the platform's own <c>ASPNETCORE_FORWARDEDHEADERS_ENABLED</c>
/// alongside this: it registers a SECOND forwarded-headers middleware, and
/// because the first truncates the entries it consumed, the second would
/// read an attacker-controlled entry.
/// </para>
/// </summary>
public static class MakablesForwardedHeadersExtensions
{
    /// <summary>
    /// Runs the middleware when <c>ForwardedHeaders:Enabled</c> is set. Must
    /// be the FIRST call in the pipeline — anything that reads the remote IP
    /// before it (CORS, rate limiting, the webhook filter) would otherwise
    /// see the proxy's address.
    /// </summary>
    public static WebApplication UseMakablesForwardedHeaders(this WebApplication app)
    {
        var options = app.Configuration
            .GetSection(MakablesForwardedHeadersOptions.SectionName)
            .Get<MakablesForwardedHeadersOptions>() ?? new MakablesForwardedHeadersOptions();

        if (!options.Enabled)
        {
            return app;
        }

        var forwarded = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor,
            ForwardLimit = options.ForwardLimit,
        };

        // KnownIPNetworks, not the KnownNetworks property — the latter is
        // obsolete in .NET 10 (ASPDEPR005), the same deprecation
        // ComgateWebhookIpAllowlist already had to route around.
        forwarded.KnownIPNetworks.Clear();
        forwarded.KnownProxies.Clear();

        app.UseForwardedHeaders(forwarded);

        return app;
    }
}
