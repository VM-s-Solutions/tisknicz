using Makables.Core.Domain.Common;

namespace Makables.Core.Domain.Payouts;

/// <summary>
/// A weekly maker-payout batch: the set of payout-eligible
/// <c>Delivered</c> orders for one country claimed into one immutable unit
/// per ADR 0009 + US-admin-0007. The batch total is the sum of each
/// claimed order's <c>MakerPayoutAmountMinor</c> — the amount the operator
/// wires from the company bank account.
///
/// <para>
/// <b>Immutable once created (lock A.4).</b> Born directly in
/// <see cref="PayoutBatchState.Processing"/> (no observable Pending —
/// creation is atomic in one UoW). No order removal, no
/// <c>UpdateAsync</c>/<c>DeleteAsync</c> on the repository: the per-batch
/// Fee invoices issued at creation (T-0102b) are legally immutable, so
/// removing an order would orphan an issued invoice. The only mutations
/// are the set-once <see cref="AttachCsvBlobPath"/> and the T-0103
/// completion (via EF change tracking; <c>Complete()</c> ships with its
/// only caller in T-0103, no dead code now). The
/// <see cref="CompletedAt"/>/<see cref="CompletedBy"/> columns ship now so
/// T-0103 is migration-free.
/// </para>
///
/// <para>
/// <b>Denormalised counts.</b> <see cref="OrderCount"/>,
/// <see cref="MakerCount"/>, and the three exclusion counts are a
/// creation-time snapshot — the batch is immutable so they can never drift
/// from the claimed rows. Storing them avoids COUNT joins on every admin /
/// maker render and makes the row self-describing in audit exports.
/// </para>
///
/// <para>
/// Admin-only aggregate per ADR 0013 — repository methods use the
/// <c>Unscoped</c> naming convention. Soft-delete query filter applies
/// (Auditable base), though the row is never destroyed per role doc.
/// </para>
/// </summary>
public sealed class PayoutBatch : Auditable
{
    /// <summary>Wire-shape of the batch-number column.</summary>
    public const int MaxBatchNumberLength = 40;

    /// <summary>Wire-shape of the CSV blob path column.</summary>
    public const int MaxCsvBlobPathLength = 500;

    /// <summary>Wire-shape of the completed-by column.</summary>
    public const int MaxCompletedByLength = 200;

    /// <summary>
    /// Customer-facing batch number <c>VYP-{CC}-{YYYY}-W{ww}</c>, immutable
    /// after creation. Unique per country via the
    /// <c>ux_payout_batches_country_batch_number</c> index per ADR 0009 —
    /// the number is a pure function of country + ISO week, so the index is
    /// the race-free uniqueness enforcement (no NumberingSequence row).
    /// </summary>
    public string BatchNumber { get; private set; } = default!;

    public PayoutBatchState State { get; private set; }

    /// <summary>Sum of each claimed order's maker payout, minor units (ADR 0003).</summary>
    public long TotalAmountMinor { get; private set; }

    /// <summary>ISO 4217 currency code (CHAR(3)). The batch's default-country currency.</summary>
    public string Currency { get; private set; } = default!;

    /// <summary>Number of orders claimed into this batch. Creation-time snapshot.</summary>
    public int OrderCount { get; private set; }

    /// <summary>Number of distinct makers paid by this batch. Creation-time snapshot.</summary>
    public int MakerCount { get; private set; }

    /// <summary>
    /// Count of Delivered orders excluded because they were partially
    /// refunded (<c>RefundedAmountMinor &gt; 0</c>) at claim time (Q3).
    /// Surfaced in the response + audit; they ride the next batch after
    /// admin resolution.
    /// </summary>
    public int ExcludedPartiallyRefundedOrderCount { get; private set; }

    /// <summary>
    /// Count of Delivered orders excluded because their maker has no bank
    /// account on file at claim time (Q5).
    /// </summary>
    public int ExcludedNoBankAccountOrderCount { get; private set; }

    /// <summary>Count of distinct makers excluded for the same NULL-bank-account reason (Q5).</summary>
    public int ExcludedNoBankAccountMakerCount { get; private set; }

    /// <summary>
    /// Blob path of the generated bank-transfer CSV. Null until T-0102b's
    /// <see cref="AttachCsvBlobPath"/> sets it after a successful upload.
    /// Format: <c>payouts/{cc}/{batchNumber}.csv</c>. Set-once.
    /// </summary>
    public string? CsvBlobPath { get; private set; }

    /// <summary>When the operator marked the batch settled (T-0103). Null while Processing.</summary>
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>Admin user id who marked the batch settled (T-0103). Null while Processing.</summary>
    public string? CompletedBy { get; private set; }

    // EF Core needs a parameterless ctor.
    private PayoutBatch() { }

