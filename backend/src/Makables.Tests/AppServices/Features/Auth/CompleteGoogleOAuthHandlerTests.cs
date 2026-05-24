using FluentAssertions;
using Makables.Core.AppServices.Features.Auth;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using Makables.TestUtilities;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Makables.Tests.AppServices.Features.Auth;

public class CompleteGoogleOAuthHandlerTests
{
    private const string RedirectUri = "https://makables.cz/auth/google/callback";
    private const string CsrfCookie = "csrf-value-1";

    private readonly IOAuthStateSigner _stateSigner = Substitute.For<IOAuthStateSigner>();
    private readonly IGoogleOAuthClient _googleClient = Substitute.For<IGoogleOAuthClient>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IRefreshTokenRepository _refreshTokens = Substitute.For<IRefreshTokenRepository>();
    private readonly IJwtIssuer _jwt = Substitute.For<IJwtIssuer>();
    private readonly IIdGenerator _ids = Substitute.For<IIdGenerator>();
    private readonly FakeClock _clock = new();
    private readonly CompleteGoogleOAuth.Handler _handler;

    public CompleteGoogleOAuthHandlerTests()
    {
        _ids.Next().Returns(_ => Ulid.NewUlid().ToString());
        _jwt.Issue(Arg.Any<User>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>())
            .Returns(c => new AccessToken("access.jwt", c.Arg<DateTimeOffset>() + TimeSpan.FromMinutes(15), "jti"));
        _handler = new CompleteGoogleOAuth.Handler(
            _stateSigner, _googleClient, _users, _refreshTokens, _jwt, _ids, _clock,
            Options.Create(new AuthDefaultCountryOptions { CountryCodePrimary = "CZ" }),
            NullLogger<CompleteGoogleOAuth.Handler>.Instance);
    }

    private CompleteGoogleOAuth.Command Cmd(string state = "state", string code = "code") =>
        new(code, state, RedirectUri, CsrfCookie, "ua", "1.2.3.4");

    private OAuthStatePayload PayloadFor(string audience) =>
        new(audience, RedirectUri,
            CsrfCookieHash: "ignored-by-substitute",
            Nonce: "n",
            IssuedAt: _clock.UtcNow);

