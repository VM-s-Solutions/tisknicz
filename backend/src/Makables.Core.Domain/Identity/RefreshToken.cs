using Makables.Core.Domain.Common;

namespace Makables.Core.Domain.Identity;

/// <summary>
/// Server-side refresh token record. Per ADR 0012 §Refresh token:
///   - Stored as SHA-256(<em>raw token</em>) — the raw token is only in the
///     HttpOnly cookie shipped to the client.
///   - TTL 30 days; single-use; rotated on every successful refresh.
///   - All tokens issued from the same login share <see cref="FamilyId"/>;
///     detecting reuse of a revoked token revokes the whole family
///     (catches stolen-token replay).
/// </summary>
public sealed class RefreshToken : Auditable
{
    /// <summary>
    /// Default refresh-token lifetime per ADR 0012 §Refresh token (30 days).
    /// Single source of truth for Login / Refresh / ConsumeMagicLink so
    /// the value doesn't drift between flows.
    /// </summary>
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromDays(30);

    public string UserId { get; private set; } = default!;
    public string TokenHash { get; private set; } = default!;
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public string? ReplacedByTokenId { get; private set; }
    public string FamilyId { get; private set; } = default!;
    public string? UserAgent { get; private set; }
    public string? IpAddress { get; private set; }

    private RefreshToken() { }

    /// <summary>
    /// Issue a brand-new refresh token at login. The family id is generated
    /// fresh; all subsequent rotations carry it forward.
    /// </summary>
    public static RefreshToken IssueNew(
        string id,
        string userId,
        string tokenHash,
        string familyId,
        DateTimeOffset expiresAt,
        string countryCode,
        string? userAgent,
        string? ipAddress)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(familyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(countryCode);

        return new RefreshToken
        {
            Id = id,
            UserId = userId,
            TokenHash = tokenHash,
            FamilyId = familyId,
            ExpiresAt = expiresAt,
            UserAgent = Truncate(userAgent, 500),
            IpAddress = Truncate(ipAddress, 64),
            CountryCode = countryCode.ToUpperInvariant(),
        };
    }

    /// <summary>
    /// Rotate this token: mark it revoked and link forward to its
    /// replacement. The replacement is created by the caller via
    /// <see cref="IssueRotation"/>.
    /// </summary>
    public RefreshToken MarkRotated(string replacementTokenId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(replacementTokenId);
        if (RevokedAt is not null)
            throw new InvalidOperationException("Token is already revoked; reuse detection should fire.");
        RevokedAt = now;
        ReplacedByTokenId = replacementTokenId;
        return this;
    }

    /// <summary>Issue the next token in the family during a refresh exchange.</summary>
    public RefreshToken IssueRotation(
        string newId,
        string newTokenHash,
        DateTimeOffset newExpiresAt,
        string? userAgent,
        string? ipAddress)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newId);
        ArgumentException.ThrowIfNullOrWhiteSpace(newTokenHash);

        return new RefreshToken
        {
            Id = newId,
            UserId = UserId,
            TokenHash = newTokenHash,
            FamilyId = FamilyId,
            ExpiresAt = newExpiresAt,
            UserAgent = Truncate(userAgent, 500),
            IpAddress = Truncate(ipAddress, 64),
            CountryCode = CountryCode,
        };
    }

    /// <summary>Explicit revoke (logout or family-wide compromise response).</summary>
    public RefreshToken Revoke(DateTimeOffset now)
    {
        if (RevokedAt is not null) return this;
        RevokedAt = now;
        return this;
    }

    public bool IsActiveAt(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;

    private static string? Truncate(string? value, int max) =>
        value is null ? null : value.Length <= max ? value : value[..max];
}
