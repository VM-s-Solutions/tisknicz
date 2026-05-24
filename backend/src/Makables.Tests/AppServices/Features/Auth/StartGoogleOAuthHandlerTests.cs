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
        _stateSigner.Sign(Arg.Any<OAuthStatePayload>()).Returns("signed-state-x");
        _googleClient.BuildAuthorizationUrl("signed-state-x", "https://makables.cz/auth/google/callback")
            .Returns("https://accounts.google.com/o/oauth2/v2/auth?state=signed-state-x");
        _handler = new StartGoogleOAuth.Handler(_stateSigner, _googleClient, _ids, _clock,
            NullLogger<StartGoogleOAuth.Handler>.Instance);
    }

    [Theory]
    [InlineData(MakablesAudiences.Customer)]
    [InlineData(MakablesAudiences.Maker)]
    public async Task Returns_authorization_url_with_signed_state_for_customer_and_maker(string audience)
    {
        var result = await _handler.Handle(
            new StartGoogleOAuth.Command(audience, "https://makables.cz/auth/google/callback"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.AuthorizationUrl.Should().Contain("state=signed-state-x");
        _stateSigner.Received(1).Sign(Arg.Is<OAuthStatePayload>(p =>
            p.Audience == audience && p.Nonce == "nonce-fixed" && p.IssuedAt == _clock.UtcNow));
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
        _stateSigner.DidNotReceive().Sign(Arg.Any<OAuthStatePayload>());
    }
}
