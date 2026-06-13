using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Payouts;

namespace Makables.Core.AppServices.Features.Payouts;

/// <summary>
/// Generates the financial artifacts a <see cref="PayoutBatch"/> produces
/// once it exists (T-0102b): one <c>InvoiceType.Fee</c> invoice per maker
/// per batch (rendered PDF + blob upload + maker email enqueue) and the
/// bank-transfer CSV. Invoked by <see cref="CreatePayoutBatch.Handler"/>
/// post-claim and on the re-run path (T-0068b service-precedent shape; the
/// handler stays thin).
///
/// <para>
/// <b>Failure posture (§C.3).</b> Never throws past itself except on
/// cancellation: the first per-step failure aborts the remaining work, is
/// logged Critical, and reflected in <see cref="PayoutArtifactResult.Complete"/>
/// being false. The handler still returns success so the claim + completed
/// artifacts commit; the re-run path resumes (re-entrancy §C.4).
/// </para>
/// </summary>
public interface IPayoutArtifactService
{
    /// <summary>
    /// Generate (or resume) the artifacts for <paramref name="batch"/>.
    /// <paramref name="claimedOrders"/> is supplied on the FIRST-run path —
    /// the just-claimed (in-memory, tracked, not-yet-committed) orders, so
    /// the service does not re-query the DB for rows the surrounding UoW has
    /// not committed. On the RE-RUN path it is null and the service reads the
    /// committed claim from the DB.
    /// </summary>
    Task<PayoutArtifactResult> GenerateAsync(
        PayoutBatch batch,
        IReadOnlyList<Order>? claimedOrders,
        CancellationToken cancellationToken);
}

/// <summary>
/// Outcome of one <see cref="IPayoutArtifactService.GenerateAsync"/> call.
/// <see cref="Complete"/> is true only when every maker's Fee invoice +
/// PDF + email is present AND the CSV is attached.
/// </summary>
public sealed record PayoutArtifactResult(bool Complete, int FeeInvoiceCount, bool CsvReady)
{
    /// <summary>A fully-artifacted batch re-run: nothing to do, everything present.</summary>
    public static PayoutArtifactResult AlreadyComplete(int feeInvoiceCount) =>
        new(Complete: true, FeeInvoiceCount: feeInvoiceCount, CsvReady: true);
}
