using FluentAssertions;
using Makables.Core.Domain.Identity;

namespace Makables.Tests.Domain.Identity;

public class RefreshTokenTests
{
    private static RefreshToken Issue(string id = "tok-1") => RefreshToken.IssueNew(
        id: id,
        userId: "user-1",
        tokenHash: $"hash-{id}",
        familyId: "fam-1",
        expiresAt: DateTimeOffset.UtcNow + TimeSpan.FromDays(30),
        countryCode: "cz",
        userAgent: "test-agent",
        ipAddress: "127.0.0.1");

    [Fact]
    public void IssueNew_uppercases_country_code()
    {
        var t = Issue();
        t.CountryCode.Should().Be("CZ");
    }

    [Fact]
    public void IssueNew_truncates_overlong_user_agent()
    {
        var longUa = new string('x', 600);
        var t = RefreshToken.IssueNew("tok-1", "u-1", "hash", "fam-1",
            DateTimeOffset.UtcNow + TimeSpan.FromDays(30),
            "CZ", longUa, "127.0.0.1");

        t.UserAgent!.Length.Should().Be(500);
    }

    [Fact]
    public void IssueRotation_preserves_family_id_and_user_and_country()
    {
        var original = Issue("tok-1");
        var rotated = original.IssueRotation(
            newId: "tok-2",
            newTokenHash: "hash-tok-2",
            newExpiresAt: DateTimeOffset.UtcNow + TimeSpan.FromDays(30),
            userAgent: "test-agent-2",
            ipAddress: "10.0.0.1");

        rotated.FamilyId.Should().Be(original.FamilyId);
        rotated.UserId.Should().Be(original.UserId);
        rotated.CountryCode.Should().Be(original.CountryCode);
        rotated.Id.Should().Be("tok-2");
        rotated.TokenHash.Should().Be("hash-tok-2");
    }

    [Fact]
    public void MarkRotated_revokes_and_links_to_replacement()
    {
        var t = Issue("tok-1");
        var now = DateTimeOffset.UtcNow;

        t.MarkRotated("tok-2", now);

        t.RevokedAt.Should().Be(now);
        t.ReplacedByTokenId.Should().Be("tok-2");
    }

    [Fact]
    public void MarkRotated_throws_when_already_revoked_triggers_reuse_detection()
    {
        var t = Issue("tok-1");
        t.MarkRotated("tok-2", DateTimeOffset.UtcNow);

        Action act = () => t.MarkRotated("tok-3", DateTimeOffset.UtcNow);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*reuse detection*");
    }

    [Fact]
    public void Revoke_is_idempotent()
    {
        var t = Issue("tok-1");
        var t1 = DateTimeOffset.UtcNow;
        var t2 = t1.AddSeconds(5);

        t.Revoke(t1);
        t.Revoke(t2);

        t.RevokedAt.Should().Be(t1, "first revoke wins; second is a no-op");
    }

    [Fact]
    public void IsActiveAt_returns_false_after_revocation()
    {
        var t = Issue();
        t.Revoke(DateTimeOffset.UtcNow);

        t.IsActiveAt(DateTimeOffset.UtcNow).Should().BeFalse();
    }

    [Fact]
    public void IsActiveAt_returns_false_after_expiry()
    {
        var t = Issue();
        // The token expires 30 days after issue (in IssueNew above).
        var futurePoint = DateTimeOffset.UtcNow + TimeSpan.FromDays(31);

        t.IsActiveAt(futurePoint).Should().BeFalse();
    }
}
