using System.Net;
using Makables.Core.Domain.Common;
using Makables.Infra.Clients.Comgate;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Makables.Web.Public.Filters;

/// <summary>
/// Restricts requests to <see cref="ComgateOptions.WebhookAllowedIps"/>.
/// Per ADR 0016 §"Webhook handling" (T-0066) the Comgate webhook is
/// authenticated by source IP plus a re-fetch from the gateway; this
/// filter is the first layer (IP).
///
/// <para>
/// <b>IAuthorizationFilter</b>, not middleware: filters run AFTER
/// routing (so we know we are hitting the webhook endpoint) but BEFORE
/// model binding (so a rejected source never has its body parsed).
/// Same idiom as <c>[Authorize]</c>.
/// </para>
///
/// <para>
/// <b>Allowlist format (Q2).</b> Accepts BOTH bare IPs (<c>203.0.113.5</c>)
/// and CIDR ranges (<c>203.0.113.0/24</c>, <c>203.0.113.5/32</c>). CIDR
/// is parsed via <see cref="System.Net.IPNetwork"/> (the
/// <c>Microsoft.AspNetCore.HttpOverrides.IPNetwork</c> the ticket originally
/// cited was deprecated in .NET 10 / ASPDEPR005 in favour of this BCL type);
/// bare IPs via <see cref="IPAddress.TryParse"/>. Malformed entries are
/// logged Critical once each (deduped via a static set) and skipped at
/// request time so the rest of the list still evaluates.
/// </para>
///
/// <para>
/// <b>Source of the remote IP.</b> Only <c>HttpContext.Connection.RemoteIpAddress</c>
/// is consulted; this filter never reads <c>X-Forwarded-For</c> itself. It
/// does not have to: <c>UseMakablesForwardedHeaders</c> runs first in
/// <c>UseMakablesPipeline</c> and rewrites <c>RemoteIpAddress</c> from the
/// forwarded header wherever <c>ForwardedHeaders:Enabled</c> is set — which
/// the deployed environments do via
/// <c>infra/bicep/modules/app-service.bicep</c>, and local development does
/// not. So the value read here is the real client behind the App Service
/// front end, and the raw peer everywhere else. That coupling is deliberate
/// and load-bearing: without the middleware this filter compares Comgate's
/// ranges against the platform front end and, being fail-closed, rejects
/// every callback. If a SECOND proxy is ever introduced (Front Door, a WAF),
/// <c>MakablesForwardedHeadersOptions.ForwardLimit</c> must grow with it or
/// the recorded address will be the wrong hop. Pinned by
/// <c>ForwardedHeadersTests</c>.
/// </para>
///
/// <para>
/// <b>Route the callback at this host directly.</b> The frontend's
/// <c>/api-proxy</c> rewrite would add its own hop, making the last forwarded
/// entry the frontend egress IP — a permanent 401. The remedy is to fix the
/// notification URL in the Comgate portal, NEVER to add the frontend egress
/// IP to the allowlist: that would admit anyone who can reach the public
/// site, collapsing this layer entirely.
/// </para>
///
/// <para>
/// <b>Empty list = reject everything.</b> No implicit allow-all.
/// Operators must populate <c>Comgate:WebhookAllowedIps</c> before going
/// live; in Development the empty list is also rejected but the host
/// logs a Warning at first use so the misconfig is visible.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class ComgateWebhookIpAllowlistAttribute
    : Attribute, IFilterFactory
{
    public bool IsReusable => true;

    public IFilterMetadata CreateInstance(IServiceProvider serviceProvider)
    {
        var optionsMonitor = serviceProvider.GetRequiredService<IOptionsMonitor<ComgateOptions>>();
        var logger = serviceProvider.GetRequiredService<ILogger<ComgateWebhookIpAllowlistFilter>>();
        return new ComgateWebhookIpAllowlistFilter(optionsMonitor, logger);
    }
}

