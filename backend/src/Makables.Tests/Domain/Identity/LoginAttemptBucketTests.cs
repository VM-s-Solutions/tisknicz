using FluentAssertions;
using Makables.Core.Domain.Identity;

namespace Makables.Tests.Domain.Identity;

public class LoginAttemptBucketTests
{
    private const int Threshold = 5;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    [Fact]
    public void Create_uses_email_as_both_id_and_lookup_key()
    {
        var now = DateTimeOffset.UtcNow;
        var b = LoginAttemptBucket.Create("anna@example.cz", now);

        b.Id.Should().Be("anna@example.cz");
        b.EmailNormalized.Should().Be("anna@example.cz");
        b.AttemptCount.Should().Be(0);
        b.LockedUntil.Should().BeNull();
        b.LastAttemptAt.Should().Be(now);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Create_rejects_blank_email(string email)
    {
        Action act = () => LoginAttemptBucket.Create(email, DateTimeOffset.UtcNow);
        act.Should().Throw<ArgumentException>().WithParameterName("emailNormalized");
    }

    [Fact]
    public void RegisterFailedAttempt_locks_at_threshold()
    {
        var b = LoginAttemptBucket.Create("a@x.cz", DateTimeOffset.UtcNow);
        var t0 = DateTimeOffset.UtcNow;

        for (var i = 0; i < Threshold - 1; i++)
            b.RegisterFailedAttempt(t0, Threshold, Window);

        b.IsLocked(t0).Should().BeFalse();
        b.RegisterFailedAttempt(t0, Threshold, Window);

        b.AttemptCount.Should().Be(Threshold);
        b.IsLocked(t0).Should().BeTrue();
        b.LockedUntil.Should().Be(t0 + Window);
    }

    [Fact]
    public void RegisterFailedAttempt_is_a_no_op_while_already_locked()
    {
        // Mirrors User.RegisterFailedLogin: an attacker can't extend the
        // victim's lockout window by hammering the endpoint from inside
        // the window.
        var b = LoginAttemptBucket.Create("a@x.cz", DateTimeOffset.UtcNow);
        var t0 = DateTimeOffset.UtcNow;
        for (var i = 0; i < Threshold; i++)
            b.RegisterFailedAttempt(t0, Threshold, Window);

        var lockedUntilAtLock = b.LockedUntil;
        var countAtLock = b.AttemptCount;

        var midWindow = t0 + TimeSpan.FromMinutes(5);
        b.RegisterFailedAttempt(midWindow, Threshold, Window);

        b.AttemptCount.Should().Be(countAtLock);
        b.LockedUntil.Should().Be(lockedUntilAtLock);
    }

    [Fact]
    public void Reset_clears_counters_and_lockout()
    {
        var b = LoginAttemptBucket.Create("a@x.cz", DateTimeOffset.UtcNow);
        var t0 = DateTimeOffset.UtcNow;
        for (var i = 0; i < Threshold; i++)
            b.RegisterFailedAttempt(t0, Threshold, Window);

        b.Reset(t0 + TimeSpan.FromMinutes(1));

        b.AttemptCount.Should().Be(0);
        b.LockedUntil.Should().BeNull();
        b.IsLocked(t0).Should().BeFalse();
    }

    [Fact]
    public void IsLocked_returns_false_after_the_window_has_passed()
    {
        var b = LoginAttemptBucket.Create("a@x.cz", DateTimeOffset.UtcNow);
        var lockedAt = DateTimeOffset.UtcNow;
        for (var i = 0; i < Threshold; i++)
            b.RegisterFailedAttempt(lockedAt, Threshold, Window);

        var afterWindow = lockedAt + TimeSpan.FromMinutes(16);
        b.IsLocked(afterWindow).Should().BeFalse();
    }
}
