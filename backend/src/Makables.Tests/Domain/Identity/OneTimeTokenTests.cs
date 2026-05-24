using FluentAssertions;
using Makables.Core.Domain.Identity;

namespace Makables.Tests.Domain.Identity;

public class OneTimeTokenTests
{
    private static OneTimeToken Issue(
        OneTimeTokenPurpose purpose = OneTimeTokenPurpose.MagicLink,
        DateTimeOffset? now = null,
        TimeSpan? lifetime = null) =>
        OneTimeToken.Issue(
            tokenHash: "hash-" + Guid.NewGuid().ToString("N")[..16],
            userId: "user-1",
            purpose: purpose,
            expiresAt: (now ?? DateTimeOffset.UtcNow) + (lifetime ?? TimeSpan.FromMinutes(15)),
            now: now ?? DateTimeOffset.UtcNow);

    [Fact]
    public void Issue_sets_fields_and_marks_not_consumed_not_expired()
    {
        var now = DateTimeOffset.UtcNow;
        var token = Issue(now: now);

        token.UserId.Should().Be("user-1");
        token.Purpose.Should().Be(OneTimeTokenPurpose.MagicLink);
        token.CreatedAt.Should().Be(now);
        token.ConsumedAt.Should().BeNull();
        token.IsConsumed.Should().BeFalse();
        token.IsExpired(now).Should().BeFalse();
        token.IsRedeemable(now).Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Issue_rejects_blank_hash(string hash)
    {
        Action act = () => OneTimeToken.Issue(hash, "user-1", OneTimeTokenPurpose.MagicLink,
            DateTimeOffset.UtcNow.AddMinutes(15), DateTimeOffset.UtcNow);
        act.Should().Throw<ArgumentException>().WithParameterName("tokenHash");
    }

    [Fact]
    public void Issue_rejects_expiry_in_the_past()
    {
        var now = DateTimeOffset.UtcNow;
        Action act = () => OneTimeToken.Issue("h", "u", OneTimeTokenPurpose.MagicLink,
            expiresAt: now.AddMinutes(-1), now: now);
        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("expiresAt");
    }

    [Fact]
    public void Consume_flips_state()
    {
        var now = DateTimeOffset.UtcNow;
        var token = Issue(now: now);

        token.Consume(now.AddSeconds(5));

        token.IsConsumed.Should().BeTrue();
        token.ConsumedAt.Should().Be(now.AddSeconds(5));
        token.IsRedeemable(now.AddSeconds(5)).Should().BeFalse();
    }

    [Fact]
    public void Consume_twice_throws_to_catch_accidental_double_credit()
    {
        var token = Issue();
        token.Consume(DateTimeOffset.UtcNow);

        Action act = () => token.Consume(DateTimeOffset.UtcNow);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void IsExpired_flips_after_window()
    {
        var now = DateTimeOffset.UtcNow;
        var token = Issue(now: now, lifetime: TimeSpan.FromMinutes(15));

        token.IsExpired(now + TimeSpan.FromMinutes(15)).Should().BeTrue("expiry is inclusive");
        token.IsExpired(now + TimeSpan.FromMinutes(16)).Should().BeTrue();
        token.IsRedeemable(now + TimeSpan.FromMinutes(16)).Should().BeFalse();
    }

    [Fact]
    public void Issue_truncates_overlong_ip()
    {
        var longIp = new string('x', 200);
        var token = OneTimeToken.Issue("h", "u", OneTimeTokenPurpose.MagicLink,
            DateTimeOffset.UtcNow.AddMinutes(15), DateTimeOffset.UtcNow,
            ipAddress: longIp);
        token.IpAddress!.Length.Should().Be(64);
    }
}
