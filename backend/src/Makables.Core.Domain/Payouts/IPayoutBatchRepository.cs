namespace Makables.Core.Domain.Payouts;

/// <summary>
/// Persistence access for <see cref="PayoutBatch"/>. <b>Admin host only</b>
/// per ADR 0013 — the aggregate is a privileged money-movement record;
/// maker-facing payout reads are T-0112's separate query seam.
///
/// <para>
/// <b>No <c>UpdateAsync</c>, no <c>DeleteAsync</c></b> (lock A.4): the batch
/// is immutable once created. The only post-creation mutations — the
/// set-once <c>AttachCsvBlobPath</c> (T-0102b) and the T-0103 completion —
/// flow through EF Core change tracking on the tracked aggregate returned
/// by <see cref="GetByIdUnscopedAsync"/> / <see cref="GetOpenBatchAsync"/>,
/// committed by the <c>UnitOfWorkPipelineBehavior</c>. Soft-delete query
/// filtering is automatic (Auditable base), though the row is never
/// destroyed per the role doc.
/// </para>
/// </summary>
public interface IPayoutBatchRepository
{
    /// <summary>Track <paramref name="batch"/> as a pending insert.</summary>
    Task AddAsync(PayoutBatch batch, CancellationToken cancellationToken);

    /// <summary>
    /// Load a single batch by id without ownership scoping. <b>Admin host
    /// only.</b> Tracked — callers may mutate (T-0102b artifact attach,
    /// T-0103 completion). Calls <c>.IgnoreQueryFilters()</c> so admin /
    /// reconciliation paths see soft-deleted rows.
    /// </summary>
    Task<PayoutBatch?> GetByIdUnscopedAsync(string id, CancellationToken cancellationToken);

    /// <summary>
    /// The re-run guard: returns the single open
    /// <see cref="PayoutBatchState.Processing"/> batch for
    /// <paramref name="countryCode"/>, or null when none is open. The
    /// partial unique index <c>ux_payout_batches_open_per_country</c>
    /// guarantees at most one. Tracked — the re-run path may resume the
    /// batch's artifacts (T-0102b). Admin host only.
    /// </summary>
    Task<PayoutBatch?> GetOpenBatchAsync(string countryCode, CancellationToken cancellationToken);

    /// <summary>
    /// Load a batch by its <c>(country_code, batch_number)</c> pair, or
    /// null. Backs the same-ISO-week re-run guard (a closed batch for this
    /// week makes the create command return
    /// <c>payoutBatch.weekAlreadyProcessed</c> rather than dying on the
    /// unique index). Tracked. Admin host only.
    /// </summary>
    Task<PayoutBatch?> GetByNumberAsync(string countryCode, string batchNumber, CancellationToken cancellationToken);
}
