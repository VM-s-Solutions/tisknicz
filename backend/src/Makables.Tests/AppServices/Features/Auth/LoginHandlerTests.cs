using FluentAssertions;
using Makables.Core.AppServices.Features.Auth;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Makables.TestUtilities;

namespace Makables.Tests.AppServices.Features.Auth;

public class LoginHandlerTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly ILoginAttemptBucketRepository _buckets = Substitute.For<ILoginAttemptBucketRepository>();
    private readonly IRefreshTokenRepository _refreshTokens = Substitute.For<IRefreshTokenRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly IJwtIssuer _jwt = Substitute.For<IJwtIssuer>();
    private readonly IIdGenerator _ids = Substitute.For<IIdGenerator>();
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 5, 24, 12, 0, 0, TimeSpan.Zero));
    private readonly Login.Handler _handler;

    private static readonly LockoutOptions Lockout = new() { Threshold = 5, Window = TimeSpan.FromMinutes(15) };

    public LoginHandlerTests()
    {
        _ids.Next().Returns(_ => Ulid.NewUlid().ToString());
        _jwt.Issue(Arg.Any<User>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>())
            .Returns(call => new AccessToken("access.jwt.token", call.Arg<DateTimeOffset>() + TimeSpan.FromMinutes(15), "jti-1"));

        _handler = new Login.Handler(
            _users, _buckets, _refreshTokens, _hasher, _jwt, _ids, _clock,
            Options.Create(Lockout),
            NullLogger<Login.Handler>.Instance);
    }

    private static User CreateUser(
        bool confirmed = true,
        string passwordHash = "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB",
        UserRole role = UserRole.Customer)
    {
        var u = User.Create("user-1", "anna@example.cz", role, "Anna", "CZ", passwordHash);
        if (confirmed) u.ConfirmEmail(DateTimeOffset.UtcNow);
        return u;
    }

    [Fact]
    public async Task Unknown_email_consumes_a_ghost_bucket_slot_and_returns_invalid_credentials()
    {
        _buckets.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((LoginAttemptBucket?)null);
        _users.GetByEmailNormalizedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((User?)null);
        _hasher.DummyHashForTimingEqualization.Returns("dummy-argon2-hash");

        var result = await _handler.Handle(
            new Login.Command("ghost@nope.cz", "whatever1234", "customer", null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.AuthInvalidCredentials);
        // A fresh bucket was created AND a failed attempt was registered.
        _buckets.Received(1).Add(Arg.Is<LoginAttemptBucket>(b => b.AttemptCount == 1));
        // Reviewer T-0022 BLOCKER B-2: unknown-email branch MUST run a
        // dummy Argon2id verify so total latency matches the known-email
        // branch and an attacker can't enumerate emails by response time.
        _hasher.Received(1).Verify("whatever1234", "dummy-argon2-hash");
    }

    [Fact]
    public async Task Successful_first_login_for_account_without_prior_bucket_does_NOT_create_a_bucket_row()
    {
        // Reviewer T-0022 MINOR Mi-1: don't write a no-op bucket row when
        // the success path will only Reset it back to zero.
        var user = CreateUser();
        _buckets.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((LoginAttemptBucket?)null);
        _users.GetByEmailNormalizedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(user);
        _hasher.Verify("correct-pw", Arg.Any<string>()).Returns(true);
        _hasher.NeedsRehash(Arg.Any<string>()).Returns(false);

        var result = await _handler.Handle(
            new Login.Command("anna@example.cz", "correct-pw", "customer", null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _buckets.DidNotReceive().Add(Arg.Any<LoginAttemptBucket>());
    }

    [Fact]
    public async Task Wrong_password_returns_invalid_credentials_and_increments_both_counters()
    {
        var user = CreateUser();
        _users.GetByEmailNormalizedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(user);
        _hasher.Verify("wrong-pw", Arg.Any<string>()).Returns(false);

        var result = await _handler.Handle(
            new Login.Command("anna@example.cz", "wrong-pw", "customer", null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.AuthInvalidCredentials);
        user.FailedLoginCount.Should().Be(1);
        _buckets.Received(1).Add(Arg.Is<LoginAttemptBucket>(b => b.AttemptCount == 1));
    }

    [Fact]
    public async Task Locked_bucket_short_circuits_before_password_check()
    {
        var bucket = LoginAttemptBucket.Create("anna@example.cz", _clock.UtcNow.AddMinutes(-1));
        for (var i = 0; i < Lockout.Threshold; i++)
            bucket.RegisterFailedAttempt(_clock.UtcNow.AddMinutes(-1), Lockout.Threshold, Lockout.Window);
        _buckets.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(bucket);

        var result = await _handler.Handle(
            new Login.Command("anna@example.cz", "correct-pw", "customer", null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.AuthLocked);
        await _users.DidNotReceive().GetByEmailNormalizedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Locked_user_row_returns_locked_even_when_bucket_is_clean()
    {
        var user = CreateUser();
        for (var i = 0; i < Lockout.Threshold; i++)
            user.RegisterFailedLogin(_clock.UtcNow, Lockout.Threshold, Lockout.Window);
        _users.GetByEmailNormalizedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(user);

        var result = await _handler.Handle(
            new Login.Command("anna@example.cz", "correct-pw", "customer", null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.AuthLocked);
    }

    [Fact]
    public async Task Unconfirmed_email_blocks_login_with_specific_error_code()
    {
        var user = CreateUser(confirmed: false);
        _users.GetByEmailNormalizedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(user);
        _hasher.Verify("correct-pw", Arg.Any<string>()).Returns(true);

        var result = await _handler.Handle(
            new Login.Command("anna@example.cz", "correct-pw", "customer", null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.AuthEmailNotConfirmed);
    }

    [Fact]
    public async Task Non_admin_user_cannot_login_to_a_different_audience()
    {
        // A customer attempting to log in via the maker endpoint must be forbidden.
        var user = CreateUser(role: UserRole.Customer);
        _users.GetByEmailNormalizedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(user);
        _hasher.Verify("correct-pw", Arg.Any<string>()).Returns(true);

        var result = await _handler.Handle(
            new Login.Command("anna@example.cz", "correct-pw", "maker", null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.Forbidden);
    }

    [Fact]
    public async Task Admin_can_login_to_any_audience()
    {
        var admin = CreateUser(role: UserRole.Admin);
        _users.GetByEmailNormalizedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(admin);
        _hasher.Verify("correct-pw", Arg.Any<string>()).Returns(true);

        var result = await _handler.Handle(
            new Login.Command("anna@example.cz", "correct-pw", "maker", null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Happy_path_resets_counters_issues_tokens_and_persists_refresh_row()
    {
        var bucket = LoginAttemptBucket.Create("anna@example.cz", _clock.UtcNow.AddMinutes(-1));
        bucket.RegisterFailedAttempt(_clock.UtcNow.AddMinutes(-1), Lockout.Threshold, Lockout.Window);
        _buckets.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(bucket);

        var user = CreateUser();
        user.RegisterFailedLogin(_clock.UtcNow.AddMinutes(-1), Lockout.Threshold, Lockout.Window);
        _users.GetByEmailNormalizedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(user);
        _hasher.Verify("correct-pw", Arg.Any<string>()).Returns(true);
        _hasher.NeedsRehash(Arg.Any<string>()).Returns(false);

        var result = await _handler.Handle(
            new Login.Command("anna@example.cz", "correct-pw", "customer", "ua-x", "10.0.0.1"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.AccessToken.Should().Be("access.jwt.token");
        result.Value.RefreshToken.Should().NotBeNullOrWhiteSpace();
        result.Value.RefreshTokenExpiresAt.Should().Be(_clock.UtcNow + TimeSpan.FromDays(30));

        user.FailedLoginCount.Should().Be(0);
        bucket.AttemptCount.Should().Be(0);
        _refreshTokens.Received(1).Add(Arg.Any<RefreshToken>());
    }

    [Fact]
    public async Task Transparent_rehash_runs_on_successful_login_when_params_have_been_bumped()
    {
        var user = CreateUser(passwordHash: "argon2id$v=19$m=4096,t=1,p=1$AAAA$BBBB");
        _users.GetByEmailNormalizedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(user);
        _hasher.Verify("correct-pw", Arg.Any<string>()).Returns(true);
        _hasher.NeedsRehash(Arg.Any<string>()).Returns(true);
        _hasher.Hash("correct-pw").Returns("argon2id$v=19$m=65536,t=3,p=1$AAAA$BBBB");

        var result = await _handler.Handle(
            new Login.Command("anna@example.cz", "correct-pw", "customer", null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        user.PasswordHash.Should().StartWith("argon2id$v=19$m=65536");
    }

}
