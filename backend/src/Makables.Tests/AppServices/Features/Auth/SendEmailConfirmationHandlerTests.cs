using FluentAssertions;
using Makables.Core.AppServices.Features.Auth;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Outbox;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.Auth;

public class SendEmailConfirmationHandlerTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IOneTimeTokenRepository _tokens = Substitute.For<IOneTimeTokenRepository>();
    private readonly IOutbox _outbox = Substitute.For<IOutbox>();
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 5, 24, 12, 0, 0, TimeSpan.Zero));
    private readonly SendEmailConfirmation.Handler _handler;

    public SendEmailConfirmationHandlerTests()
    {
        _handler = new SendEmailConfirmation.Handler(_users, _tokens, _outbox, _clock, NullLogger<SendEmailConfirmation.Handler>.Instance);
    }

    private static User CreateUnconfirmedUser() =>
        User.Create("user-1", "anna@example.cz", UserRole.Customer, "Anna", "CZ", "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");

    private static User CreateConfirmedUser()
    {
        var u = CreateUnconfirmedUser();
        u.ConfirmEmail(DateTimeOffset.UtcNow);
        return u;
    }

    [Fact]
    public async Task Unknown_email_returns_success_and_does_not_persist_or_enqueue()
    {
        _users.GetByEmailNormalizedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((User?)null);

        var result = await _handler.Handle(
            new SendEmailConfirmation.Command("ghost@nope.cz", null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _tokens.DidNotReceive().Add(Arg.Any<OneTimeToken>());
        _outbox.DidNotReceive().Enqueue(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Already_confirmed_account_returns_success_silently()
    {
        // Re-sending a confirmation to an already-confirmed account
        // could be mistaken for a phishing attempt — silent no-op.
        var user = CreateConfirmedUser();
        _users.GetByEmailNormalizedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(user);

        var result = await _handler.Handle(
            new SendEmailConfirmation.Command("anna@example.cz", null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _tokens.DidNotReceive().Add(Arg.Any<OneTimeToken>());
        _outbox.DidNotReceive().Enqueue(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Rate_limited_when_three_requests_in_the_last_ten_minutes()
    {
        var user = CreateUnconfirmedUser();
        _users.GetByEmailNormalizedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(user);
        _tokens.CountIssuedSinceAsync("user-1", OneTimeTokenPurpose.EmailConfirmation, Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>()).Returns(SendEmailConfirmation.MaxRequestsPerWindow);

        var result = await _handler.Handle(
            new SendEmailConfirmation.Command("anna@example.cz", null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _tokens.DidNotReceive().Add(Arg.Any<OneTimeToken>());
        _outbox.DidNotReceive().Enqueue(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Happy_path_persists_token_and_enqueues_outbox_event()
    {
        var user = CreateUnconfirmedUser();
        _users.GetByEmailNormalizedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(user);

        var result = await _handler.Handle(
            new SendEmailConfirmation.Command("anna@example.cz", "1.2.3.4"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _tokens.Received(1).Add(Arg.Is<OneTimeToken>(t =>
            t.UserId == "user-1" &&
            t.Purpose == OneTimeTokenPurpose.EmailConfirmation &&
            t.ExpiresAt == _clock.UtcNow + SendEmailConfirmation.TokenLifetime));
        _outbox.Received(1).Enqueue("user-1", SendEmailConfirmation.OutboxEventType, Arg.Any<string>());
    }

    [Fact]
    public async Task Unknown_email_path_also_runs_CountIssuedSince_to_equalize_latency()
    {
        // Same B-1 timing-equalization contract as RequestMagicLink.
        _users.GetByEmailNormalizedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((User?)null);

        await _handler.Handle(new SendEmailConfirmation.Command("ghost@nope.cz", null), CancellationToken.None);

        await _tokens.Received(1).CountIssuedSinceAsync(
            Arg.Any<string>(),
            OneTimeTokenPurpose.EmailConfirmation,
            _clock.UtcNow - SendEmailConfirmation.RateLimitWindow,
            Arg.Any<CancellationToken>());
    }

    private sealed class FakeClock(DateTimeOffset now) : IClock { public DateTimeOffset UtcNow { get; } = now; }
}
