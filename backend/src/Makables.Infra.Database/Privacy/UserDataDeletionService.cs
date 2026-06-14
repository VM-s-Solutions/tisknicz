using Makables.Core.Domain.Addresses;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Privacy;
using Makables.Core.Domain.Reviews;
using Microsoft.EntityFrameworkCore;

namespace Makables.Infra.Database.Privacy;

/// <summary>
/// <see cref="IUserDataDeletionService"/> impl (T-0110) — the single
/// orchestration seam for GDPR erasure. Runs the full matrix (anonymize +
/// hard-delete) inside the CALLER'S unit of work; NEVER calls
/// <c>SaveChangesAsync</c> (the command's UoW pipeline commits atomically).
/// The ONLY place EF Core <c>Remove()</c> runs against <c>User</c> data
/// (ADR 0013 §"Hard delete (GDPR)"). See patterns §A.23 +
/// extension-points.md §14 for the disposition matrix.
///
/// <para>
/// Invoices are touched by ZERO code here — GDPR Art. 17(3)(b) legal-
/// obligation exemption (immutable tax records). They are never loaded for
/// mutation; an integration test asserts the byte-for-byte invariant.
/// </para>
/// </summary>
public sealed class UserDataDeletionService(MakablesDbContext db) : IUserDataDeletionService
{
    private static readonly OrderState[] InFlightStates =
    [
        OrderState.PendingPayment,
        OrderState.Paid,
        OrderState.Accepted,
        OrderState.Shipped,
    ];

    public async Task<BusinessResult> EraseAsync(string userId, CancellationToken ct)
    {
        // Resolve the user's maker row first (if any) — needed for the
        // maker-as-seller in-flight check + the PII anonymization. Tracked +
        // IgnoreQueryFilters: a soft-deleted maker is still erasable.
        var maker = await db.Set<Maker>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.UserId == userId, ct);

        // === 1. In-flight-order guard (runs FIRST, before any mutation) ===
        // Belt-and-braces: the handler already pre-flighted this, but the
        // architect seam owns the guarantee that no irreversible mutation
        // runs while money/fulfilment is in motion (patterns §A.23 rule 1).
        // IgnoreQueryFilters: a soft-deleted-but-in-flight order still blocks.
        var hasInFlight = await db.Set<Order>()
            .IgnoreQueryFilters()
            .Where(o => o.CustomerUserId == userId
                || (maker != null && o.MakerId == maker.Id))
            .AnyAsync(o => InFlightStates.Contains(o.State), ct);
        if (hasInFlight)
        {
            return BusinessResult.Failure(
                Error.Conflict("orders", BusinessErrorMessage.UserCannotDeleteWithInFlightOrders));
        }

        // === 2. Anonymize pass (replace-in-place sentinel) ===

        // Order contact snapshots (the order is a retained commercial record;
        // only the PII columns are scrubbed). IgnoreQueryFilters so soft-
        // deleted orders are anonymized too.
        var orders = await db.Set<Order>()
            .IgnoreQueryFilters()
            .Where(o => o.CustomerUserId == userId)
            .ToListAsync(ct);
        foreach (var order in orders)
        {
            order.AnonymizeContact();
        }

        // Review author de-identification (content stays — it's about the
        // maker). IgnoreQueryFilters so soft-deleted reviews are scrubbed too.
        var reviews = await db.Set<Review>()
            .IgnoreQueryFilters()
            .Where(r => r.CustomerUserId == userId)
            .ToListAsync(ct);
        foreach (var review in reviews)
        {
            review.AnonymizeAuthor();
        }

        // Maker PII (retain IČO + bank account, set IsRetainedForLegal).
        maker?.AnonymizeForErasure();

        // === 3. Hard-delete pass ===

        // RefreshTokens — session credentials, no legal-retention case.
        // IgnoreQueryFilters so soft-deleted tokens are removed too.
        var refreshTokens = await db.Set<RefreshToken>()
            .IgnoreQueryFilters()
            .Where(rt => rt.UserId == userId)
            .ToListAsync(ct);
        if (refreshTokens.Count > 0)
        {
            db.Set<RefreshToken>().RemoveRange(refreshTokens);
        }

        // Unreferenced addresses created by the user — deleted only when no
        // live maker still references them (the maker legal-seat address
        // stays referenced). IgnoreQueryFilters so the candidate + the
        // referencing-maker probe both see all rows.
        var referencedAddressIds = await db.Set<Maker>()
            .IgnoreQueryFilters()
            .Select(m => m.RegisteredAddressId)
            .ToListAsync(ct);
        var userAddresses = await db.Set<Address>()
            .IgnoreQueryFilters()
            .Where(a => a.CreatedBy == userId && !referencedAddressIds.Contains(a.Id))
            .ToListAsync(ct);
        if (userAddresses.Count > 0)
        {
            db.Set<Address>().RemoveRange(userAddresses);
        }

        // The User row — the ONLY user hard-delete in the system.
        // IgnoreQueryFilters so a soft-deleted user is reached.
        var user = await db.Set<User>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is not null)
        {
            db.Set<User>().Remove(user);
        }

        // === 4. Invoices — RETAINED untouched. No read, no write. ===

        // No SaveChangesAsync — the command's UoW pipeline commits the
        // anonymizations + hard-deletes atomically.
        return BusinessResult.Success();
    }
}
