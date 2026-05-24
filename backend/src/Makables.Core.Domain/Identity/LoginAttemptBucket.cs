using Makables.Core.Domain.Common;

namespace Makables.Core.Domain.Identity;

/// <summary>
/// Per-email failed-login counter + lockout state. Per ADR 0012 §Lockout:
/// "if the email doesn't exist, we still consume ghost lockout slots
/// (rate limit by EmailNormalized even if user is missing) to prevent
/// enumeration." Q-0004 chose a persistent table over an in-memory LRU
/// so the rate-limit policy works under multi-host scale-out.
///
/// Inherits <see cref="BaseEntity"/> rather than <see cref="Auditable"/>
/// — buckets are anti-abuse infrastructure, not transactional data. They
/// have no <c>CountryCode</c>, no soft-delete, and no audit trail beyond
/// what <see cref="LastAttemptAt"/> already records.
/// </summary>
public sealed class LoginAttemptBucket : BaseEntity
{
    /// <summary>
    /// Bucket key: normalized email (lower-case NFC). Used as both
    /// <see cref="BaseEntity.Id"/> and the lookup index so we never need
    /// a separate primary key.
    /// </summary>
    public string EmailNormalized => Id;

    public int AttemptCount { get; private set; }

    public DateTimeOffset? LockedUntil { get; private set; }

    public DateTimeOffset LastAttemptAt { get; private set; }

    private LoginAttemptBucket() { }

    /// <summary>
    /// Create a fresh bucket. Caller passes the normalized email as the
    /// id — emails are unique within the table by definition.
    /// </summary>
    public static LoginAttemptBucket Create(string emailNormalized, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(emailNormalized);
        return new LoginAttemptBucket
        {
            Id = emailNormalized,
            AttemptCount = 0,
            LockedUntil = null,
            LastAttemptAt = now,
        };
    }

    /// <summary>
    /// Record a failed login attempt. Mirrors <see cref="User.RegisterFailedLogin"/>:
    /// no-op while already locked so an attacker can't extend the window
    /// from inside it; locks when <see cref="AttemptCount"/> hits
    /// <paramref name="threshold"/>.
    /// </summary>
    public LoginAttemptBucket RegisterFailedAttempt(DateTimeOffset now, int threshold, TimeSpan window)
    {
        if (IsLocked(now)) return this;

        AttemptCount += 1;
        LastAttemptAt = now;
        if (AttemptCount >= threshold)
        {
            LockedUntil = now + window;
        }
        return this;
    }

    /// <summary>Reset on a successful login (counter + lockout clear).</summary>
    public LoginAttemptBucket Reset(DateTimeOffset now)
    {
        AttemptCount = 0;
        LockedUntil = null;
        LastAttemptAt = now;
        return this;
    }

    public bool IsLocked(DateTimeOffset now) => LockedUntil is not null && LockedUntil > now;
}
