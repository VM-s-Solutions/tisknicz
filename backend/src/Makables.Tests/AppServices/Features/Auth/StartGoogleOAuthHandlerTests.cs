using FluentAssertions;
using Makables.Core.AppServices.Common;
using Makables.Core.AppServices.Features.Auth;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using Makables.TestUtilities;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.Auth;

public class StartGoogleOAuthHandlerTests
{
    private readonly IOAuthStateSigner _stateSigner = Substitute.For<IOAuthStateSigner>();
    private readonly IGoogleOAuthClient _googleClient = Substitute.For<IGoogleOAuthClient>();
    private readonly IIdGenerator _ids = Substitute.For<IIdGenerator>();
    private readonly FakeClock _clock = new();
    private readonly StartGoogleOAuth.Handler _handler;

    public StartGoogleOAuthHandlerTests()
    {
        _ids.Next().Returns("nonce-fixed");
        _stateSigner.Sign(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<DateTimeOffset>())
            .Returns("signed-state-x");
        _googleClient.BuildAuthorizationUrl("signed-state-x", "https://makables.cz/auth/google/callback")
            .Returns("https://accounts.google.com/o/oauth2/v2/auth?state=signed-state-x");
        _handler = new StartGoogleOAuth.Handler(_stateSigner, _googleClient, _ids, _clock,
            NullLogger<StartGoogleOAuth.Handler>.Instance);
    }

    [Theory]
    [InlineData(MakablesAudiences.Customer)]
    [InlineData(MakablesAudiences.Maker)]
    public async Task Returns_authorization_url_AND_a_fresh_csrf_cookie_value(string audience)
    {
        var result = await _handler.Handle(
            new StartGoogleOAuth.Command(audience, "https://makables.cz/auth/google/callback"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.AuthorizationUrl.Should().Contain("state=signed-state-x");
        result.Value.CsrfCookieValue.Should().NotBeNullOrWhiteSpace();
        // 32 bytes of CSPRNG -> 43 base64url chars without padding.
        result.Value.CsrfCookieValue.Length.Should().Be(43);

        _stateSigner.Received(1).Sign(
            audience,
            "https://makables.cz/auth/google/callback",
            result.Value.CsrfCookieValue,
            "nonce-fixed",
            _clock.UtcNow);
    }

    [Fact]
    public async Task Each_invocation_produces_a_different_csrf_cookie_value()
    {
        var a = await _handler.Handle(new StartGoogleOAuth.Command(
            MakablesAudiences.Customer, "https://makables.cz/cb"), CancellationToken.None);
        var b = await _handler.Handle(new StartGoogleOAuth.Command(
            MakablesAudiences.Customer, "https://makables.cz/cb"), CancellationToken.None);

        a.Value!.CsrfCookieValue.Should().NotBe(b.Value!.CsrfCookieValue);
    }

    [Fact]
    public async Task Rejects_admin_audience_to_prevent_escalation_via_OAuth()
    {
        var result = await _handler.Handle(
            new StartGoogleOAuth.Command(MakablesAudiences.Admin, "https://x.cz/cb"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.AuthOAuthNotAllowedForAdmin);
        result.Error.Type.Should().Be(ErrorType.Forbidden);
        _stateSigner.DidNotReceive().Sign(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<DateTimeOffset>());
    }
}
