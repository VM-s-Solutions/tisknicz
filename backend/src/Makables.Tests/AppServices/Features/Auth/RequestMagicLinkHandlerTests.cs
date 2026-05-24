using FluentAssertions;
using Makables.Core.AppServices.Features.Auth;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Outbox;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.Auth;

public class RequestMagicLinkHandlerTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IOneTimeTokenRepository _tokens = Substitute.For<IOneTimeTokenRepository>();
    private readonly IOutbox _outbox = Substitute.For<IOutbox>();
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 5, 24, 12, 0, 0, TimeSpan.Zero));
    private readonly RequestMagicLink.Handler _handler;

    public RequestMagicLinkHandlerTests()
    {
        _handler = new RequestMagicLink.Handler(_users, _tokens, _outbox, _clock, NullLogger<RequestMagicLink.Handler>.Instance);
    }

    private static User CreateActiveUser(string email = "anna@example.cz")
    {
        var u = User.Create("user-1", email, UserRole.Customer, "Anna", "CZ", "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
        u.ConfirmEmail(DateTimeOffset.UtcNow);
        return u;
    }

    [Fact]
    public async Task Unknown_email_returns_success_and_does_not_email_or_persist_a_token()
    {
        _users.GetByEmailNormalizedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((User?)null);

        var result = await _handler.Handle(
            new RequestMagicLink.Command("ghost@nope.cz", "1.2.3.4"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _tokens.DidNotReceive().Add(Arg.Any<OneTimeToken>());
        _outbox.DidNotReceive().Enqueue(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Soft_deleted_account_returns_success_and_does_not_email_or_persist_a_token()
    {
        var user = CreateActiveUser();
        user.MarkDeactivated("admin", _clock.UtcNow);
        _users.GetByEmailNormalizedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(user);

        var result = await _handler.Handle(
            new RequestMagicLink.Command("anna@example.cz", null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _tokens.DidNotReceive().Add(Arg.Any<OneTimeToken>());
        _outbox.DidNotReceive().Enqueue(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Rate_limited_when_three_requests_in_the_last_ten_minutes()
    {
        var user = CreateActiveUser();
        _users.GetByEmailNormalizedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(user);
        _tokens.CountIssuedSinceAsync("user-1", OneTimeTokenPurpose.MagicLink, Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>()).Returns(RequestMagicLink.MaxRequestsPerWindow);

        var result = await _handler.Handle(
            new RequestMagicLink.Command("anna@example.cz", null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _tokens.DidNotReceive().Add(Arg.Any<OneTimeToken>());
        _outbox.DidNotReceive().Enqueue(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Happy_path_persists_token_and_enqueues_outbox_event()
    {
        var user = CreateActiveUser();
        _users.GetByEmailNormalizedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(user);
        _tokens.CountIssuedSinceAsync(Arg.Any<string>(), Arg.Any<OneTimeTokenPurpose>(),
            Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(0);

        var result = await _handler.Handle(
            new RequestMagicLink.Command("anna@example.cz", "1.2.3.4"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _tokens.Received(1).Add(Arg.Is<OneTimeToken>(t =>
            t.UserId == "user-1" &&
            t.Purpose == OneTimeTokenPurpose.MagicLink &&
            t.ExpiresAt == _clock.UtcNow + RequestMagicLink.TokenLifetime));
        _outbox.Received(1).Enqueue("user-1", RequestMagicLink.OutboxEventType, Arg.Any<string>());
    }

    [Fact]
    public async Task Window_uses_now_minus_RateLimitWindow_as_the_inclusive_lower_bound()
    {
        var user = CreateActiveUser();
        _users.GetByEmailNormalizedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(user);

        await _handler.Handle(new RequestMagicLink.Command("anna@example.cz", null), CancellationToken.None);

        await _tokens.Received(1).CountIssuedSinceAsync(
            "user-1",
            OneTimeTokenPurpose.MagicLink,
            _clock.UtcNow - RequestMagicLink.RateLimitWindow,
            Arg.Any<CancellationToken>());
    }

    private sealed class FakeClock(DateTimeOffset now) : IClock { public DateTimeOffset UtcNow { get; } = now; }
}
