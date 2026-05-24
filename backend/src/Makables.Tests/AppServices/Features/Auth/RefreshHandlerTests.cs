using FluentAssertions;
using Makables.Core.AppServices.Common;
using Makables.Core.AppServices.Features.Auth;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.Auth;

public class RefreshHandlerTests
{
    private readonly IRefreshTokenRepository _refreshTokens = Substitute.For<IRefreshTokenRepository>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IJwtIssuer _jwt = Substitute.For<IJwtIssuer>();
    private readonly IIdGenerator _ids = Substitute.For<IIdGenerator>();
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 5, 24, 12, 0, 0, TimeSpan.Zero));
    private readonly Refresh.Handler _handler;

    public RefreshHandlerTests()
    {
        _ids.Next().Returns(_ => Ulid.NewUlid().ToString());
        _jwt.Issue(Arg.Any<User>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>())
            .Returns(c => new AccessToken("access.jwt.token", c.Arg<DateTimeOffset>() + TimeSpan.FromMinutes(15), "jti"));
        _handler = new Refresh.Handler(_refreshTokens, _users, _jwt, _ids, _clock, NullLogger<Refresh.Handler>.Instance);
    }

    private static User CreateUser(UserRole role = UserRole.Customer)
    {
        var u = User.Create("user-1", "anna@example.cz", role, "Anna", "CZ", "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
        u.ConfirmEmail(DateTimeOffset.UtcNow);
        return u;
    }

    [Fact]
    public async Task Unknown_refresh_token_returns_unauthorized()
    {
        _refreshTokens.GetByTokenHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((RefreshToken?)null);

        var result = await _handler.Handle(
            new Refresh.Command("not-a-real-token", "customer", null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.Unauthorized);
    }

    [Fact]
    public async Task Already_revoked_token_triggers_family_wide_revocation_and_returns_unauthorized()
    {
        // Reuse detection per ADR 0012 §Refresh token.
        var existing = RefreshToken.IssueNew("tok-old", "user-1",
            "hash-old", "fam-1", _clock.UtcNow + TimeSpan.FromDays(30), "CZ", null, null);
        existing.MarkRotated("tok-newer", _clock.UtcNow.AddMinutes(-5));

        var siblingActive = RefreshToken.IssueNew("tok-newer", "user-1",
            "hash-newer", "fam-1", _clock.UtcNow + TimeSpan.FromDays(30), "CZ", null, null);

        _refreshTokens.GetByTokenHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(existing);
        _refreshTokens.GetActiveByFamilyAsync("fam-1", Arg.Any<CancellationToken>())
            .Returns([siblingActive]);

        var result = await _handler.Handle(
            new Refresh.Command("raw-stolen-token", "customer", null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.Unauthorized);
        siblingActive.RevokedAt.Should().NotBeNull("reuse detection burns the whole family");
    }

    [Fact]
    public async Task Expired_token_returns_unauthorized()
    {
        var expired = RefreshToken.IssueNew("tok", "user-1",
            "hash", "fam", _clock.UtcNow.AddMinutes(-1), "CZ", null, null);
        _refreshTokens.GetByTokenHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(expired);

        var result = await _handler.Handle(
            new Refresh.Command("raw", "customer", null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.Unauthorized);
    }

    [Fact]
    public async Task Soft_deleted_user_revokes_the_token_and_returns_unauthorized()
    {
        var token = RefreshToken.IssueNew("tok", "user-1",
            "hash", "fam", _clock.UtcNow + TimeSpan.FromDays(30), "CZ", null, null);
        _refreshTokens.GetByTokenHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(token);

        var deactivated = CreateUser();
        deactivated.MarkDeactivated("test", _clock.UtcNow);
        _users.GetByIdAsync("user-1", Arg.Any<CancellationToken>()).Returns(deactivated);

        var result = await _handler.Handle(
            new Refresh.Command("raw", "customer", null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.Unauthorized);
        token.RevokedAt.Should().Be(_clock.UtcNow);
    }

    [Fact]
    public async Task Audience_mismatch_for_non_admin_user_returns_forbidden()
    {
        var token = RefreshToken.IssueNew("tok", "user-1",
            "hash", "fam", _clock.UtcNow + TimeSpan.FromDays(30), "CZ", null, null);
        _refreshTokens.GetByTokenHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(token);
        _users.GetByIdAsync("user-1", Arg.Any<CancellationToken>()).Returns(CreateUser(UserRole.Customer));

        var result = await _handler.Handle(
            new Refresh.Command("raw", "maker", null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.Forbidden);
    }

    [Fact]
    public async Task Happy_path_rotates_token_and_returns_new_session()
    {
        var token = RefreshToken.IssueNew("tok-old", "user-1",
            "hash-old", "fam-1", _clock.UtcNow + TimeSpan.FromDays(30), "CZ", "ua", "1.2.3.4");
        _refreshTokens.GetByTokenHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(token);
        _users.GetByIdAsync("user-1", Arg.Any<CancellationToken>()).Returns(CreateUser());

        var result = await _handler.Handle(
            new Refresh.Command("raw", "customer", "ua-2", "5.6.7.8"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.AccessToken.Should().Be("access.jwt.token");
        result.Value.RefreshToken.Should().NotBeNullOrWhiteSpace();
        token.RevokedAt.Should().Be(_clock.UtcNow, "old token must be marked rotated");
        token.ReplacedByTokenId.Should().NotBeNullOrWhiteSpace();
        _refreshTokens.Received(1).Add(Arg.Is<RefreshToken>(t => t.FamilyId == "fam-1"));
    }

    private sealed class FakeClock(DateTimeOffset now) : IClock { public DateTimeOffset UtcNow { get; } = now; }
}
