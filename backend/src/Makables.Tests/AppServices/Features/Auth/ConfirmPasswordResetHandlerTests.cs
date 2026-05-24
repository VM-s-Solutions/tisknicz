using FluentAssertions;
using Makables.Core.AppServices.Common;
using Makables.Core.AppServices.Features.Auth;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Makables.TestUtilities;

namespace Makables.Tests.AppServices.Features.Auth;

public class ConfirmPasswordResetHandlerTests
{
    private readonly IOneTimeTokenRepository _tokens = Substitute.For<IOneTimeTokenRepository>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IRefreshTokenRepository _refreshTokens = Substitute.For<IRefreshTokenRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 5, 24, 12, 0, 0, TimeSpan.Zero));
    private readonly ConfirmPasswordReset.Handler _handler;

    public ConfirmPasswordResetHandlerTests()
    {
        _hasher.Hash(Arg.Any<string>()).Returns("argon2id$v=19$m=65536,t=3,p=1$NEW$HASH");
        _handler = new ConfirmPasswordReset.Handler(_tokens, _users, _refreshTokens, _hasher, _clock,
            NullLogger<ConfirmPasswordReset.Handler>.Instance);
    }

    private static User CreateUser() =>
        User.Create("user-1", "anna@example.cz", UserRole.Customer, "Anna", "CZ", "argon2id$v=19$m=8192,t=1,p=1$OLD$HASH");

    private static OneTimeToken IssueRedeemable(DateTimeOffset now, OneTimeTokenPurpose purpose = OneTimeTokenPurpose.PasswordReset) =>
        OneTimeToken.Issue("hash", "user-1", purpose, now + TimeSpan.FromHours(1), now);

    [Fact]
    public async Task Returns_invalid_when_token_unknown()
    {
        _tokens.GetByHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((OneTimeToken?)null);

        var result = await _handler.Handle(
            new ConfirmPasswordReset.Command("raw", "newpassword1"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.AuthPasswordResetInvalid);
        await _tokens.DidNotReceive().TryConsumeAsync(Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Returns_invalid_when_token_purpose_is_MagicLink_AND_does_not_claim_it()
    {
        var token = IssueRedeemable(_clock.UtcNow, OneTimeTokenPurpose.MagicLink);
        _tokens.GetByHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(token);

        var result = await _handler.Handle(
            new ConfirmPasswordReset.Command("raw", "newpassword1"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.AuthPasswordResetInvalid);
        await _tokens.DidNotReceive().TryConsumeAsync(Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Lost_race_returns_invalid()
    {
        var token = IssueRedeemable(_clock.UtcNow);
        _tokens.GetByHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(token);
        _tokens.TryConsumeAsync(Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(false);

        var result = await _handler.Handle(
            new ConfirmPasswordReset.Command("raw", "newpassword1"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.AuthPasswordResetInvalid);
        _users.DidNotReceive();
        _refreshTokens.DidNotReceive().Add(Arg.Any<RefreshToken>());
    }

    [Fact]
    public async Task Soft_deleted_user_after_claim_returns_invalid_without_setting_password()
    {
        var token = IssueRedeemable(_clock.UtcNow);
        _tokens.GetByHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(token);
        _tokens.TryConsumeAsync(Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(true);
        var user = CreateUser();
        user.MarkDeactivated("admin", _clock.UtcNow.AddMinutes(-10));
        _users.GetByIdAsync("user-1", Arg.Any<CancellationToken>()).Returns(user);

        var result = await _handler.Handle(
            new ConfirmPasswordReset.Command("raw", "newpassword1"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.AuthPasswordResetInvalid);
        _hasher.DidNotReceive().Hash(Arg.Any<string>());
    }

    [Fact]
    public async Task Happy_path_sets_new_hash_AND_revokes_all_active_refresh_tokens()
    {
        var token = IssueRedeemable(_clock.UtcNow);
        _tokens.GetByHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(token);
        _tokens.TryConsumeAsync(Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(true);
        var user = CreateUser();
        _users.GetByIdAsync("user-1", Arg.Any<CancellationToken>()).Returns(user);

        var existingRefresh1 = RefreshToken.IssueNew("rt-1", "user-1", "h1", "fam-1",
            _clock.UtcNow + TimeSpan.FromDays(30), "CZ", null, null);
        var existingRefresh2 = RefreshToken.IssueNew("rt-2", "user-1", "h2", "fam-2",
            _clock.UtcNow + TimeSpan.FromDays(30), "CZ", null, null);
        _refreshTokens.GetActiveByUserAsync("user-1", Arg.Any<CancellationToken>())
            .Returns([existingRefresh1, existingRefresh2]);

        var result = await _handler.Handle(
            new ConfirmPasswordReset.Command("raw", "newpassword1"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        user.PasswordHash.Should().Be("argon2id$v=19$m=65536,t=3,p=1$NEW$HASH");
        existingRefresh1.RevokedAt.Should().Be(_clock.UtcNow);
        existingRefresh2.RevokedAt.Should().Be(_clock.UtcNow);
    }

    [Fact]
    public async Task Validation_rejects_password_shorter_than_ten_chars()
    {
        // Pin the password policy from ADR 0012 §Password policy. The
        // validator runs before the handler; here we exercise it directly.
        var validator = new ConfirmPasswordReset.Validator();
        var result = validator.Validate(new ConfirmPasswordReset.Command("raw", "short"));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorCode == BusinessErrorMessage.MinLength);
    }

}
