using Makables.Core.Domain.Common;

namespace Makables.Core.Domain.Identity;

/// <summary>
/// Single-use opaque token shared by every Phase-2 email-bound auth flow:
/// magic link, email confirmation, password reset. Per ADR 0012 — all
/// three sections specify the same shape ("32 bytes URL-safe base64;
/// SHA-256 stored; TTL N min; single-use").
///
/// Storage shape:
///   - <see cref="BaseEntity.Id"/> = the SHA-256 hex of the raw token.
///     Lookup is a primary-key probe; we never store the raw value.
///   - <see cref="UserId"/> ties the token to its account (the consume
///     handler reloads the User by id and applies the side-effect).
///   - <see cref="ConsumedAt"/> flips to non-null on first redemption;
///     subsequent attempts must observe this and refuse.
///
/// Inherits <see cref="BaseEntity"/> not <see cref="Auditable"/> because
/// these tokens are short-lived single-use rows, not transactional data.
/// </summary>
public sealed class OneTimeToken : BaseEntity
{
    public string UserId { get; private set; } = default!;
    public OneTimeTokenPurpose Purpose { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? ConsumedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// IP that requested the token. Optional; populated when the request
    /// context exposes one. Useful for ops forensics on suspicious flows.
    /// </summary>
    public string? IpAddress { get; private set; }

    private OneTimeToken() { }

    /// <summary>
    /// Issue a new token. <paramref name="tokenHash"/> is the SHA-256 hex
    /// of the raw token the caller emails; the raw value never reaches
    /// this entity.
    /// </summary>
    public static OneTimeToken Issue(
        string tokenHash,
        string userId,
        OneTimeTokenPurpose purpose,
        DateTimeOffset expiresAt,
        DateTimeOffset now,
        string? ipAddress = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        if (expiresAt <= now)
            throw new ArgumentOutOfRangeException(nameof(expiresAt), "Must be in the future.");

        return new OneTimeToken
        {
            Id = tokenHash,
            UserId = userId,
            Purpose = purpose,
            ExpiresAt = expiresAt,
            CreatedAt = now,
            IpAddress = ipAddress is null ? null : Truncate(ipAddress, 64),
        };
    }

    /// <summary>
    /// Mark consumed. Throws if already consumed — the caller's flow
    /// must observe <see cref="IsConsumed"/> before calling. Reuse is
    /// not an attack vector here (single-use; no family to revoke) but
    /// the guard prevents accidental double-credit.
    /// </summary>
    public OneTimeToken Consume(DateTimeOffset now)
    {
        if (ConsumedAt is not null)
            throw new InvalidOperationException("Token has already been consumed.");
        ConsumedAt = now;
        return this;
    }

    public bool IsConsumed => ConsumedAt is not null;
    public bool IsExpired(DateTimeOffset now) => ExpiresAt <= now;
    public bool IsRedeemable(DateTimeOffset now) => !IsConsumed && !IsExpired(now);

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