/// <summary>
/// Runtime filter created by <see cref="ComgateWebhookIpAllowlistAttribute"/>.
/// Public so the unit tests can construct it directly with stubbed
/// <see cref="IOptionsMonitor{TOptions}"/> + <see cref="ILogger{TCategoryName}"/>.
/// </summary>
public sealed class ComgateWebhookIpAllowlistFilter(
    IOptionsMonitor<ComgateOptions> options,
    ILogger<ComgateWebhookIpAllowlistFilter> logger) : IAuthorizationFilter
{
    // Static, process-wide dedupe set for malformed-entry Critical logs.
    // Without this every request would re-log the same broken allowlist
    // entry and drown the alerting pipeline. Lock-free read/write via
    // ConcurrentDictionary semantics is overkill here — the HashSet+lock
    // is fine because writes only happen on a brand-new malformed entry,
    // i.e. effectively zero traffic post-startup.
    private static readonly HashSet<string> LoggedMalformedEntries = new(StringComparer.Ordinal);
    private static readonly object LoggedMalformedLock = new();

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var remoteIp = context.HttpContext.Connection.RemoteIpAddress;
        if (remoteIp is null)
        {
            // Defensive: in TestServer / Kestrel-localhost edge cases
            // RemoteIpAddress can briefly be null. Refuse the request —
            // the webhook needs an authenticated source IP.
            logger.LogCritical(
                "Comgate webhook: rejected request with null RemoteIpAddress. Code={Code}.",
                BusinessErrorMessage.PaymentWebhookIpRejected);
            context.Result = new StatusCodeResult(StatusCodes.Status401Unauthorized);
            return;
        }

        var allowlist = options.CurrentValue.WebhookAllowedIps ?? Array.Empty<string>();
        if (allowlist.Count == 0)
        {
            // Empty list = reject everything. No implicit allow-all.
            logger.LogCritical(
                "Comgate webhook: WebhookAllowedIps is empty; rejecting {RemoteIp}. Code={Code}.",
                remoteIp, BusinessErrorMessage.PaymentWebhookIpRejected);
            context.Result = new StatusCodeResult(StatusCodes.Status401Unauthorized);
            return;
        }

        foreach (var entry in allowlist)
        {
            if (string.IsNullOrWhiteSpace(entry)) continue;
            var trimmed = entry.Trim();

            // CIDR first — covers both /24 ranges and /32 singletons.
            if (System.Net.IPNetwork.TryParse(trimmed, out var network)
                && network.Contains(remoteIp))
            {
                return;
            }

            // Bare IPs.
            if (IPAddress.TryParse(trimmed, out var ip)
                && ip.Equals(remoteIp))
            {
                return;
            }

            // Neither CIDR nor bare IP: malformed entry. Log once.
            // Critical because a malformed allowlist entry silently
            // shrinks the allowlist and could lock Comgate out.
            if (System.Net.IPNetwork.TryParse(trimmed, out _))
            {
                // CIDR parsed but didn't contain — not a malformed entry,
                // just a non-match. Skip without logging.
                continue;
            }
            if (IPAddress.TryParse(trimmed, out _))
            {
                // Bare IP parsed but didn't match — non-match, not malformed.
                continue;
            }

            // Genuinely unparseable.
            lock (LoggedMalformedLock)
            {
                if (LoggedMalformedEntries.Add(trimmed))
                {
                    logger.LogCritical(
                        "Comgate webhook: malformed allowlist entry '{Entry}' — neither CIDR nor IP. Entry skipped at request time. Fix Comgate:WebhookAllowedIps.",
                        trimmed);
                }
            }
        }

        // No allowlist entry matched — reject with 401, no body.
        logger.LogCritical(
            "Comgate webhook: rejected request from {RemoteIp} — not in allowlist. Code={Code}.",
            remoteIp, BusinessErrorMessage.PaymentWebhookIpRejected);
        context.Result = new StatusCodeResult(StatusCodes.Status401Unauthorized);
    }

    /// <summary>
    /// Test-only hook to clear the static dedupe set. The set is process-
    /// wide; tests that exercise "malformed entry logs Critical" need a
    /// fresh slate between runs. Public because the unit-test project
    /// doesn't have <c>InternalsVisibleTo</c> for Web.Public and that
    /// indirection isn't worth the wire shape change for one method.
    /// </summary>
    public static void ResetLoggedMalformedEntriesForTesting()
    {
        lock (LoggedMalformedLock)
        {
            LoggedMalformedEntries.Clear();
        }
    }
}
