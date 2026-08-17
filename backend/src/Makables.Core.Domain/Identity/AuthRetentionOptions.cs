namespace Makables.Core.Domain.Identity;

/// <summary>
/// Retention window for the expired auth artifacts purged by T-0114's
/// <c>DataRetentionCleanup</c> Function. Bound from the <c>Auth:Retention</c>
/// configuration section.
/// </summary>
public sealed class AuthRetentionOptions
{
    public const string SectionName = "Auth:Retention";

    /// <summary>
    /// Days an <em>already-expired</em> refresh token / one-time token /
    /// login-attempt bucket is kept before it is deleted.
    ///
    /// <para>
    /// The default is deliberately longer than any auth artifact's own
    /// lifetime (refresh tokens are the longest at 30 days per ADR 0012): the
    /// grace period exists so a support or abuse investigation still has the
    /// recent trail, not so the row stays useful. Callers clamp to ≥ 1 day —
    /// a misconfigured 0 must never delete an artifact the moment it expires.
    /// </para>
    /// </summary>
    public int ExpiredArtifactRetentionDays { get; set; } = 30;
}
