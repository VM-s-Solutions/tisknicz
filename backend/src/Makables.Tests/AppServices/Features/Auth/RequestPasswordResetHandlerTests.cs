using FluentAssertions;
using Makables.Core.AppServices.Features.Auth;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Outbox;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.Auth;

public class RequestPasswordResetHandlerTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IOneTimeTokenRepository _tokens = Substitute.For<IOneTimeTokenRepository>();
    private readonly IOutbox _outbox = Substitute.For<IOutbox>();
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 5, 24, 12, 0, 0, TimeSpan.Zero));
    private readonly RequestPasswordReset.Handler _handler;

    public RequestPasswordResetHandlerTests()
    {
        _handler = new RequestPasswordReset.Handler(_users, _tokens, _outbox, _clock, NullLogger<RequestPasswordReset.Handler>.Instance);
    }

    private static User CreateActiveUser() =>
        User.Create("user-1", "anna@example.cz", UserRole.Customer, "Anna", "CZ", "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");

    [Fact]
    public async Task Unknown_email_returns_success_silently()
    {
        _users.GetByEmailNormalizedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((User?)null);

        var result = await _handler.Handle(
            new RequestPasswordReset.Command("ghost@nope.cz", null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _tokens.DidNotReceive().Add(Arg.Any<OneTimeToken>());
        _outbox.DidNotReceive().Enqueue(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
        await _tokens.DidNotReceive().InvalidateRedeemableAsync(Arg.Any<string>(), Arg.Any<OneTimeTokenPurpose>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Rate_limited_returns_success_without_invalidating_prior_tokens()
    {
        var user = CreateActiveUser();
        _users.GetByEmailNormalizedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(user);
        _tokens.CountIssuedSinceAsync("user-1", OneTimeTokenPurpose.PasswordReset, Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>()).Returns(RequestPasswordReset.MaxRequestsPerWindow);

        var result = await _handler.Handle(
            new RequestPasswordReset.Command("anna@example.cz", null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _tokens.DidNotReceive().Add(Arg.Any<OneTimeToken>());
        // Rate-limited request must NOT touch prior tokens — protects
        // against accidentally killing a legitimate in-flight reset.
        await _tokens.DidNotReceive().InvalidateRedeemableAsync(Arg.Any<string>(), Arg.Any<OneTimeTokenPurpose>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Happy_path_invalidates_prior_tokens_persists_new_one_and_enqueues_outbox()
    {
        var user = CreateActiveUser();
        _users.GetByEmailNormalizedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(user);

        var result = await _handler.Handle(
            new RequestPasswordReset.Command("anna@example.cz", "1.2.3.4"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _tokens.Received(1).InvalidateRedeemableAsync(
            "user-1", OneTimeTokenPurpose.PasswordReset, _clock.UtcNow, Arg.Any<CancellationToken>());
        _tokens.Received(1).Add(Arg.Is<OneTimeToken>(t =>
            t.UserId == "user-1" &&
            t.Purpose == OneTimeTokenPurpose.PasswordReset &&
            t.ExpiresAt == _clock.UtcNow + RequestPasswordReset.TokenLifetime));
        _outbox.Received(1).Enqueue("user-1", RequestPasswordReset.OutboxEventType, Arg.Any<string>());
    }

    [Fact]
    public async Task Unknown_email_path_also_runs_CountIssuedSince_to_equalize_latency()
    {
        // Same B-1 timing-equalization contract as the sibling flows.
        _users.GetByEmailNormalizedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((User?)null);

        await _handler.Handle(new RequestPasswordReset.Command("ghost@nope.cz", null), CancellationToken.None);

        await _tokens.Received(1).CountIssuedSinceAsync(
            Arg.Any<string>(),
            OneTimeTokenPurpose.PasswordReset,
            _clock.UtcNow - RequestPasswordReset.RateLimitWindow,
            Arg.Any<CancellationToken>());
    }

    private sealed class FakeClock(DateTimeOffset now) : IClock { public DateTimeOffset UtcNow { get; } = now; }
}
