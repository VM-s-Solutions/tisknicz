namespace Makables.Infra.Database.Orders;

/// <summary>
/// Keyless row shape of the T-0192 revenue-series aggregate. Exists only
/// because the query cannot be written in LINQ: it truncates
/// <c>paid_at</c> to a bucket in a named civil timezone
/// (<c>date_trunc(field, timestamptz, zone)</c>, PostgreSQL 16), and the
/// Npgsql EF provider has no translation for that three-argument form. The
/// house answer to "EF cannot express it" is a parameterised
/// <c>FromSqlInterpolated</c> (see <c>MakerRepository</c>,
/// <c>ProductRepository</c>, <c>NumberingSequenceAllocator</c>), and that
/// needs a mapped type to project into.
///
/// <para>
/// Mapped <c>HasNoKey().ToView(null)</c>: no table, no key, never tracked,
/// never migrated. It is a projection target, not an entity — nothing
/// outside <see cref="OrderQueries"/> may reference it, which is why it is
/// <c>internal</c>.
/// </para>
///
/// <para>
/// <b>The global soft-delete filter does not apply to a keyless type</b>,
/// so the raw SQL carries <c>is_active</c> in its own WHERE clause. That
/// duplication is the price of the raw query and is pinned by an
/// integration test (a soft-deleted order must not appear in any bucket) —
/// if the global filter's meaning ever changes, that test fails here.
/// </para>
/// </summary>
internal sealed class PlatformRevenueBucketRow
{
    public DateTimeOffset BucketStart { get; init; }

    public int PaidOrderCount { get; init; }

    public long GrossVolumeMinor { get; init; }

    public long PlatformFeeMinor { get; init; }

    public long MakerPayoutMinor { get; init; }

    public long RefundedMinor { get; init; }
}
