using FluentAssertions;
using Makables.Core.AppServices.Common;
using Makables.Core.AppServices.Features.Auth;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.Auth;

public class ConfirmEmailHandlerTests
{
    private readonly IOneTimeTokenRepository _tokens = Substitute.For<IOneTimeTokenRepository>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 5, 24, 12, 0, 0, TimeSpan.Zero));
    private readonly ConfirmEmail.Handler _handler;

    public ConfirmEmailHandlerTests()
    {
        _handler = new ConfirmEmail.Handler(_tokens, _users, _clock, NullLogger<ConfirmEmail.Handler>.Instance);
    }

    private static User CreateUser(bool confirmed = false)
    {
        var u = User.Create("user-1", "anna@example.cz", UserRole.Customer, "Anna", "CZ", "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
        if (confirmed) u.ConfirmEmail(DateTimeOffset.UtcNow.AddDays(-1));
        return u;
    }

    private static OneTimeToken IssueRedeemable(DateTimeOffset now, OneTimeTokenPurpose purpose = OneTimeTokenPurpose.EmailConfirmation) =>
        OneTimeToken.Issue("hash", "user-1", purpose, now + TimeSpan.FromHours(24), now);

    [Fact]
    public async Task Returns_invalid_when_token_unknown()
    {
        _tokens.GetByHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((OneTimeToken?)null);

        var result = await _handler.Handle(new ConfirmEmail.Command("raw"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.AuthEmailConfirmationInvalid);
        await _tokens.DidNotReceive().TryConsumeAsync(Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Returns_invalid_when_token_purpose_is_MagicLink_AND_does_not_claim_it()
    {
        var token = IssueRedeemable(_clock.UtcNow, OneTimeTokenPurpose.MagicLink);
        _tokens.GetByHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(token);

        var result = await _handler.Handle(new ConfirmEmail.Command("raw"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.AuthEmailConfirmationInvalid);
        await _tokens.DidNotReceive().TryConsumeAsync(Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Returns_invalid_when_token_expired_or_consumed()
    {
        var consumed = IssueRedeemable(_clock.UtcNow);
        consumed.Consume(_clock.UtcNow.AddMinutes(-1));
        _tokens.GetByHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(consumed);

        var result = await _handler.Handle(new ConfirmEmail.Command("raw"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.AuthEmailConfirmationInvalid);
    }

    [Fact]
    public async Task Lost_race_to_concurrent_request_returns_invalid()
    {
        var token = IssueRedeemable(_clock.UtcNow);
        _tokens.GetByHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(token);
        _tokens.TryConsumeAsync(Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(false);

        var result = await _handler.Handle(new ConfirmEmail.Command("raw"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.AuthEmailConfirmationInvalid);
    }

    [Fact]
    public async Task Soft_deleted_user_returns_invalid_after_token_already_claimed()
    {
        var token = IssueRedeemable(_clock.UtcNow);
        _tokens.GetByHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(token);
        _tokens.TryConsumeAsync(Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(true);
        var user = CreateUser();
        user.MarkDeactivated("admin", _clock.UtcNow.AddMinutes(-10));
        _users.GetByIdAsync("user-1", Arg.Any<CancellationToken>()).Returns(user);

        var result = await _handler.Handle(new ConfirmEmail.Command("raw"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.AuthEmailConfirmationInvalid);
        // Token was burned by the claim — replay safety.
        await _tokens.Received(1).TryConsumeAsync(Arg.Any<string>(), _clock.UtcNow, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Happy_path_claims_token_and_marks_email_confirmed()
    {
        var token = IssueRedeemable(_clock.UtcNow);
        _tokens.GetByHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(token);
        _tokens.TryConsumeAsync(Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(true);
        var user = CreateUser(confirmed: false);
        _users.GetByIdAsync("user-1", Arg.Any<CancellationToken>()).Returns(user);

        var result = await _handler.Handle(new ConfirmEmail.Command("raw"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        user.EmailConfirmedAt.Should().NotBeNull();
        user.EmailConfirmedAt.Should().Be(_clock.UtcNow);
    }

    [Fact]
    public async Task Happy_path_is_idempotent_when_email_was_already_confirmed_externally()
    {
        // ConfirmEmail is itself idempotent; a token that resolves to an
        // already-confirmed user still burns (replay safety) and returns
        // Success.
        var token = IssueRedeemable(_clock.UtcNow);
        _tokens.GetByHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(token);
        _tokens.TryConsumeAsync(Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(true);
        var user = CreateUser(confirmed: true);
        var originalConfirmedAt = user.EmailConfirmedAt;
        _users.GetByIdAsync("user-1", Arg.Any<CancellationToken>()).Returns(user);

        var result = await _handler.Handle(new ConfirmEmail.Command("raw"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        user.EmailConfirmedAt.Should().Be(originalConfirmedAt, "ConfirmEmail is idempotent — first confirmation wins");
    }

    private sealed class FakeClock(DateTimeOffset now) : IClock { public DateTimeOffset UtcNow { get; } = now; }
}
