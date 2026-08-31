using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Makables.IntegrationTests.HostStartup;

/// <summary>
/// Pins <c>UseForwardedHeaders</c> through the REAL public-host pipeline,
/// against its real consumer: the Comgate webhook IP allowlist.
///
/// <para>
/// This is the seam that made the payment webhook unusable in every deployed
/// environment. <c>ComgateWebhookIpAllowlistFilter</c> compares Comgate's
/// published ranges against <c>Connection.RemoteIpAddress</c>, and behind the
/// Azure App Service front end that address is the platform's, not Comgate's —
/// so a correctly-configured allowlist still rejected 100% of callbacks, and
/// the webhook is the only route an order has to <c>Paid</c>.
/// </para>
///
/// <para>
/// Asserted end to end rather than against the extension in isolation,
/// deliberately: the bug was never in the middleware, it was in the middleware
/// not being <i>wired</i>. A unit test of
/// <c>MakablesForwardedHeadersExtensions</c> would have passed the whole time.
/// </para>
///
/// <para>
/// <b>No outbound traffic.</b> A request that passes the allowlist never gets
/// as far as the gateway: the handler first resolves the country's payment
/// provider, which queries a <c>CountryConfiguration</c> table that does not
/// exist on the harness's schema-less in-memory SQLite, and the request dies
/// there. <c>Comgate:BaseUrl</c> is pinned at the loopback discard port
/// anyway as defence in depth (the harness's own default is already an
/// unresolvable <c>.test</c> host). The assertions only ever distinguish 401
/// (filter rejected) from anything else (filter passed), which is exactly the
/// boundary under test.
/// </para>
/// </summary>
public class ForwardedHeadersTests
{
    private const string WebhookPath = "/api/v1/public/webhooks/comgate";
    private const string ComgateIp = "203.0.113.5";

    [Fact]
    public async Task Forwarded_Client_Ip_Is_Honoured_When_Enabled()
    {
        using var factory = BuildPublicHost(forwardedHeadersEnabled: true);
        using var client = factory.CreateClient();

        var outcome = await PostWebhookAsync(client, forwardedFor: ComgateIp);

        outcome.Should().NotBe(
            HttpStatusCode.Unauthorized,
            "with ForwardedHeaders enabled the allowlist must see the forwarded client IP " +
            "({0}), not the proxy's — a 401 here means every Comgate callback is rejected " +
            "in the deployed environments", ComgateIp);
    }

    [Fact]
    public async Task Forwarded_Client_Ip_Is_Ignored_When_Disabled()
    {
        using var factory = BuildPublicHost(forwardedHeadersEnabled: false);
        using var client = factory.CreateClient();

        var outcome = await PostWebhookAsync(client, forwardedFor: ComgateIp);

        outcome.Should().Be(
            HttpStatusCode.Unauthorized,
            "X-Forwarded-For must NOT be trusted unless ForwardedHeaders:Enabled is set — " +
            "otherwise any caller could forge a source IP straight past the allowlist");
    }

    /// <summary>
    /// The allowlist is fail-closed by contract; this pins that the forwarded
    /// path does not accidentally weaken it into an allow-all.
    /// </summary>
    [Fact]
    public async Task Forwarded_Ip_Outside_The_Allowlist_Is_Still_Rejected()
    {
        using var factory = BuildPublicHost(forwardedHeadersEnabled: true);
        using var client = factory.CreateClient();

        var outcome = await PostWebhookAsync(client, forwardedFor: "198.51.100.77");

        outcome.Should().Be(
            HttpStatusCode.Unauthorized,
            "honouring X-Forwarded-For must not turn the allowlist into an allow-all");
    }

    /// <summary>
    /// The single most important assertion in this file.
    ///
    /// <para>
    /// <c>ForwardLimit = 1</c> is the ONLY thing stopping an internet caller
    /// from choosing the address the allowlist sees. The middleware consumes
    /// <c>X-Forwarded-For</c> right-to-left and the App Service front end
    /// appends the real socket peer last, so a forged leftmost entry must be
    /// ignored. Raise <c>ForwardLimit</c> without adding a real proxy hop and
    /// forging an allowlisted source IP becomes trivial — every other test in
    /// this file would still pass, because they all send a single entry.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Leftmost_Forged_Entry_Is_Not_Honoured()
    {
        using var factory = BuildPublicHost(forwardedHeadersEnabled: true);
        using var client = factory.CreateClient();

        // What an attacker sending "X-Forwarded-For: <allowlisted>" actually
        // produces once the front end appends their real address.
        var outcome = await PostWebhookAsync(
            client, forwardedFor: $"{ComgateIp}, 198.51.100.77");

        outcome.Should().Be(
            HttpStatusCode.Unauthorized,
            "only the rightmost X-Forwarded-For entry (the hop the proxy itself " +
            "appended) may be trusted; honouring the client-supplied leftmost " +
            "entry would let anyone forge an allowlisted source IP");
    }

    /// <summary>
    /// Posts the webhook and reports how far the request got.
    ///
    /// <para>
    /// Returns <see cref="HttpStatusCode.Unauthorized"/> when the allowlist
    /// filter rejected the caller, and <c>null</c> when the request got PAST
    /// the filter into the handler and something downstream threw. Both are
    /// valid outcomes for this test: the filter is what is under test, and
    /// anything beyond it — a refused connection to the stubbed gateway, a
    /// downstream fault — is equally proof that the IP check passed.
    /// </para>
    ///
    /// <para>
    /// A downstream fault surfaces as a thrown exception rather than a 500
    /// because the backend has no global exception handler; that is a separate,
    /// tracked gap and deliberately not worked around here.
    /// </para>
    /// </summary>
    private static async Task<HttpStatusCode?> PostWebhookAsync(HttpClient client, string forwardedFor)
    {
        // Body shape is irrelevant — the filter is an IAuthorizationFilter and
        // runs before model binding, so a rejected source never gets this far.
        var content = new StringContent("transId=stub&refId=stub");
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");

        using var request = new HttpRequestMessage(HttpMethod.Post, WebhookPath) { Content = content };
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", forwardedFor);

        try
        {
            using var response = await client.SendAsync(request);
            return response.StatusCode;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static WebApplicationFactory<Makables.Web.Public.Program> BuildPublicHost(
        bool forwardedHeadersEnabled)
        => HostStartupHarness.Build<Makables.Web.Public.Program>()
            .WithWebHostBuilder(builder =>
                builder.ConfigureAppConfiguration((_, config) =>
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ForwardedHeaders:Enabled"] = forwardedHeadersEnabled ? "true" : "false",
                        ["Comgate:WebhookAllowedIps:0"] = ComgateIp,
                        // Defence in depth only — see the class remarks:
                        // the request dies before any gateway call. Scheme
                        // must be https, ComgateOptions validation rejects
                        // anything else at startup.
                        ["Comgate:BaseUrl"] = "https://127.0.0.1:9",
                    })));
}
