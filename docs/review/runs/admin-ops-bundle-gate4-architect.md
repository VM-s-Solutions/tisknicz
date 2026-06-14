# Gate 4 — Architect verdict: admin-ops bundle (`feat/admin-ops-bundle`)

Reviewer: Solution Architect. Scope: `IUserDataDeletionService` seam alignment (extension-points §14 / patterns §A.23) + the Review→User FK-drop ruling.

## 1. Seam alignment — PASS

`EraseAsync(string userId, CancellationToken) → BusinessResult` matches §14 exactly:

- **Single orchestration method** — one method on `IUserDataDeletionService`. ✓
- **`BusinessResult` return** (non-generic), `Error.Conflict("orders", UserCannotDeleteWithInFlightOrders)` on guard rejection. ✓
- **In-flight guard is pass #1**, before any mutation — covers customer AND maker-seller roles, `IgnoreQueryFilters` so a soft-deleted-but-in-flight order still blocks. Belt-and-braces over the handler's pre-flight, which is correct: the seam owns the guarantee. ✓
- **Fixed 4-pass order** — guard → anonymize (Order contact via `AnonymizeContact`, Review author via `AnonymizeAuthor`, Maker PII via `AnonymizeForErasure`) → hard-delete (RefreshTokens, unreferenced Addresses, User) → invoices never loaded. ✓
- **One UoW, no `SaveChangesAsync()`** — service stages tracked changes; returns `Success()`; pipeline commits atomically. ✓
- **Unreferenced-Address probe** correctly excludes addresses still referenced by any maker's `RegisteredAddressId` (legal-seat address stays). ✓
- **One-shot/irreversible** — User row gone after first run; re-call returns `user.notFound` via the handler load. ✓

No drift. The implementation is faithful to the seam contract.

## 2. Review→User FK-drop — **ACCEPT** (load-bearing ruling)

**The Order-precedent finding (decisive):** `OrderConfiguration.cs` maps `CustomerUserId` as a plain `HasMaxLength(40).IsRequired()` property with **no `HasOne<User>()` relationship** — Order's only enforced FK is to `PayoutBatch` (RESTRICT). So **Order already carries a denormalized, non-enforced author id to User.** The FK drop makes `Review.CustomerUserId` *consistent with the existing Order pattern* — it does not invent a new posture, it removes an inconsistency.

**Vs the alternatives:**
- (a) **SetNull** — requires making `customer_user_id` nullable, which contradicts the §14 replace-in-place sentinel strategy (the column stays NOT NULL, overwritten with `"Anonymized"`). It would also diverge from Order, which is NOT NULL + denormalized. Rejected.
- (b) **Keep FK + anonymize-only (never hard-delete user)** — directly violates the §14 erasure matrix (`User` = HARD-DELETE, the anchor row). A retained User row is a GDPR Art. 17 failure. Rejected.
- (c) **FK drop (denormalized id)** — the chosen call. A Restrict FK makes "overwrite author id with a non-id sentinel + hard-delete the user" a guaranteed 23503 violation. Dropping the FK is the only option that lets the matrix run. **Accepted.**

**No referential-integrity regression:** Review's real anchor is `order_id` (Cascade FK to orders) + `maker_id` (Cascade FK to makers); both enforced FKs remain. `customer_user_id` is display/audit-only and becomes a sentinel post-anonymization — never joined for integrity, only `ix_reviews_customer_user` for reads. The migration `Up` drops `FK_reviews_users_customer_user_id` and `Down` restores it (reversible). ✓

**Codified pattern note (see below):** "denormalized user-author id, no enforced FK, for erasure-compatibility."

## 3. `Maker.IsRetainedForLegal` — PASS

Migration `20260614151458_AddMakerIsRetainedForLegal`: `is_retained_for_legal boolean NOT NULL DEFAULT false` on `makers`, mapped on `MakerConfiguration` (`HasDefaultValue(false).IsRequired()`). Exactly the §14 schema addition. `AnonymizeForErasure()` sets it true while retaining IČO + BankAccount. ✓

## 4. Collaborator budget (ADR 0015) — within budget

`DeleteUserPermanently.Handler` ctor: `IUserRepository`, `IMakerRepository`, `IOrderRepository`, `IUserDataDeletionService`, `IUserSessionProvider` = 5 collaborators. The handler is a thin orchestrator (fail-closed → load → retype → interlock → delegate); the erasure matrix is entirely behind the seam, which holds a single collaborator (`MakablesDbContext`). The split is the correct RDD shape — handler knows *whether* to erase, the seam knows *how*. Within budget; no flag.

## Codification action (pattern note)

Fold into the §A.23 / §14 living docs: **"Denormalized user-author id (no enforced FK), for erasure-compatibility"** — a column that records *who* (Order.CustomerUserId, Review.CustomerUserId) but carries no Restrict FK to `users`, so the GDPR hard-delete of the anchor User row can run while the sentinel overwrites the author id in place. Enforced integrity is provided by the aggregate's *real* anchor FK (order_id), not by the author id. This is now a two-entity pattern (Order, Review), not a one-off.

## Verdict: PASS — merge approved.
