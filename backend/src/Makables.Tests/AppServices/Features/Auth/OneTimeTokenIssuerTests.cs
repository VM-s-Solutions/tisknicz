using FluentAssertions;
using Makables.Core.AppServices.Common;
using Makables.Core.AppServices.Features.Auth;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Outbox;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Makables.TestUtilities;

namespace Makables.Tests.AppServices.Features.Auth;

/// <summary>
/// Pins the timing-equalization invariant + the issue pipeline contract
/// that all three opaque-token-by-email flows depend on. Per T-0023
/// security review B-1: an attacker must not be able to enumerate
/// emails by response latency. The issuer owns the invariant — these
/// tests are the regression net so it can't drift.
/// </summary>
public class OneTimeTokenIssuerTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IOneTimeTokenRepository _tokens = Substitute.For<IOneTimeTokenRepository>();
    private readonly IOutbox _outbox = Substitute.For<IOutbox>();
    private readonly ILanguageResolver _languageResolver = Substitute.For<ILanguageResolver>();
    private readonly FakeClock _clock = new();
    private readonly OneTimeTokenIssuer _sut;

    public OneTimeTokenIssuerTests()
    {
        _languageResolver.ResolveForUserAsync(Arg.Any<User>(), Arg.Any<CancellationToken>())
            .Returns(LanguageCode.DefaultFallback);
        _sut = new OneTimeTokenIssuer(_users, _tokens, _outbox, _clock, _languageResolver,
            NullLogger<OneTimeTokenIssuer>.Instance);
    }

    private static User CreateActiveUser(bool confirmed = false)
    {
        var u = User.Create("user-1", "anna@example.cz", UserRole.Customer, "Anna", "CZ",
            "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
        if (confirmed) u.ConfirmEmail(DateTimeOffset.UtcNow);
        return u;
    }

    private static IssueRequest MagicLinkRequest(
        Func<User, bool>? eligibility = null,
        Func<User, DateTimeOffset, CancellationToken, Task>? preHook = null) =>
        new(
            Email: "anna@example.cz",
            Purpose: OneTimeTokenPurpose.MagicLink,
            TokenLifetime: TimeSpan.FromMinutes(15),
            OutboxEventType: OutboxEventTypes.AuthMagicLinkSend,
            MaxRequestsPerWindow: 3,
            RateLimitWindow: TimeSpan.FromMinutes(10),
            EligibilityFilter: eligibility,
            PreIssueHook: preHook);

    [Fact]
    public async Task Unknown_email_no_ops_AND_still_runs_CountIssuedSince_to_equalize_latency()
    {
        _users.GetByEmailNormalizedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((User?)null);

        var outcome = await _sut.IssueAsync(MagicLinkRequest(), CancellationToken.None);

        outcome.Sent.Should().BeFalse();
        outcome.UserId.Should().BeNull();
        _tokens.DidNotReceive().Add(Arg.Any<OneTimeToken>());
        _outbox.DidNotReceive().Enqueue(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());

        // The B-1 invariant: the round-trip happens for the unknown
        // email path too (using a sentinel user id) so total latency
        // matches the known-email path.
        await _tokens.Received(1).CountIssuedSinceAsync(
            Arg.Any<string>(),
            OneTimeTokenPurpose.MagicLink,
            _clock.UtcNow - TimeSpan.FromMinutes(10),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Soft_deleted_user_no_ops()
    {
        var user = CreateActiveUser();
        user.MarkDeactivated("admin", _clock.UtcNow);
        _users.GetByEmailNormalizedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(user);

        var outcome = await _sut.IssueAsync(MagicLinkRequest(), CancellationToken.None);

        outcome.Sent.Should().BeFalse();
        _tokens.DidNotReceive().Add(Arg.Any<OneTimeToken>());
    }

    [Fact]
    public async Task Rate_limited_no_ops_WITHOUT_running_the_pre_issue_hook()
    {
        var user = CreateActiveUser();
        _users.GetByEmailNormalizedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(user);
        _tokens.CountIssuedSinceAsync("user-1", OneTimeTokenPurpose.MagicLink, Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>()).Returns(3);

        var preHookCalls = 0;
        var outcome = await _sut.IssueAsync(MagicLinkRequest(
            preHook: (_, _, _) => { preHookCalls++; return Task.CompletedTask; }),
            CancellationToken.None);

        outcome.Sent.Should().BeFalse();
        preHookCalls.Should().Be(0, "rate-limited requests MUST NOT run the pre-issue hook");
        _tokens.DidNotReceive().Add(Arg.Any<OneTimeToken>());
    }

    [Fact]
    public async Task Eligibility_filter_can_veto_an_otherwise_eligible_user()
    {
        var user = CreateActiveUser(confirmed: true);
        _users.GetByEmailNormalizedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(user);

        // Veto: only allow unconfirmed users (mirrors SendEmailConfirmation).
        var outcome = await _sut.IssueAsync(MagicLinkRequest(eligibility: u => u.EmailConfirmedAt is null),
            CancellationToken.None);

        outcome.Sent.Should().BeFalse();
        _tokens.DidNotReceive().Add(Arg.Any<OneTimeToken>());
    }

    [Fact]
    public async Task Happy_path_runs_pre_issue_hook_then_persists_token_then_enqueues_outbox()
    {
        var user = CreateActiveUser();
        _users.GetByEmailNormalizedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(user);

        var calls = new List<string>();
        var outcome = await _sut.IssueAsync(MagicLinkRequest(
            preHook: (_, _, _) => { calls.Add("preHook"); return Task.CompletedTask; }),
            CancellationToken.None);

        outcome.Sent.Should().BeTrue();
        outcome.UserId.Should().Be("user-1");
        calls.Should().Equal("preHook");
        _tokens.Received(1).Add(Arg.Is<OneTimeToken>(t =>
            t.UserId == "user-1" &&
            t.Purpose == OneTimeTokenPurpose.MagicLink &&
            t.ExpiresAt == _clock.UtcNow + TimeSpan.FromMinutes(15)));
        _outbox.Received(1).Enqueue("user-1", OutboxEventTypes.AuthMagicLinkSend, Arg.Any<string>());
    }
}
