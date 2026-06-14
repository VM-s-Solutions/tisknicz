using Makables.Core.Domain.Common;

namespace Makables.Core.Domain.Privacy;

/// <summary>
/// The single orchestration seam for GDPR "right to erasure" (Art. 17),
/// invoked by the <c>DeleteUserPermanently</c> handler (T-0110, admin-ops
/// bundle). Designs the service named — but never specified — in ADR 0013
/// §"Hard delete (GDPR)". See <c>docs/architecture/extension-points.md §14</c>
/// + patterns §A.23 for the full erasure matrix.
///
/// <para>
/// <b>The ONLY place EF Core <c>Remove()</c> runs against <c>User</c> data.</b>
/// The Reviewer enforces that no other handler calls <c>Remove()</c> on a
/// <c>User</c>.
/// </para>
/// </summary>
public interface IUserDataDeletionService
{
    /// <summary>
    /// Run the full erasure matrix (guard → anonymize → hard-delete) for
    /// <paramref name="userId"/> inside the CALLER'S unit of work. Does NOT
    /// call <c>SaveChangesAsync</c> — the command's UoW pipeline owns the
    /// commit, so the anonymizations and hard-deletes are atomic (a
    /// mid-matrix failure rolls everything back).
    ///
    /// <para>
    /// Fixed pass order (patterns §A.23):
    /// <list type="number">
    ///   <item>In-flight-order guard — if the user (as customer OR maker) has
    ///   any order in <c>[PendingPayment, Paid, Accepted, Shipped]</c>, returns
    ///   <c>BusinessResult.Failure(user.cannotDeleteWithInFlightOrders)</c>
    ///   and erases nothing.</item>
    ///   <item>Anonymize — Order contact snapshots, Review author, Maker PII.</item>
    ///   <item>Hard-delete — <c>RefreshToken</c>s, unreferenced <c>Address</c>es,
    ///   the <c>User</c> row.</item>
    ///   <item>Invoices — RETAINED untouched (never loaded for mutation).</item>
    /// </list>
    /// One-shot, NOT retry-safe by design: after the first run the
    /// <c>User</c> row is gone, so the caller's re-call fails at load.
    /// </para>
    /// </summary>
    Task<BusinessResult> EraseAsync(string userId, CancellationToken ct);
}
