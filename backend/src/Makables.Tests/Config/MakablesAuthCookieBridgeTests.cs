using FluentAssertions;
using Makables.Config.Extensions;
using Microsoft.AspNetCore.Http;

namespace Makables.Tests.Config;

/// <summary>
/// Pins the T-0156 cookie → JWT bridge
/// (<see cref="MakablesAuthExtensions.ResolveTokenFromCookies"/>): the
/// HttpOnly access cookie authenticates browser sessions when no
/// <c>Authorization</c> header is present; an explicit Bearer header
/// always wins; audiences probe in the host's accepted order (own
/// audience before admin).
/// </summary>
public class MakablesAuthCookieBridgeTests
{
    private static readonly string[] CustomerHostAudiences = ["customer", "admin"];
    private static readonly string[] MakerHostAudiences = ["maker", "admin"];
    private static readonly string[] PublicHostAudiences = ["customer", "maker", "admin"];

    private static HttpRequest RequestWith(string? cookieHeader = null, string? authorization = null)
    {
        var context = new DefaultHttpContext();
        if (cookieHeader is not null) context.Request.Headers.Cookie = cookieHeader;
        if (authorization is not null) context.Request.Headers.Authorization = authorization;
        return context.Request;
    }

    [Fact]
    public void Reads_the_access_cookie_when_no_Authorization_header_is_present()
    {
        var request = RequestWith(cookieHeader: "makables_access_customer=jwt-customer");

        var token = MakablesAuthExtensions.ResolveTokenFromCookies(request, CustomerHostAudiences);

        token.Should().Be("jwt-customer");
    }

    [Fact]
    public void Authorization_header_always_wins_over_cookies()
    {
        var request = RequestWith(
            cookieHeader: "makables_access_customer=jwt-customer",
            authorization: "Bearer explicit-token");

        var token = MakablesAuthExtensions.ResolveTokenFromCookies(request, CustomerHostAudiences);

        token.Should().BeNull("the default header path must stay in charge");
    }

    [Fact]
    public void Returns_null_when_no_accepted_cookie_exists()
    {
        // A maker cookie on the customer host is NOT an accepted audience —
        // the compile-time audience isolation must survive the bridge.
        var request = RequestWith(cookieHeader: "makables_access_maker=jwt-maker");

        var token = MakablesAuthExtensions.ResolveTokenFromCookies(request, CustomerHostAudiences);

        token.Should().BeNull();
    }

    [Fact]
    public void Probes_audiences_in_host_order_so_own_audience_beats_admin()
    {
        var request = RequestWith(
            cookieHeader: "makables_access_admin=jwt-admin; makables_access_maker=jwt-maker");

        var token = MakablesAuthExtensions.ResolveTokenFromCookies(request, MakerHostAudiences);

        token.Should().Be("jwt-maker");
    }

    [Fact]
    public void Public_host_probes_customer_then_maker_then_admin()
    {
        var request = RequestWith(
            cookieHeader: "makables_access_maker=jwt-maker; makables_access_admin=jwt-admin");

        var token = MakablesAuthExtensions.ResolveTokenFromCookies(request, PublicHostAudiences);

        token.Should().Be("jwt-maker");
    }

    [Fact]
    public void Skips_empty_cookie_values()
    {
        var request = RequestWith(
            cookieHeader: "makables_access_customer=; makables_access_admin=jwt-admin");

        var token = MakablesAuthExtensions.ResolveTokenFromCookies(request, CustomerHostAudiences);

        token.Should().Be("jwt-admin");
    }

    [Fact]
    public void Returns_null_on_a_bare_request()
    {
        var token = MakablesAuthExtensions.ResolveTokenFromCookies(
            RequestWith(), CustomerHostAudiences);

        token.Should().BeNull();
    }
}
