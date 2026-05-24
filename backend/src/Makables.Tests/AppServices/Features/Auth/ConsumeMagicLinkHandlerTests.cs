using FluentAssertions;
using Makables.Core.AppServices.Common;
using Makables.Core.AppServices.Features.Auth;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.Auth;

public class ConsumeMagicLinkHandlerTests
{
    private readonly IOneTimeTokenRepository _tokens = Substitute.For<IOneTimeTokenRepository>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IRefreshTokenRepository _refreshTokens = Substitute.For<IRefreshTokenRepository>();
    private readonly IJwtIssuer _jwt = Substitute.For<IJwtIssuer>();
    private readonly IIdGenerator _ids = Substitute.For<IIdGenerator>();
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 5, 24, 12, 0, 0, TimeSpan.Zero));
    private readonly ConsumeMagicLink.Handler _handler;

    public ConsumeMagicLinkHandlerTests()
    {
        _ids.Next().Returns(_ => Ulid.NewUlid().ToString());
        _jwt.Issue(Arg.Any<User>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>())
            .Returns(c => new AccessToken("access.jwt", c.Arg<DateTimeOffset>() + TimeSpan.FromMinutes(15), "jti"));
        _handler = new ConsumeMagicLink.Handler(_tokens, _users, _refreshTokens, _jwt, _ids, _clock,
            NullLogger<ConsumeMagicLink.Handler>.Instance);
    }

    private static User CreateUser(UserRole role = UserRole.Customer, bool confirmed = false)
    {
        var u = User.Create("user-1", "anna@example.cz", role, "Anna", "CZ", "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
        if (confirmed) u.ConfirmEmail(DateTimeOffset.UtcNow);
        return u;
    }

    private static OneTimeToken IssueRedeemable(
        DateTimeOffset now,
        OneTimeTokenPurpose purpose = OneTimeTokenPurpose.MagicLink) =>
        OneTimeToken.Issue("hash", "user-1", purpose, now + TimeSpan.FromMinutes(15), now);

    [Fact]
    public async Task Returns_invalid_when_token_is_unknown()
    {
        _tokens.GetByHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((OneTimeToken?)null);

        var result = await _handler.Handle(
            new ConsumeMagicLink.Command("raw-not-real", "customer", null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.AuthMagicLinkInvalid);
    }

    [Fact]
    public async Task Returns_invalid_when_token_purpose_is_wrong()
    {
        var token = IssueRedeemable(_clock.UtcNow, OneTimeTokenPurpose.PasswordReset);
        _tokens.GetByHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(token);

        var result = await _handler.Handle(
            new ConsumeMagicLink.Command("raw", "customer", null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.AuthMagicLinkInvalid);
        // Wrong-purpose tokens MUST NOT be silently consumed.
        token.IsConsumed.Should().BeFalse();
    }

    [Fact]
    public async Task Returns_invalid_when_token_already_consumed()
    {
        var token = IssueRedeemable(_clock.UtcNow);
        token.Consume(_clock.UtcNow.AddMinutes(-1));
        _tokens.GetByHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(token);

        var result = await _handler.Handle(
            new ConsumeMagicLink.Command("raw", "customer", null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.AuthMagicLinkInvalid);
    }

    [Fact]
    public async Task Returns_invalid_when_token_expired()
    {
        var token = OneTimeToken.Issue("hash", "user-1", OneTimeTokenPurpose.MagicLink,
            _clock.UtcNow.AddMinutes(-1), _clock.UtcNow.AddMinutes(-16));
        _tokens.GetByHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(token);

        var result = await _handler.Handle(
            new ConsumeMagicLink.Command("raw", "customer", null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.AuthMagicLinkInvalid);
    }

    [Fact]
    public async Task Soft_deleted_user_returns_invalid_AND_burns_the_token()
    {
        var token = IssueRedeemable(_clock.UtcNow);
        _tokens.GetByHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(token);
        var user = CreateUser();
        user.MarkDeactivated("admin", _clock.UtcNow.AddMinutes(-10));
        _users.GetByIdAsync("user-1", Arg.Any<CancellationToken>()).Returns(user);

        var result = await _handler.Handle(
            new ConsumeMagicLink.Command("raw", "customer", null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.AuthMagicLinkInvalid);
        token.IsConsumed.Should().BeTrue("a stolen valid link must not be replayable after reactivation");
    }

    [Fact]
    public async Task Audience_mismatch_for_non_admin_returns_forbidden_AND_burns_the_token()
    {
        var token = IssueRedeemable(_clock.UtcNow);
        _tokens.GetByHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(token);
        _users.GetByIdAsync("user-1", Arg.Any<CancellationToken>()).Returns(CreateUser(UserRole.Customer));

        var result = await _handler.Handle(
            new ConsumeMagicLink.Command("raw", "maker", null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.Forbidden);
        token.IsConsumed.Should().BeTrue("a link targeted at the wrong audience burns on first attempt");
    }

    [Fact]
    public async Task Happy_path_consumes_token_confirms_email_and_issues_session()
    {
        var token = IssueRedeemable(_clock.UtcNow);
        _tokens.GetByHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(token);
        var user = CreateUser(confirmed: false);
        _users.GetByIdAsync("user-1", Arg.Any<CancellationToken>()).Returns(user);

        var result = await _handler.Handle(
            new ConsumeMagicLink.Command("raw", "customer", "ua", "1.2.3.4"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.AccessToken.Should().Be("access.jwt");
        result.Value.RefreshToken.Should().NotBeNullOrWhiteSpace();
        token.IsConsumed.Should().BeTrue();
        user.EmailConfirmedAt.Should().NotBeNull("successful magic-link redemption proves inbox control");
        _refreshTokens.Received(1).Add(Arg.Any<RefreshToken>());
    }

    private sealed class FakeClock(DateTimeOffset now) : IClock { public DateTimeOffset UtcNow { get; } = now; }
}