    /// <summary>
    /// Create a new payout batch born in <see cref="PayoutBatchState.Processing"/>
    /// (lock A.4). Throws <see cref="ArgumentException"/> for impossible
    /// inputs: blank id/batchNumber/countryCode, currency length ≠ 3,
    /// <paramref name="totalAmountMinor"/> ≤ 0, <paramref name="orderCount"/>
    /// &lt; 1, <paramref name="makerCount"/> &lt; 1,
    /// <paramref name="makerCount"/> &gt; <paramref name="orderCount"/>, or
    /// any negative exclusion count. An empty batch never reaches this
    /// factory — the claim handler short-circuits with
    /// <see cref="BusinessErrorMessage.PayoutBatchEmpty"/> and writes no row.
    /// </summary>
    public static PayoutBatch Create(
        string id,
        string batchNumber,
        string countryCode,
        long totalAmountMinor,
        string currency,
        int orderCount,
        int makerCount,
        int excludedPartiallyRefundedOrderCount,
        int excludedNoBankAccountOrderCount,
        int excludedNoBankAccountMakerCount)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Id is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(batchNumber))
            throw new ArgumentException("BatchNumber is required.", nameof(batchNumber));
        if (string.IsNullOrWhiteSpace(countryCode) || countryCode.Length != 2)
            throw new ArgumentException("CountryCode must be 2 chars (ISO 3166-1 alpha-2).", nameof(countryCode));

        var trimmedNumber = batchNumber.Trim();
        if (trimmedNumber.Length > MaxBatchNumberLength)
            throw new ArgumentException($"BatchNumber must be at most {MaxBatchNumberLength} chars.", nameof(batchNumber));

        if (string.IsNullOrWhiteSpace(currency) || currency.Length != 3)
            throw new ArgumentException("Currency must be a 3-char ISO 4217 code.", nameof(currency));

        if (totalAmountMinor <= 0)
            throw new ArgumentException("TotalAmountMinor must be positive (an empty batch never creates a row).", nameof(totalAmountMinor));
        if (orderCount < 1)
            throw new ArgumentException("OrderCount must be at least 1.", nameof(orderCount));
        if (makerCount < 1)
            throw new ArgumentException("MakerCount must be at least 1.", nameof(makerCount));
        if (makerCount > orderCount)
            throw new ArgumentException("MakerCount cannot exceed OrderCount.", nameof(makerCount));

        if (excludedPartiallyRefundedOrderCount < 0)
            throw new ArgumentException("ExcludedPartiallyRefundedOrderCount cannot be negative.", nameof(excludedPartiallyRefundedOrderCount));
        if (excludedNoBankAccountOrderCount < 0)
            throw new ArgumentException("ExcludedNoBankAccountOrderCount cannot be negative.", nameof(excludedNoBankAccountOrderCount));
        if (excludedNoBankAccountMakerCount < 0)
            throw new ArgumentException("ExcludedNoBankAccountMakerCount cannot be negative.", nameof(excludedNoBankAccountMakerCount));

        return new PayoutBatch
        {
            Id = id,
            BatchNumber = trimmedNumber,
            CountryCode = countryCode.ToUpperInvariant(),
            State = PayoutBatchState.Processing,
            TotalAmountMinor = totalAmountMinor,
            Currency = currency.ToUpperInvariant(),
            OrderCount = orderCount,
            MakerCount = makerCount,
            ExcludedPartiallyRefundedOrderCount = excludedPartiallyRefundedOrderCount,
            ExcludedNoBankAccountOrderCount = excludedNoBankAccountOrderCount,
            ExcludedNoBankAccountMakerCount = excludedNoBankAccountMakerCount,
        };
    }

    /// <summary>
    /// Attach the generated CSV blob path. Set-once with idempotent
    /// same-value semantics, mirroring <c>Invoice.AttachPdfBlobPath</c>
    /// (T-0068a):
    /// <list type="bullet">
    ///   <item>First call with a non-null value → set + success.</item>
    ///   <item>Second call with the SAME value → no-op + success (the CSV
    ///     formatter is deterministic; a re-run produces the same path).</item>
    ///   <item>Second call with a DIFFERENT value →
    ///     <see cref="BusinessErrorMessage.PayoutBatchCsvPathAlreadySet"/>.</item>
    /// </list>
    /// </summary>
    public BusinessResult AttachCsvBlobPath(string csvBlobPath)
    {
        if (string.IsNullOrWhiteSpace(csvBlobPath))
            throw new ArgumentException("CsvBlobPath is required.", nameof(csvBlobPath));

        var trimmed = csvBlobPath.Trim();
        if (trimmed.Length > MaxCsvBlobPathLength)
            throw new ArgumentException(
                $"CsvBlobPath must be at most {MaxCsvBlobPathLength} chars.",
                nameof(csvBlobPath));

        if (CsvBlobPath is not null)
        {
            if (string.Equals(CsvBlobPath, trimmed, StringComparison.Ordinal))
            {
                return BusinessResult.Success();
            }

            return BusinessResult.Failure(
                Error.Conflict("csvBlobPath", BusinessErrorMessage.PayoutBatchCsvPathAlreadySet));
        }

        CsvBlobPath = trimmed;
        return BusinessResult.Success();
    }
}
