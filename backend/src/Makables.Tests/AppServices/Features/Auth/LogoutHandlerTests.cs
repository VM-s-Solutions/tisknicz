using FluentAssertions;
using Makables.Core.AppServices.Features.Auth;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.Auth;

public class LogoutHandlerTests
{
    private readonly IRefreshTokenRepository _refreshTokens = Substitute.For<IRefreshTokenRepository>();
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 5, 24, 12, 0, 0, TimeSpan.Zero));
    private readonly Logout.Handler _handler;

    public LogoutHandlerTests()
    {
        _handler = new Logout.Handler(_refreshTokens, _clock);
    }

    [Fact]
    public async Task Logout_revokes_a_live_refresh_token()
    {
        var token = RefreshToken.IssueNew("tok-1", "user-1",
            OpaqueTokenFactory.Sha256Hex("raw-refresh"),
            "fam-1", _clock.UtcNow + TimeSpan.FromDays(30), "CZ", null, null);
        _refreshTokens.GetByTokenHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(token);

        var result = await _handler.Handle(new Logout.Command("raw-refresh"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        token.RevokedAt.Should().Be(_clock.UtcNow);
    }

    [Fact]
    public async Task Logout_is_idempotent_for_an_unknown_token()
    {
        _refreshTokens.GetByTokenHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((RefreshToken?)null);

        var result = await _handler.Handle(new Logout.Command("raw-not-real"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue("logout silently succeeds so the UI doesn't flap");
    }

    private sealed class FakeClock(DateTimeOffset now) : IClock { public DateTimeOffset UtcNow { get; } = now; }
}
