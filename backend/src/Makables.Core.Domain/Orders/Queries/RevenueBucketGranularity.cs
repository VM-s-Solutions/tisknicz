namespace Makables.Core.Domain.Orders.Queries;

/// <summary>
/// Bucket width of a platform-revenue time series (T-0192). Deliberately a
/// closed set of four: each member names a Postgres <c>date_trunc</c> field,
/// so the read side maps the enum to a literal instead of accepting a
/// caller-supplied interval string. A caller cannot therefore ask for an
/// arbitrary bucket, and the SQL never interpolates untrusted text.
///
/// <para>
/// Truncation happens in the country's civil timezone, so <see cref="Day"/>
/// means "a Prague day" (23, 24 or 25 hours across a DST switch), not a
/// fixed 24-hour slice of UTC. <see cref="Week"/> is the ISO week — Monday
/// through Sunday — which is what <c>date_trunc('week', …)</c> yields and
/// what the payout batches already use for their numbering.
/// </para>
/// </summary>
public enum RevenueBucketGranularity
{
    /// <summary>One hour.</summary>
    Hour = 0,

    /// <summary>One civil day, midnight to midnight in the country's timezone.</summary>
    Day = 1,

    /// <summary>One ISO week, Monday to Sunday.</summary>
    Week = 2,

    /// <summary>One calendar month.</summary>
    Month = 3,
}