    [Fact]
    public async Task Returns_invalid_state_when_signer_rejects_the_state()
    {
        _stateSigner.TryVerify(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>())
            .Returns((OAuthStatePayload?)null);

        var result = await _handler.Handle(Cmd(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.AuthOAuthInvalidState);
        await _googleClient.DidNotReceive().ExchangeCodeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Forwards_redirect_uri_and_csrf_cookie_to_the_signer()
    {
        _stateSigner.TryVerify(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>())
            .Returns((OAuthStatePayload?)null);

        await _handler.Handle(Cmd(), CancellationToken.None);

        _stateSigner.Received(1).TryVerify("state", RedirectUri, CsrfCookie, _clock.UtcNow);
    }

    [Fact]
    public async Task Rejects_admin_audience_even_with_a_valid_signed_state()
    {
        _stateSigner.TryVerify(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>())
            .Returns(PayloadFor(MakablesAudiences.Admin));

        var result = await _handler.Handle(Cmd(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.AuthOAuthNotAllowedForAdmin);
        await _googleClient.DidNotReceive().ExchangeCodeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Returns_exchange_failed_when_Google_throws_HttpRequestException()
    {
        _stateSigner.TryVerify(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>())
            .Returns(PayloadFor(MakablesAudiences.Customer));
        _googleClient.ExchangeCodeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(new HttpRequestException("Google said no."));

        var result = await _handler.Handle(Cmd(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.AuthOAuthExchangeFailed);
    }

    [Fact]
    public async Task Rethrows_OperationCanceledException_on_caller_cancellation()
    {
        // Reviewer T-0026 M-3: catch-all swallowing a user-cancellation
        // would mask the disconnect. Cancel-on-token should surface to
        // the framework.
        _stateSigner.TryVerify(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>())
            .Returns(PayloadFor(MakablesAudiences.Customer));
        _googleClient.ExchangeCodeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(new OperationCanceledException());

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => _handler.Handle(Cmd(), cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Rejects_profile_without_verified_email()
    {
        _stateSigner.TryVerify(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>())
            .Returns(PayloadFor(MakablesAudiences.Customer));
        _googleClient.ExchangeCodeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new GoogleProfile("sub-1", "anna@example.cz", EmailVerified: false, Name: "Anna"));

        var result = await _handler.Handle(Cmd(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.AuthOAuthEmailNotVerified);
    }

    [Fact]
    public async Task Existing_user_matched_by_GoogleSub_logs_in_directly()
    {
        var existing = User.Create("user-1", "anna@example.cz", UserRole.Customer, "Anna", "CZ",
            passwordHash: null, googleSub: "sub-1", emailAlreadyConfirmed: true);
        _stateSigner.TryVerify(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>())
            .Returns(PayloadFor(MakablesAudiences.Customer));
        _googleClient.ExchangeCodeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new GoogleProfile("sub-1", "anna@example.cz", true, "Anna"));
        _users.GetByGoogleSubAsync("sub-1", Arg.Any<CancellationToken>()).Returns(existing);

        var result = await _handler.Handle(Cmd(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _users.DidNotReceive().Add(Arg.Any<User>());
        _refreshTokens.Received(1).Add(Arg.Any<RefreshToken>());
    }

    [Fact]
    public async Task Password_user_with_same_email_gets_GoogleSub_linked_and_confirmed()
    {
        var existing = User.Create("user-1", "anna@example.cz", UserRole.Customer, "Anna", "CZ",
            passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
        _stateSigner.TryVerify(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>())
            .Returns(PayloadFor(MakablesAudiences.Customer));
        _googleClient.ExchangeCodeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new GoogleProfile("sub-1", "anna@example.cz", true, "Anna"));
        _users.GetByGoogleSubAsync("sub-1", Arg.Any<CancellationToken>()).Returns((User?)null);
        _users.GetByEmailNormalizedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(existing);

        var result = await _handler.Handle(Cmd(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        existing.GoogleSub.Should().Be("sub-1");
        existing.EmailConfirmedAt.Should().NotBeNull();
        _users.DidNotReceive().Add(Arg.Any<User>());
    }

    [Fact]
    public async Task Brand_new_email_creates_user_with_role_from_signed_audience_AND_country_from_config()
    {
        // Reviewer T-0026 code-quality M-1: country is now config-driven
        // (was hardcoded "CZ"). The default options bind "CZ" so the
        // observable behavior is unchanged.
        _stateSigner.TryVerify(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>())
            .Returns(PayloadFor(MakablesAudiences.Maker));
        _googleClient.ExchangeCodeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new GoogleProfile("sub-9", "fresh@example.cz", true, "Fresh User"));
        _users.GetByGoogleSubAsync("sub-9", Arg.Any<CancellationToken>()).Returns((User?)null);
        _users.GetByEmailNormalizedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((User?)null);

        var result = await _handler.Handle(Cmd(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _users.Received(1).Add(Arg.Is<User>(u =>
            u.GoogleSub == "sub-9" &&
            u.Role == UserRole.Maker &&
            u.CountryCodePrimary == "CZ" &&
            u.EmailConfirmedAt != null));
    }

    [Fact]
    public async Task Soft_deleted_user_by_GoogleSub_is_refused_with_exchange_failed()
    {
        var soft = User.Create("user-1", "anna@example.cz", UserRole.Customer, "Anna", "CZ",
            passwordHash: null, googleSub: "sub-1", emailAlreadyConfirmed: true);
        soft.MarkDeactivated("admin", _clock.UtcNow.AddDays(-30));
        _stateSigner.TryVerify(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>())
            .Returns(PayloadFor(MakablesAudiences.Customer));
        _googleClient.ExchangeCodeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new GoogleProfile("sub-1", "anna@example.cz", true, "Anna"));
        _users.GetByGoogleSubAsync("sub-1", Arg.Any<CancellationToken>()).Returns(soft);

        var result = await _handler.Handle(Cmd(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.AuthOAuthExchangeFailed);
        _refreshTokens.DidNotReceive().Add(Arg.Any<RefreshToken>());
    }

    [Fact]
    public async Task Existing_customer_cannot_log_in_via_the_maker_audience()
    {
        var existing = User.Create("user-1", "anna@example.cz", UserRole.Customer, "Anna", "CZ",
            passwordHash: null, googleSub: "sub-1", emailAlreadyConfirmed: true);
        _stateSigner.TryVerify(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>())
            .Returns(PayloadFor(MakablesAudiences.Maker));
        _googleClient.ExchangeCodeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new GoogleProfile("sub-1", "anna@example.cz", true, "Anna"));
        _users.GetByGoogleSubAsync("sub-1", Arg.Any<CancellationToken>()).Returns(existing);

        var result = await _handler.Handle(Cmd(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.Forbidden);
        _refreshTokens.DidNotReceive().Add(Arg.Any<RefreshToken>());
    }
}
