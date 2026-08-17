using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Makables.Functions.Identity;

/// <summary>
/// Timer-triggered Function that deletes expired auth artifacts — refresh
/// tokens, one-time tokens, login-attempt buckets — once they are past the
/// <see cref="AuthRetentionOptions.ExpiredArtifactRetentionDays"/> grace window
/// (T-0114, ADR 0023 §retention).
///
/// <para>
/// This is a data-protection job, not housekeeping. Each of the three tables
/// stores personal data that no business process needs after the artifact
/// expires: refresh tokens keep the IP and user-agent of every session ever
/// issued, one-time tokens keep the requesting IP, and login-attempt buckets
/// are keyed BY the email address — including addresses that never registered,
/// because ADR 0012 §Lockout deliberately consumes ghost slots to stop account
/// enumeration. Left alone, all three accumulate forever.
/// </para>
///
/// <para>
/// Deliberately narrow: order, invoice and payout data have statutory
/// retention (accounting law) and are NOT touched here. The GDPR erasure path
/// for a specific person is a different mechanism entirely — the admin
/// <c>DeleteUserPermanently</c> command (T-0110) and the self-service account
/// deletion — and stays the answer to a subject request. This job is the
/// standing minimisation sweep that runs whether or not anyone asks.
/// </para>
///
/// <para>
/// Idempotent by construction: deleting rows that are already gone is a no-op,
/// so a re-run, an overlapping run, or a retry after a partial failure all
/// converge. The Function holds no state between runs.
/// </para>
///
/// <para>
/// <b>Schedule:</b> weekly Sunday 03:00 UTC — the quietest window, and offset
/// from the nightly jobs (<c>CancelExpiredPendingPaymentOrders</c> 02:00,
/// <c>EvictExpiredRegistryCache</c> 02:30) per the codebase's load-spreading
/// convention. Configured via the <c>DataRetentionCleanup:Schedule</c> app
/// setting.
/// </para>
/// </summary>
public sealed class DataRetentionCleanupFunction(
    IAuthRetentionStore retentionStore,
    IOptions<AuthRetentionOptions> retentionOptions,
    IClock clock,
    ILogger<DataRetentionCleanupFunction> logger)
{
    public const string FunctionName = "DataRetentionCleanup";

    /// <summary>
    /// Floor for the retention window. A misconfigured 0 or negative value
    /// would delete an artifact the moment it expired, taking the recent
    /// abuse trail with it — clamp instead of trusting configuration
    /// (the T-0113 <c>EvictExpiredRegistryCache</c> precedent).
    /// </summary>
    public const int MinimumRetentionDays = 1;

    [Function(FunctionName)]
    public async Task RunAsync(
        [TimerTrigger("%DataRetentionCleanup:Schedule%")] TimerInfo timer,
        CancellationToken cancellationToken)
    {
        var retentionDays = Math.Max(
            MinimumRetentionDays,
            retentionOptions.Value.ExpiredArtifactRetentionDays);
        var expiredBefore = clock.UtcNow - TimeSpan.FromDays(retentionDays);

        var result = await retentionStore.PurgeExpiredAsync(expiredBefore, cancellationToken);

        logger.LogInformation(
            "DataRetentionCleanup completed: removed {RefreshTokens} refresh token(s), "
            + "{OneTimeTokens} one-time token(s), {LoginAttemptBuckets} login-attempt bucket(s) "
            + "expired before {ExpiredBefore:o} ({RetentionDays}-day retention).",
            result.RefreshTokens,
            result.OneTimeTokens,
            result.LoginAttemptBuckets,
            expiredBefore,
            retentionDays);
    }
}
