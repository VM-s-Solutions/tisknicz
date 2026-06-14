# Admin-ops bundle (T-0108 + T-0109 + T-0110 + T-0111) — Final Review

**Branch:** `feat/admin-ops-bundle` · 8 commits `2a9ee86`..`9e7f5c0` · base `master`
**Verdict:** **APPROVE WITH FOLD** — one non-blocking RDD role-file parity fold (Gate 7). No correctness, security, atomicity, TDD, or i18n-parity BLOCKER. The headline (T-0110 erasure atomicity) and the FK-drop deviation both pass.

---

## (a) Verdict

The bundle is correct, atomic, secured, and TDD-clean. Build 0/0, unit **1719 passed / 0 failed** (re-run locally — the three red-first pure-logic surfaces are now green), frontend `tsc` exit 0. Integration **235 (reduced parallelism, xUnit.MaxParallelThreads=2)** accepted on the implementer's authoritative run note (Testcontainers connection-limit constraint; not re-spun here). Every mandatory check 1–14 traced to file:line. One fold (role-file RDD parity) does not block approval but must land before merge.

## (b) Erasure-atomicity + FK-drop dispositions

**Erasure atomicity (HIGH-1 — THE HEADLINE): PASS.**
`UserDataDeletionService.EraseAsync` (`Privacy/UserDataDeletionService.cs:37-135`) runs the WHOLE matrix in tracked changes and **never calls `SaveChangesAsync`** — the command's `UnitOfWorkPipelineBehavior` commits once, so a mid-pass throw rolls back EVERYTHING (no half-erased user). Verified: in-flight guard pass-1 inside the seam (`:46-60`), anonymize Order/Review/Maker (`:62-88`), hard-delete RefreshToken/unreferenced-Address/User (`:90-128`), **Invoices touched by zero code** (`:130`, no `Set<Invoice>()` read/write anywhere in the seam). The single `Remove()`-on-`User` path in the system is here (`:127`). Integration test `POST_erase_runs_full_matrix...` asserts: user gone under `IgnoreQueryFilters` (`:256`), orders→`"Anonymized"` with pricing intact (`:263-265`), maker IČO `27074358` + bank `123456789/0100` retained + `IsRetainedForLegal==true` (`:273-278`), refresh tokens gone, unreferenced address gone / seat address retained (`:280-288`), **invoice byte-for-byte intact** — `RecipientName`/`RecipientEmail`/`AmountWithVatMinor` re-asserted post-erase (`:290-294`), audit row survives referencing the deleted id (`:296-302`).
*Minor gap (fold, not blocker):* there is no dedicated forced-mid-matrix-throw rollback test. Atomicity is structurally guaranteed (one UoW, no in-seam commit) and the in-flight path proves all-or-nothing on the guard branch, but the explicit injected-failure rollback HIGH-1 contemplated is not separately pinned. Recommend adding one in a follow-up; not a merge blocker given the structural guarantee + green matrix test.

**FK-drop (DEVIATION 3): ACCEPT.**
Migration `20260614151458_AddMakerIsRetainedForLegal.cs:13-15` drops `FK_reviews_users_customer_user_id`; `Down()` correctly reinstates it (`:32-38`) — reversible. `ReviewConfiguration.cs:92-99` declares NO enforced FK on `CustomerUserId` with an explicit rationale: an enforced Restrict FK would make BOTH the anonymize-overwrite (sentinel is not a real user id) and the user hard-delete a 23503/Restrict violation; mirrors `Order.CustomerUserId` (denormalized author id, no FK). ModelSnapshot agrees — the Review entity block carries only `ix_reviews_customer_user` (plain index, line ~2372) and NO `HasOne(User)`; the `AuthorUserId`/Restrict `HasOne(User)` at snapshot `:2402` belongs to **OrderMessage**, not Review (verified by block context). Orphan-integrity intact: Review keeps its `order_id` FK (Cascade) + `maker_id` FK (Cascade) for joins. This is the cleanest path vs the SetNull-nullable alternative (which would lose the denormalized author id and break the Order-mirror invariant). **ACCEPT — pending the architect's parallel Gate-4 note; if the architect rejects, this flips to BLOCKER.**

## (c) BLOCKERs

