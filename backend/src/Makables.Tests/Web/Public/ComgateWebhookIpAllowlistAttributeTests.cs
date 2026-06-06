using System.Net;
using FluentAssertions;
using Makables.Infra.Clients.Comgate;
using Makables.Web.Public.Filters;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Makables.Tests.Web.Public;

/// <summary>
/// Pins the T-0066 <see cref="ComgateWebhookIpAllowlistFilter"/> contract:
/// bare-IP + CIDR matching, malformed-entry handling (logged once and
/// skipped), empty-list rejection, null remote-IP rejection. The filter
/// is constructed directly with a stubbed
/// <see cref="IOptionsMonitor{TOptions}"/>; the
/// <see cref="AuthorizationFilterContext"/> is hand-built so no test
/// host is required.
/// </summary>
public class ComgateWebhookIpAllowlistAttributeTests
{
    public ComgateWebhookIpAllowlistAttributeTests()
    {
        // Each test starts with a fresh dedupe set — otherwise the
        // malformed-entry test would pass on its first run and silently
        // skip-log on every subsequent run because the static set still
        // holds the entry.
        ComgateWebhookIpAllowlistFilter.ResetLoggedMalformedEntriesForTesting();
    }

    private static AuthorizationFilterContext BuildContext(string? remoteIp)
    {
        var httpContext = new DefaultHttpContext();
        if (remoteIp is not null)
        {
            httpContext.Connection.RemoteIpAddress = IPAddress.Parse(remoteIp);
        }

        var actionContext = new ActionContext(
            httpContext,
            new RouteData(),
            new ActionDescriptor());

        return new AuthorizationFilterContext(actionContext, Array.Empty<IFilterMetadata>());
    }

    private static ComgateWebhookIpAllowlistFilter BuildFilter(params string[] allowedIps)
    {
        var options = new ComgateOptions
        {
            MerchantId = "12345",
            Secret = "secret",
            BaseUrl = "https://payments.comgate.test",
            WebhookAllowedIps = allowedIps,
        };
        var monitor = new StubOptionsMonitor<ComgateOptions>(options);
        return new ComgateWebhookIpAllowlistFilter(
            monitor, NullLogger<ComgateWebhookIpAllowlistFilter>.Instance);
    }

    [Fact]
    public void Bare_IP_match_allows_request()
    {
        var filter = BuildFilter("203.0.113.5");
        var context = BuildContext("203.0.113.5");

        filter.OnAuthorization(context);

        context.Result.Should().BeNull("a bare-IP match means fall-through to the action");
    }

    [Fact]
    public void CIDR_slash_24_match_allows_request()
    {
        var filter = BuildFilter("203.0.113.0/24");
        var context = BuildContext("203.0.113.5");

        filter.OnAuthorization(context);

        context.Result.Should().BeNull();
    }

    [Fact]
    public void CIDR_slash_32_match_allows_request()
    {
        var filter = BuildFilter("203.0.113.5/32");
        var context = BuildContext("203.0.113.5");

        filter.OnAuthorization(context);

        context.Result.Should().BeNull();
    }

    [Fact]
    public void Non_match_returns_401_with_no_body()
    {
        var filter = BuildFilter("203.0.113.0/24");
        var context = BuildContext("198.51.100.1");

        filter.OnAuthorization(context);

        context.Result.Should().BeOfType<StatusCodeResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public void Empty_allowlist_rejects_with_401()
    {
        var filter = BuildFilter(); // empty list
        var context = BuildContext("203.0.113.5");

        filter.OnAuthorization(context);

        context.Result.Should().BeOfType<StatusCodeResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public void Malformed_entry_is_skipped_and_remaining_entries_still_evaluated()
    {
        // First entry is garbage; second is a CIDR match for the request.
        var filter = BuildFilter("not-an-ip", "203.0.113.0/24");
        var context = BuildContext("203.0.113.5");

        filter.OnAuthorization(context);

        context.Result.Should().BeNull(
            "the valid entry that follows the malformed one must still be evaluated");
    }

    [Fact]
    public void IPv6_CIDR_match_allows_request()
    {
        var filter = BuildFilter("2001:db8::/32");
        var context = BuildContext("2001:db8::1");

        filter.OnAuthorization(context);

        context.Result.Should().BeNull();
    }

    [Fact]
    public void Null_RemoteIpAddress_returns_401()
    {
        var filter = BuildFilter("203.0.113.0/24");
        var context = BuildContext(remoteIp: null);

        filter.OnAuthorization(context);

        context.Result.Should().BeOfType<StatusCodeResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    private sealed class StubOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = value;
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
