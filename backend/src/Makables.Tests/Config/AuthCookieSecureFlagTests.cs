using FluentAssertions;
using Makables.Config.Auth;
using Makables.Core.AppServices.Features.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace Makables.Tests.Config;

/// <summary>
/// Pins the environment gate on the <c>Secure</c> attribute of the session
/// cookies (<see cref="AuthCookies.SetSessionCookies"/>).
///
/// <para>
/// Safari refuses to store a <c>Secure</c> cookie on a plain
/// <c>http://localhost</c> origin, so with the attribute unconditionally
/// set the local dev login answered <c>200</c> while the session cookie
/// was silently dropped — the app stayed logged out and the developer
/// re-submitted the login form over and over. Chrome and Firefox treat
/// localhost as a trustworthy origin and stored it either way, which is
/// why this only ever reproduced in Safari.
/// </para>
///
/// <para>
/// The relaxation MUST be unreachable outside Development (CLAUDE.md §6),
/// including the reverse-proxy case where the inner hop is plain HTTP and
/// <see cref="HttpRequest.IsHttps"/> is therefore <c>false</c>.
/// </para>
/// </summary>
public class AuthCookieSecureFlagTests
{
    private static readonly SessionResult Session = new(
        UserId: "user-1",
        AccessToken: "access-token",
        AccessTokenExpiresAt: new DateTimeOffset(2026, 8, 18, 12, 0, 0, TimeSpan.Zero),
        RefreshToken: "refresh-token",
        RefreshTokenExpiresAt: new DateTimeOffset(2026, 9, 17, 12, 0, 0, TimeSpan.Zero));

    private static DefaultHttpContext ContextFor(string? environmentName, bool isHttps)
    {
        var services = new ServiceCollection();
        if (environmentName is not null)
        {
            var environment = Substitute.For<IHostEnvironment>();
            environment.EnvironmentName.Returns(environmentName);
            services.AddSingleton(environment);
        }

        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        context.Request.IsHttps = isHttps;
        return context;
    }

    private static string[] SetCookiesFor(string? environmentName, bool isHttps)
    {
        var context = ContextFor(environmentName, isHttps);
        AuthCookies.SetSessionCookies(context.Response, "customer", Session);
        return context.Response.Headers.SetCookie.ToArray()!;
    }

    [Fact]
    public void Development_over_plain_http_omits_Secure_so_Safari_stores_the_session()
    {
        var cookies = SetCookiesFor("Development", isHttps: false);

        cookies.Should().HaveCount(2);
        cookies.Should().OnlyContain(c => !c.Contains("secure", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Development_over_https_keeps_Secure()
    {
        var cookies = SetCookiesFor("Development", isHttps: true);

        cookies.Should().OnlyContain(c => c.Contains("secure", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The production-safety case: a TLS-terminating reverse proxy forwards
    /// the inner hop as plain HTTP, so <c>IsHttps</c> is false. The cookie
    /// must STILL be Secure — the environment check has to dominate.
    /// </summary>
    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("IntegrationTest")]
    public void Non_development_always_keeps_Secure_even_on_a_plain_http_hop(string environmentName)
    {
        var cookies = SetCookiesFor(environmentName, isHttps: false);

        cookies.Should().OnlyContain(c => c.Contains("secure", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Fails_closed_to_Secure_when_no_IHostEnvironment_is_registered()
    {
        var cookies = SetCookiesFor(environmentName: null, isHttps: false);

        cookies.Should().OnlyContain(c => c.Contains("secure", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void HttpOnly_and_SameSite_Strict_are_never_relaxed()
    {
        var cookies = SetCookiesFor("Development", isHttps: false);

        cookies.Should().OnlyContain(c => c.Contains("httponly", StringComparison.OrdinalIgnoreCase));
        cookies.Should().OnlyContain(c => c.Contains("samesite=strict", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// A deletion cookie only overwrites the original when its attributes
    /// match, so the clear path must follow the very same gate — otherwise
    /// logout would leave the dev session cookie in place.
    /// </summary>
    [Fact]
    public void Clearing_the_session_uses_the_same_gate_as_setting_it()
    {
        var devContext = ContextFor("Development", isHttps: false);
        AuthCookies.ClearSessionCookies(devContext.Response, "customer");
        devContext.Response.Headers.SetCookie.ToArray()
            .Should().OnlyContain(c => !c!.Contains("secure", StringComparison.OrdinalIgnoreCase));

        var prodContext = ContextFor("Production", isHttps: false);
        AuthCookies.ClearSessionCookies(prodContext.Response, "customer");
        prodContext.Response.Headers.SetCookie.ToArray()
            .Should().OnlyContain(c => c!.Contains("secure", StringComparison.OrdinalIgnoreCase));
    }
}