**None.** No correctness, atomicity, security, TDD, or i18n-parity blocker. (FK-drop accept is contingent on the architect's Gate-4 confirmation.)

## (d) Fold list (land before merge)

1. **Gate 7 / RDD parity (ADR 0015) — role files stale for new responsibilities.** The bundle adds responsibilities not reflected in their role files: `maker.md` has zero mention of `AnonymizeForErasure`/`IsRetainedForLegal` tombstone; `outbox.md` zero mention of `RequeueForRetry`/admin force-retry; `country-configuration.md` zero mention of the `IProviderRegistry` seam/retype gate. Per my workflow step 5: *"If a role's responsibility changed, the role file is updated in the same PR."* Canonical docs (patterns §A.23, extension-points §14) WERE updated and fully document the matrix + seam, so this is a secondary-index parity fold, not a correctness defect. Add the three role-file notes (+ a one-line `IAdminQueries` cross-ref) before merge.
2. **(Optional, recommended)** add a forced-mid-matrix-throw rollback integration test for the erasure seam (HIGH-1) — structural guarantee already holds; this pins it.

## (e) Checks (1–14)

1. **Gate 5 red-first: PASS.** `1d772af` is tests-only (6 files, 410 ins) and precedes all impl (`8752a05`+). Ladder test `RequeueForRetry_does_NOT_reset_the_backoff_ladder` (`OutboxEventTests.cs:174-203`) pins locked A.1 (count 4→5, next failure resumes at `TransientBackoffs[5]` not `[0]`); in-flight set + `Anonymize*` transforms red-first in `1d772af`.
2. **T-0110 atomicity: PASS** (see (b)).
3. **Dual-role in-flight interlock: PASS.** Seam predicate `o.CustomerUserId == userId || (maker != null && o.MakerId == maker.Id)` (`UserDataDeletionService.cs:53-54`); repo `HasInFlightOrderForUserAsync` (`OrderRepository.cs:339-359`) covers both, fail-closed on empty. Test `In_flight_order_blocks_409_and_nothing_is_mutated` pins 409 + nothing mutated + no audit row (`:305-328`).
4. **Irreversibility + audit survival: PASS.** No "already-erased→200" branch; re-call→`user.notFound` 404, single surviving audit row (`Re_call_after_successful_erase...:349-372`).
5. **Retype gate: PASS.** `User.NormalizeEmail(ConfirmedEmail) != user.EmailNormalized` after load, before in-flight/seam (`DeleteUserPermanently.cs:112-117`); mismatch test (`:330-347`).
6. **FK-drop: ACCEPT** (see (b)).
7. **T-0111 admin-only: PASS.** `AddMakablesAuth.cs:130` → `Admin => [Audiences.Admin]` only; `[Authorize]` on all admin controllers; `.Unscoped()` not called from any Customer/Maker/Public source file; customer-JWT→401 test (`:374-395`); `.IgnoreQueryFilters()` calls commented.
8. **T-0108 provider gate: PASS.** Unregistered-check (step 5) BEFORE retype gate (step 6); no-op cheap-first (step 4); WARN-not-block advisory (step 7); `GetByCodeForUpdateAsync` tracked, `GetByCodeAsync` stays `AsNoTracking` (`CountryConfigurationRepository.cs:9-37`).
9. **T-0109 backoff preservation: PASS.** `RequeueForRetry` increments via `checked()`, no ladder reset, throws on processed (`OutboxEvent.cs:128-134`); red test pins it.
10. **i18n parity (HARVESTED #2): PASS — zero gap.** All 8 keys present in `cs-CZ.ts`: `outbox.rowNotFound`, `outbox.alreadyProcessed`, `country.providerNotRegistered`, `country.providerConfirmationMismatch`, `countryConfiguration.notFound`, `user.notFound`, `user.deleteConfirmationMismatch`, `user.cannotDeleteWithInFlightOrders` (`:530-548`). No 4th-hit harvest needed.
11. **Unique-index-translator (#3): N/A confirmed.** `is_retained_for_legal` is plain `BOOLEAN NOT NULL DEFAULT false`; no `IsUnique()` in the bundle migration.
12. **Deviations (6): all judged OK.** DTO in `Domain/Admin` (correct per layering); `GetByCodeForUpdateAsync` (tracked-load fix, read path preserved); FK-drop (ACCEPT); dual in-flight guard (defensive belt-and-braces, intended per §A.23); ProviderRegistry built from `IServiceCollection` at composition root (Infra-only, no runtime container reach-through); harness truncate (test-infra).
13. **Provider-registry email fallback: T-0124 note, NOT a T-0108 defect.** Seed `default_email_provider='resend'` (InitialSchema `:125`) vs `ProviderRegistry.EmailCodes={ "sendgrid" }` (`:33`); unregistered-check only fires on a CHANGED provider field, no AC/test drives an email change, and email is explicitly `// TODO(T-0124)` static fallback. Latent reconciliation item, correctly out of T-0108 scope.
14. **Build + tests: PASS.** `dotnet build Makables.Api.slnx` 0/0; unit **1719/0** (local re-run); frontend `tsc` exit 0; consistency delta = exactly **+7** bundle T1 static-class-wrapper false-positives (GetAdminAuditLog, GetAllInvoices, GetAllOrders, UpdateCountryConfiguration, AcknowledgeOutboxEvent, RetryOutboxEvent, DeleteUserPermanently); NSwag regen is **admin-only** (`admin-api.v1.ts` + `.spec-hashes.json`; customer/maker client deltas in `master..HEAD` belong to the prior reviews-loop merge, not this PR). Integration 235 accepted on authoritative reduced-parallelism note.

**Gates:** G1 lint/build ✓ · G2 layering ✓ (no SaveChangesAsync in handlers/seam; no IServiceProvider in Core.AppServices) · G3 SecOps — admin-audience-only + Unscoped reachability confirmed (defer to SecOps sign-off artifact) · G4 Architect — FK-drop ACCEPT pending architect's parallel note · G5 TDD red-first ✓ · G6 contract parity (admin NSwag regen) ✓ · G7 docs — **FOLD** (role-file RDD parity).
