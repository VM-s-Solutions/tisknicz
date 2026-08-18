namespace Makables.Core.Domain.Identity;

/// <summary>
/// Retention purge for the three auth side-tables that grow without bound
/// and hold personal data no business process needs once the artifact has
/// expired (T-0114, ADR 0023 §retention):
///
/// <list type="bullet">
///   <item><c>refresh_tokens</c> — carries <c>ip_address</c> + <c>user_agent</c>
///   per issued session.</item>
///   <item><c>one_time_tokens</c> — carries <c>ip_address</c> per magic-link /
///   confirmation / reset request.</item>
///   <item><c>login_attempt_buckets</c> — keyed BY the normalized email, so a
///   row exists for every address anyone ever tried to log in with,
///   <em>including addresses that never registered</em> (the ghost-slot
///   behaviour ADR 0012 §Lockout deliberately relies on).</item>
/// </list>
///
/// <para>
/// This is a hard DELETE, not the soft delete the <c>Auditable</c> tables use:
/// the point is that the personal data stops existing. Nothing references these
/// rows — <c>replaced_by_token_id</c> is a plain column, not an FK — and an
/// expired artifact can never be redeemed, so there is nothing to preserve.
/// </para>
///
/// <para>
/// Implementations run outside the request unit of work on their own
/// <c>DbContext</c> (the <c>ICompanyRegistryCacheStore</c> precedent from
/// T-0032/T-0113) — the caller is a timer Function with no ambient scope.
/// </para>
/// </summary>
public interface IAuthRetentionStore
{
    /// <summary>
    /// Delete every refresh token and one-time token that expired strictly
    /// before <paramref name="expiredBefore"/>, and every login-attempt bucket
    /// whose last attempt <em>and</em> lockout both predate it.
    /// </summary>
    Task<AuthRetentionPurgeResult> PurgeExpiredAsync(
        DateTimeOffset expiredBefore,
        CancellationToken cancellationToken);
}

/// <summary>
/// Per-table row counts removed by one
/// <see cref="IAuthRetentionStore.PurgeExpiredAsync"/> run. Logged by the
/// Function; no PII, so it is safe in a log template.
/// </summary>
public sealed record AuthRetentionPurgeResult(
    int RefreshTokens,
    int OneTimeTokens,
    int LoginAttemptBuckets)
{
    public int Total => RefreshTokens + OneTimeTokens + LoginAttemptBuckets;

    public static AuthRetentionPurgeResult Empty { get; } = new(0, 0, 0);
}
