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

public class CompleteAppleOAuthHandlerTests
{
    private const string RedirectUri = "https://makables.cz/auth/apple/callback";
    private const string CsrfCookie = "csrf-value-1";

    private readonly IOAuthStateSigner _stateSigner = Substitute.For<IOAuthStateSigner>();
    private readonly IAppleOAuthClient _appleClient = Substitute.For<IAppleOAuthClient>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IRefreshTokenRepository _refreshTokens = Substitute.For<IRefreshTokenRepository>();
    private readonly IJwtIssuer _jwt = Substitute.For<IJwtIssuer>();
    private readonly IIdGenerator _ids = Substitute.For<IIdGenerator>();
    private readonly FakeClock _clock = new();
    private readonly CompleteAppleOAuth.Handler _handler;

    public CompleteAppleOAuthHandlerTests()
    {
        _ids.Next().Returns(_ => Ulid.NewUlid().ToString());
        _jwt.Issue(Arg.Any<User>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>())
            .Returns(c => new AccessToken("access.jwt", c.Arg<DateTimeOffset>() + TimeSpan.FromMinutes(15), "jti"));
        _handler = new CompleteAppleOAuth.Handler(
            _stateSigner, _appleClient, _users, _refreshTokens, _jwt, _ids, _clock,
            Options.Create(new AuthDefaultCountryOptions { CountryCodePrimary = "CZ" }),
            NullLogger<CompleteAppleOAuth.Handler>.Instance);
    }

    private CompleteAppleOAuth.Command Cmd(string state = "state", string code = "code", string? userFieldJson = null) =>
        new(code, state, RedirectUri, CsrfCookie, userFieldJson, "ua", "1.2.3.4");

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
        await _appleClient.DidNotReceive().ExchangeCodeAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Rejects_admin_audience_even_with_a_valid_signed_state()
    {
        _stateSigner.TryVerify(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>())
            .Returns(PayloadFor(MakablesAudiences.Admin));

        var result = await _handler.Handle(Cmd(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.AuthOAuthNotAllowedForAdmin);
        await _appleClient.DidNotReceive().ExchangeCodeAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Returns_exchange_failed_when_Apple_throws_HttpRequestException()
    {
        _stateSigner.TryVerify(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>())
            .Returns(PayloadFor(MakablesAudiences.Customer));
        _appleClient.ExchangeCodeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Throws(new HttpRequestException("Apple said no."));

        var result = await _handler.Handle(Cmd(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.AuthOAuthExchangeFailed);
    }

    [Fact]
    public async Task Rethrows_OperationCanceledException_on_caller_cancellation()
    {
        _stateSigner.TryVerify(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>())
            .Returns(PayloadFor(MakablesAudiences.Customer));
        _appleClient.ExchangeCodeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
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
        _appleClient.ExchangeCodeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new AppleProfile("sub-1", "anna@example.cz", EmailVerified: false, Name: "Anna", IsPrivateEmail: false));

        var result = await _handler.Handle(Cmd(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.AuthOAuthEmailNotVerified);
    }

    [Fact]
    public async Task Existing_user_matched_by_AppleSub_logs_in_directly_without_name_field()
    {
        // AC-7: repeat login carries no `user` field — resolution must
        // succeed on AppleSub match alone.
        var existing = User.Create("user-1", "anna@example.cz", UserRole.Customer, "Anna", "CZ",
            passwordHash: null);
        existing.LinkAppleSub("sub-1");
        _stateSigner.TryVerify(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>())
            .Returns(PayloadFor(MakablesAudiences.Customer));
        _appleClient.ExchangeCodeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new AppleProfile("sub-1", "anna@example.cz", true, Name: null, IsPrivateEmail: false));
        _users.GetByAppleSubAsync("sub-1", Arg.Any<CancellationToken>()).Returns(existing);

        var result = await _handler.Handle(Cmd(userFieldJson: null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _users.DidNotReceive().Add(Arg.Any<User>());
        _refreshTokens.Received(1).Add(Arg.Any<RefreshToken>());
        existing.FullName.Should().Be("Anna"); // untouched — name never overwritten on match-by-sub
    }

    [Fact]
    public async Task Password_user_with_same_email_gets_AppleSub_linked_and_confirmed()
    {
        var existing = User.Create("user-1", "anna@example.cz", UserRole.Customer, "Anna", "CZ",
            passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
        _stateSigner.TryVerify(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>())
            .Returns(PayloadFor(MakablesAudiences.Customer));
        _appleClient.ExchangeCodeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new AppleProfile("sub-1", "anna@example.cz", true, Name: "Anna", IsPrivateEmail: false));
        _users.GetByAppleSubAsync("sub-1", Arg.Any<CancellationToken>()).Returns((User?)null);
        _users.GetByEmailNormalizedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(existing);

        var result = await _handler.Handle(Cmd(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        existing.AppleSub.Should().Be("sub-1");
        existing.EmailConfirmedAt.Should().NotBeNull();
        existing.FullName.Should().Be("Anna"); // pre-existing name untouched, not overwritten
        _users.DidNotReceive().Add(Arg.Any<User>());
    }

    [Fact]
    public async Task Brand_new_email_with_user_field_creates_user_with_name_role_and_country()
    {
        // AC-6: first authorization — `user` field name is used at creation time.
        _stateSigner.TryVerify(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>())
            .Returns(PayloadFor(MakablesAudiences.Maker));
        _appleClient.ExchangeCodeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new AppleProfile("sub-9", "fresh@example.cz", true, Name: "Fresh User", IsPrivateEmail: false));
        _users.GetByAppleSubAsync("sub-9", Arg.Any<CancellationToken>()).Returns((User?)null);
        _users.GetByEmailNormalizedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((User?)null);

        var result = await _handler.Handle(Cmd(userFieldJson: """{"name":{"firstName":"Fresh","lastName":"User"}}"""), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _users.Received(1).Add(Arg.Is<User>(u =>
            u.AppleSub == "sub-9" &&
            u.Role == UserRole.Maker &&
            u.CountryCodePrimary == "CZ" &&
            u.FullName == "Fresh User" &&
            u.EmailConfirmedAt != null));
    }

    [Fact]
    public async Task Brand_new_email_without_user_field_falls_back_to_email_as_full_name()
    {
        // AC-6 without-name path: Apple sent no `user` field at all
        // (declined name sharing) — falls back to the email.
        _stateSigner.TryVerify(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>())
            .Returns(PayloadFor(MakablesAudiences.Customer));
        _appleClient.ExchangeCodeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new AppleProfile("sub-10", "noname@example.cz", true, Name: null, IsPrivateEmail: false));
        _users.GetByAppleSubAsync("sub-10", Arg.Any<CancellationToken>()).Returns((User?)null);
        _users.GetByEmailNormalizedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((User?)null);

        var result = await _handler.Handle(Cmd(userFieldJson: null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _users.Received(1).Add(Arg.Is<User>(u => u.FullName == "noname@example.cz"));
    }

    [Fact]
    public async Task Soft_deleted_user_by_AppleSub_is_refused_with_exchange_failed()
    {
        var soft = User.Create("user-1", "anna@example.cz", UserRole.Customer, "Anna", "CZ",
            passwordHash: null);
        soft.LinkAppleSub("sub-1");
        soft.MarkDeactivated("admin", _clock.UtcNow.AddDays(-30));
        _stateSigner.TryVerify(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>())
            .Returns(PayloadFor(MakablesAudiences.Customer));
        _appleClient.ExchangeCodeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new AppleProfile("sub-1", "anna@example.cz", true, Name: "Anna", IsPrivateEmail: false));
        _users.GetByAppleSubAsync("sub-1", Arg.Any<CancellationToken>()).Returns(soft);

        var result = await _handler.Handle(Cmd(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.AuthOAuthExchangeFailed);
        _refreshTokens.DidNotReceive().Add(Arg.Any<RefreshToken>());
    }

    [Fact]
    public async Task Existing_customer_cannot_log_in_via_the_maker_audience()
    {
        var existing = User.Create("user-1", "anna@example.cz", UserRole.Customer, "Anna", "CZ",
            passwordHash: null);
        existing.LinkAppleSub("sub-1");
        _stateSigner.TryVerify(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>())
            .Returns(PayloadFor(MakablesAudiences.Maker));
        _appleClient.ExchangeCodeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new AppleProfile("sub-1", "anna@example.cz", true, Name: "Anna", IsPrivateEmail: false));
        _users.GetByAppleSubAsync("sub-1", Arg.Any<CancellationToken>()).Returns(existing);

        var result = await _handler.Handle(Cmd(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.Forbidden);
        _refreshTokens.DidNotReceive().Add(Arg.Any<RefreshToken>());
    }
}
