---
id: T-0110
title: DeleteUserPermanently command (GDPR hard-delete + PII erasure matrix; the only hard-delete in the system)
status: ready
size: M
owner: dotnet-backend
created: 2026-06-14
updated: 2026-06-14
depends_on: [T-0033, T-0060, T-0111, T-0109, T-0108]
blocks: [T-0118]
user_stories: [US-admin-0016]
adrs: [0013, 0014]
phase: 5
manual_steps: []
security_touching: true
layers: [domain, appservices, infra-database, web-admin]
---

# T-0110 — DeleteUserPermanently command (GDPR hard-delete + PII erasure matrix)

## Context

T-0110 is the **last ticket in the admin-completion bundle** (T-0111 read-only admin queries → T-0109 outbox retry/acknowledge → T-0108 country-config update → T-0110 GDPR hard-delete; sequential, one PR, ordered risk-ascending — the irreversible hard-delete is built last on top of the verified-by-T-0111 read harness). It satisfies **US-admin-0016 — GDPR delete a user**: AC-1 (the erasure matrix runs in one transaction) and AC-2 (in-flight orders block the deletion). It is **the single most sensitive ticket in the backlog** — the ONLY place in the entire system where EF Core `Remove()` + commit runs against user data, the only PII-erasure path, and the only command that is genuinely irreversible. Per ADR 0013 §"Hard delete (GDPR)" this command is the named, audited, single code path; the Reviewer enforces that no other handler calls `Remove()` on a `User`.

The destructive orchestration does NOT live in the handler. Per the parallel architect engagement (§Architect-review below), the erasure matrix is owned by a dedicated `IUserDataDeletionService.EraseAsync(userId)` seam that runs the entire anonymize-then-hard-delete sequence inside **one** unit of work. T-0110's handler is a thin orchestrator: it loads the user, runs two gates (retype-confirmation + in-flight-order interlock), invokes the seam, and lets `AdminAuditPipelineBehavior` record WHO erased WHOM. This keeps the handler reviewable at a glance and isolates the hard-delete blast radius behind one interface the architect designs and the Reviewer audits.

The erasure matrix is asymmetric by legal design (Q-A, user-locked 2026-06-14 deliberation; ADR 0013 + US-admin-0016 locked):

| Entity | Disposition | Rationale |
|---|---|---|
| `User` row | **HARD-DELETE** (`Remove()`) | The data subject. GDPR Art. 17 erasure. |
| `RefreshToken` rows (the user's) | **HARD-DELETE** | Session credentials; no legal-retention case. |
| `Address` rows (the user's, **unreferenced**) | **HARD-DELETE** | Only when no other live entity FKs them (a maker's legal-seat address stays). |
| `Order.ContactName / ContactEmail / ContactPhone` | **ANONYMIZE** → `"Anonymized"` | The order itself is a tax/commercial record; only the PII snapshot columns are scrubbed. |
| `Review` author (`Author` projection) | **ANONYMIZE** → `"Anonymized"` | The review content stays (it's about the maker); authorship is de-identified. |
| `Maker` PII (`CompanyName`, contact, etc.) | **ANONYMIZE**; `IČO` (`RegistrationNumber`) + `BankAccount` **RETAINED**, `IsRetainedForLegal = true` | Tax records reference the IČO + payout account; flag marks the row as a legally-retained tombstone. |
| `Invoice` rows | **RETAIN UNTOUCHED** | GDPR Art. 17(3)(b) legal-obligation exemption — immutable tax records. The repo has no Update/Delete surface (role/invoice.md); T-0110 does not touch a single invoice column. |
| `AdminAuditLogEntry` (this command's own row) | **SURVIVES** | Not the user's data — it records the admin action. References the now-deleted `UserId` as `TargetId`; that dangling reference is correct and intentional (an admin-action log, not an FK). |

Two gates protect the operation. **Gate 1 (retype):** the admin must retype the user's email into `ConfirmedEmail`; a mismatch returns `user.deleteConfirmationMismatch` (mirrors the T-0108 retype-the-provider-code idiom). **Gate 2 (in-flight interlock, Q-B):** if the user — as customer OR maker — has any order in `[PendingPayment, Paid, Accepted, Shipped]`, the command is REJECTED with `user.cannotDeleteWithInFlightOrders`; the admin must resolve (refund / cancel / deliver) those orders first. The interlock is the real safety mechanism — it prevents erasing the contact snapshot off an order whose money or fulfilment is still moving.

`Web.Admin` gains controllers across the bundle (T-0111 first, then T-0109/T-0108); T-0110 adds the erase endpoint. Audit before/after JSONB + reason come free from `AdminAuditPipelineBehavior` via `IAdminAuditableCommand` (ADR 0014); `Reason` maps to the audit `Notes`. No outbox events and no emails are emitted (PM default — a GDPR erasure is silent; notifying a just-deleted user is both pointless and a re-identification risk). Fail-closed session check applies (never attribute a hard-delete to "system").

## Locked design decisions

Captured per `docs/process/deliberation.md`. The user locked the erasure matrix + interlock (Q-A, Q-B) in the 2026-06-14 batched deliberation. PM-absorbed defaults follow the bundle's shared conventions (all four commands `IAdminAuditableCommand`, fail-closed session, no `SaveChangesAsync` in handlers, reason cap 2000, security_touching YES) and the T-0107/VerifyMaker audited-command precedents.

### A. User-locked (2026-06-14 deliberation, non-negotiable)

1. **Q-A — the erasure matrix** (table in Context). HARD-DELETE `User` + `RefreshToken` + **unreferenced** `Address`; ANONYMIZE `Order` contact snapshots (`ContactName/Email/Phone → "Anonymized"`) + `Review` author + `Maker` PII (RETAIN `IČO` + `BankAccount`, set `IsRetainedForLegal = true`); RETAIN `Invoice` rows **UNTOUCHED** (GDPR Art. 17(3)(b) legal-obligation exemption — the invoice repo exposes no Update/Delete). **Rejected:** hard-delete everything FK'd to the user (destroys tax records + maker-aggregate reporting — illegal under Czech accounting-record retention); soft-delete the user only (does not satisfy a right-to-erasure request — the PII is still in the row).
2. **Q-B — single `DeleteUserPermanently` command, retype gate + in-flight interlock.** The admin retypes the user's email to confirm (mirrors the T-0108 retype idiom); the real interlock is a REJECT with `user.cannotDeleteWithInFlightOrders` when the user (as customer OR maker) has any order in `[PendingPayment, Paid, Accepted, Shipped]` — admin resolves those first. **Rejected:** two-phase "request erasure → confirm after cooldown" workflow (over-engineered for 2 trusted admins at MVP; the retype + interlock are sufficient friction); allow deletion regardless of order state (strands money / fulfilment on an order whose contact snapshot just got scrubbed).

### B. ADR-locked (no relitigation)

- **ADR 0013 §"Hard delete (GDPR)".** This command is the named single hard-delete path; it calls `IUserDataDeletionService` (the architect-designed seam — §Architect-review). `Remove()` against user data runs ONLY inside that service. The user is loaded **Unscoped + `IgnoreQueryFilters()`** (a soft-deleted/deactivated user can still be erased — a deactivation does not satisfy erasure; the comment at the call site documents the bypass per ADR 0013 §`IgnoreQueryFilters`).
- **ADR 0014 (admin audit).** `Command : IAdminAuditableCommand` → `AdminAuditPipelineBehavior` captures before/after JSONB and appends the `AdminAuditLogEntry` only on success. `ActionCode = "user.erase"`, `TargetEntity = "user"`, `TargetId = UserId`, `Notes => Reason`. The audit row is NOT the user's data — it survives the deletion and references the now-deleted `UserId` (correct; it's an admin-action log). Per Q-0021 (architect ruling, this engagement): a no-op/failed call writes no audit row only because the behavior skips on `!IsSuccess` — there is no special "no second audit row" assertion to make here (the matrix is irreversible, so re-calls fail at load with `user.notFound`, never reaching the behavior's success branch).
- **`BusinessResult<T>` + centralized `BusinessErrorMessage`.** Every failure (`user.notFound`, `user.deleteConfirmationMismatch`, `user.cannotDeleteWithInFlightOrders`) is a typed code. One-file feature shape; the handler never calls `SaveChangesAsync()` — the `UnitOfWorkPipelineBehavior` commits the single transaction the seam staged.
- **`[Authorize]` admin-host gate + fail-closed session check** in the handler (VerifyMaker / T-0107 precedent — a hard-delete must never be attributed to "system").

### C. PM-absorbed (no user input needed)

- **Erasure seam is architect-owned.** `IUserDataDeletionService` interface declared in `Core.Domain/Identity/`; implementation in `Infra.Database/Identity/`. `EraseAsync(string userId, CancellationToken)` orchestrates the FULL matrix in ONE UoW: (1) anonymize `Order` snapshots → `"Anonymized"`; (2) anonymize `Review` author; (3) anonymize `Maker` PII + set `IsRetainedForLegal = true` (RETAIN `RegistrationNumber` + `BankAccount`); (4) hard-delete `RefreshToken` rows; (5) hard-delete **unreferenced** `Address` rows; (6) hard-delete the `User` row; (7) `Invoice` rows — **no read, no write**. The service does NOT call `SaveChangesAsync` — the command's UoW pipeline owns the commit boundary (the service stages tracked changes; the pipeline commits atomically). **The exact internal contract of this seam is the architect's deliverable; T-0110 consumes it.**
- **Anonymization placeholder = the literal `"Anonymized"`** (US-admin-0016 AC-1 wording). Applied to `Order.ContactName / ContactEmail / ContactPhone`, `Review` author, and the `Maker` PII fields. Pure transform — testable in isolation (see Tests).
- **In-flight predicate is pure logic:** `InFlightOrderStates = { PendingPayment, Paid, Accepted, Shipped }`. The interlock query asks the order repo "does ANY order where `(CustomerUserId == userId OR MakerId == maker.Id)` have `State ∈ InFlightOrderStates`?" — a single existence check, not a list. The set itself is a static readonly collection on the feature so the test and the query share one source of truth. **TDD red-first: the in-flight predicate + the anonymization transforms commit before implementation** (T-0067+ pure-logic rule).
- **New `Maker.IsRetainedForLegal` column** (`BOOLEAN NOT NULL DEFAULT false`). EF migration `AddMakerIsRetainedForLegal`. Default false; the erasure seam flips it to true on the anonymized maker tombstone. New entity method `Maker.AnonymizeForErasure()` (sets PII fields to `"Anonymized"`, keeps `RegistrationNumber` + `BankAccount`, sets `IsRetainedForLegal = true`) so the transform lives on the aggregate, not in the service.
- **Email normalization on the retype gate.** `ConfirmedEmail` is compared against `User.EmailNormalized` via `User.NormalizeEmail(command.ConfirmedEmail)` — the admin can type any case; the comparison is case/NFC-insensitive (mirrors the login lookup invariant). Mismatch → `user.deleteConfirmationMismatch`.
- **NO Silent-Success on re-call.** The operation is irreversible; a second call finds no user (the row is gone) and returns `user.notFound` — there is no idempotent "already erased → 200" branch (unlike T-0108/T-0109's same-value/already-acknowledged no-op). This is the one bundle command that does NOT get Silent-Success.
- **New `BusinessErrorMessage` codes (2 new; 1 reused):** `UserNotFound = "user.notFound"`, `UserDeleteConfirmationMismatch = "user.deleteConfirmationMismatch"`. **Reused:** `UserCannotDeleteWithInFlightOrders = "user.cannotDeleteWithInFlightOrders"` (already on master in the `// === User ===` block). Parallel `cs-CZ` i18n keys for all three.
- **Endpoint:** `POST /api/v1/users/{id}/erase` (judge call — `POST .../erase` reads clearer than `DELETE` for a side-effecting, matrix-running operation that is NOT a plain resource delete; `DELETE` would imply REST idempotency this op deliberately does not have). Body `{ confirmedEmail, reason }`. `[Authorize]` admin audience. Globally-unique response `DeleteUserPermanentlyResponse(string ErasedUserId)`. NSwag regen: admin host client only.
- **Reason validation:** `NotEmpty` + `MaximumLength(2000)` (audit notes column width, VerifyMaker / T-0107 precedent). `ConfirmedEmail`: `NotEmpty` + `MaximumLength(200)` (matches `Order.MaxContactEmailLength` / user email column). No `MinimumLength` on reason — a GDPR ticket reference (e.g. "GDPR-2026-014") can be short; the audit row + before/after JSONB carry the forensic weight.

## Scope

### Domain layer

- **`Core.Domain/Identity/IUserDataDeletionService.cs`** — NEW interface (the architect-designed seam; T-0110 declares the consuming contract, the architect engagement finalizes the internal orchestration):
  ```csharp
  public interface IUserDataDeletionService
  {
      // Runs the full GDPR erasure matrix (anonymize + hard-delete) for the
      // given user inside the caller's unit of work. Does NOT call
      // SaveChangesAsync — the command's UoW pipeline owns the commit.
      // The ONLY place EF Core Remove() runs against User data (ADR 0013).
      Task EraseAsync(string userId, CancellationToken ct);
  }
  ```
- **`Core.Domain/Makers/Maker.cs`** — NEW method `AnonymizeForErasure()`:
  - Sets `CompanyName` (and any other maker PII) to `"Anonymized"`.
  - KEEPS `RegistrationNumber` (IČO) + `BankAccount` untouched.
  - Sets `IsRetainedForLegal = true`.
  - NEW property `public bool IsRetainedForLegal { get; private set; }` (default false).
- **`Core.Domain/Common/BusinessErrorMessage.cs`** — add to the `// === User ===` block: `UserNotFound = "user.notFound"`, `UserDeleteConfirmationMismatch = "user.deleteConfirmationMismatch"`. `UserCannotDeleteWithInFlightOrders` already present — reuse.

### AppServices layer

- **`Core.AppServices/Features/Users/DeleteUserPermanently.cs`** — NEW one-file feature:
  - `Command(string UserId, string ConfirmedEmail, string Reason) : ICommand<DeleteUserPermanentlyResponse>, IAdminAuditableCommand` with `ActionCode => "user.erase"`, `TargetEntity => "user"`, `TargetId => UserId`, `Notes => Reason`.
  - `DeleteUserPermanentlyResponse(string ErasedUserId)` — globally-unique name (NSwag PR #38 rule).
  - Static `InFlightOrderStates` (the pure-logic set: `PendingPayment`, `Paid`, `Accepted`, `Shipped`) — shared by the test and the interlock query.
  - `Validator`: `UserId` NotEmpty/Max 40; `ConfirmedEmail` NotEmpty/Max 200; `Reason` NotEmpty/Max 2000.
  - `Handler(IUserRepository users, IOrderRepository orders, IUserDataDeletionService deletion, IUserSessionProvider session)`:
    1. **Fail-closed session check** — `string.IsNullOrEmpty(session.GetUserId())` → `Error.Unauthorized()` (never attribute a hard-delete to "system" — VerifyMaker precedent).
    2. **Load user Unscoped** — `users.GetByIdIgnoringFiltersAsync(command.UserId, ct)` (`IgnoreQueryFilters()` — a soft-deleted user is still erasable; comment documents the bypass per ADR 0013). Null → `Error.NotFound("user")` `UserNotFound`.
    3. **Retype gate** — `User.NormalizeEmail(command.ConfirmedEmail) != user.EmailNormalized` → `Error.Conflict("confirmedEmail", UserDeleteConfirmationMismatch)`.
    4. **In-flight interlock (Q-B)** — existence check via the order repo: any order where `(CustomerUserId == user.Id OR the user's maker id matches MakerId)` AND `State ∈ InFlightOrderStates` → `Error.Conflict("orders", UserCannotDeleteWithInFlightOrders)`. (No mutation; the admin resolves first.)
    5. **Erase** — `await deletion.EraseAsync(user.Id, ct);` (the seam stages the full matrix in tracked changes).
    6. **Return** — `BusinessResult.Success(new DeleteUserPermanentlyResponse(user.Id))`. NO `SaveChangesAsync()` — the UoW pipeline commits; `AdminAuditPipelineBehavior` writes the audit row (which survives the user deletion).

### Infrastructure / Database layer

- **`Infra.Database/Identity/UserDataDeletionService.cs`** — NEW class implementing `IUserDataDeletionService` (the architect finalizes the orchestration; the consuming contract is fixed here):
  - Primary-constructor DI: `UserDataDeletionService(MakablesDbContext db) : IUserDataDeletionService`.
  - `EraseAsync` stages, in one UoW (no `SaveChangesAsync`):
    1. Anonymize the user's `Order` contact snapshots (`db.Orders.IgnoreQueryFilters().Where(o => o.CustomerUserId == userId)` → `"Anonymized"` on the three contact columns — via an `Order.AnonymizeContact()` entity method or tracked update; the architect picks the exact mutation surface, but the snapshot columns are the only ones touched).
    2. Anonymize `Review` author for the user's reviews.
    3. If the user is a maker: `maker.AnonymizeForErasure()` (PII → `"Anonymized"`, retain IČO + bank account, `IsRetainedForLegal = true`).
    4. `db.RefreshTokens.IgnoreQueryFilters().Where(rt => rt.UserId == userId)` → `Remove`/`RemoveRange`.
    5. Unreferenced `Address` rows owned by the user → `Remove` (skip any address still FK'd by a live entity — e.g. the maker legal-seat address stays referenced).
    6. The `User` row → `Remove` (the ONLY user hard-delete in the system).
    7. `Invoice` — untouched; no query, no mutation.
  - Every `IgnoreQueryFilters()` call carries a comment per ADR 0013 (erasure must reach soft-deleted rows too).
- **`Core.Domain/Identity/IUserRepository.cs`** — extend with `GetByIdIgnoringFiltersAsync(string id, CancellationToken)` (loads a user bypassing the soft-delete filter; comment documents the GDPR reason). If the repo already exposes an equivalent, reuse it.
- **`Core.Domain/Orders/IOrderRepository.cs`** — extend with an existence check for the in-flight interlock, e.g. `HasInFlightOrderForUserAsync(string customerUserId, string? makerId, IReadOnlyCollection<OrderState> states, CancellationToken)` returning `bool` (single `EXISTS` query, no list materialization). Implementer matches the existing repo method-naming conventions.
- **EF migration `AddMakerIsRetainedForLegal`** — `is_retained_for_legal BOOLEAN NOT NULL DEFAULT false` on `makers`. No other schema change.
- **`Config/Extensions/AddMakablesInfrastructure.cs`** — register `services.AddScoped<IUserDataDeletionService, UserDataDeletionService>();`.

### Web.Admin host

- **`Web.Admin/Controllers/UsersController.cs`** — NEW controller (or extend if the bundle created one): `[HttpPost("{id}/erase")]`, `[Authorize]` (admin audience per ADR 0013), `[ProducesResponseType(typeof(DeleteUserPermanentlyResponse), 200)]`, one-liner `Mediator.Send(new DeleteUserPermanently.Command(id, body.ConfirmedEmail, body.Reason), ct)`.

### Frontend

- **`frontend/src/lib/i18n/cs-CZ.ts`** — 3 error keys: `user.notFound` ("Uživatel nebyl nalezen."), `user.deleteConfirmationMismatch` ("Zadaný e-mail neodpovídá uživateli — smazání nebylo potvrzeno."), `user.cannotDeleteWithInFlightOrders` ("Uživatele nelze smazat — má rozpracované objednávky. Nejprve je vyřešte."). The third key may already exist; reuse if so.
- **`frontend/src/lib/api-client/admin-api.v1.ts`** — NSwag regen (admin host), committed in the same PR. No manual edits (pre-commit hook).

### Tests (~12 unit + ~5 integration)

`DeleteUserPermanentlyPredicateTests` + transform tests (red-first, pure logic; `backend/src/Makables.Tests/Domain/Identity/` + `.../Makers/`):

1. **InFlightOrderStates_contains_exactly_the_four_locked_states** — asserts the set is `{ PendingPayment, Paid, Accepted, Shipped }` and excludes `Delivered`, `Completed`, `Cancelled`, `Refunded`, `Disputed`.
2. **AnonymizeForErasure_scrubs_PII_keeps_ICO_and_bank_and_sets_legal_flag** — build a maker; call `AnonymizeForErasure()`; assert `CompanyName == "Anonymized"`, `RegistrationNumber` unchanged, `BankAccount` unchanged, `IsRetainedForLegal == true`.
3. **AnonymizeForErasure_is_idempotent** — a second call leaves the IČO/bank intact and the flag true (no exception).
4. **Order_contact_anonymization_sets_all_three_snapshot_columns** — the contact transform sets `ContactName == ContactEmail == ContactPhone == "Anonymized"` and touches no pricing/state column.

`DeleteUserPermanentlyHandlerTests` (NSubstitute mocks: `IUserRepository`, `IOrderRepository`, `IUserDataDeletionService`, `IUserSessionProvider`):

5. **Happy_path_invokes_erasure_seam_once_and_returns_erased_id** — session has admin id; user found; emails match; no in-flight orders. Assert `IUserDataDeletionService.EraseAsync(userId, ct)` called once; response `ErasedUserId == userId`.
6. **Missing_session_user_returns_Unauthorized** — `session.GetUserId()` null/empty → `Error.Unauthorized()`; seam NOT called.
7. **User_not_found_returns_UserNotFound** — repo returns null → `user.notFound`; seam NOT called.
8. **Email_mismatch_returns_deleteConfirmationMismatch** — `ConfirmedEmail` differs from the user's email → `user.deleteConfirmationMismatch`; seam NOT called.
9. **Email_match_is_case_and_whitespace_insensitive** — `ConfirmedEmail = " ADMIN@X.COM "` against stored `admin@x.com` → passes the retype gate (normalization).
10. **In_flight_order_blocks_erasure_cannotDeleteWithInFlightOrders** — order repo existence check returns true → `user.cannotDeleteWithInFlightOrders`; seam NOT called.
11. **No_in_flight_orders_proceeds** — existence check false → seam called.
12. **Re_call_after_erasure_returns_UserNotFound_not_silent_success** — second invocation, repo now returns null → `user.notFound` (asserts NO idempotent 200 branch — the irreversible-no-Silent-Success rule).
13. **Validator_rejects_empty_ConfirmedEmail_and_empty_Reason** — both required; `Reason` > 2000 chars → MaxLength failure.

`DeleteUserPermanentlyIntegrationTests` (Testcontainers Postgres + `WebApplicationFactory` + seeded fixtures; full erasure e2e):

1. **POST_erase_runs_full_matrix_user_gone_orders_anonymized_invoices_intact** — seed a maker-user with: 1 Delivered order (contact snapshot populated), 1 Completed order, 1 issued Invoice, 1 Review, 2 RefreshTokens, 1 Address. POST `/api/v1/users/{id}/erase` with the correct `confirmedEmail` + a reason, as admin. Assert 200; DB: `User` row gone (query with `IgnoreQueryFilters`), both orders' contact columns == `"Anonymized"` (other columns unchanged), the Review author anonymized, `Maker` PII anonymized with `RegistrationNumber` + `BankAccount` intact and `IsRetainedForLegal == true`, RefreshTokens gone, the unreferenced Address gone, **the Invoice row byte-for-byte unchanged**.
2. **In_flight_order_blocks_409_and_nothing_is_mutated** — seed the same user with one order in `Paid`. POST erase. Assert 409 `user.cannotDeleteWithInFlightOrders`; DB: user still present, orders/maker/invoices completely unchanged, RefreshTokens present (the seam never ran).
3. **Retype_mismatch_blocks_409_no_mutation** — POST with a wrong `confirmedEmail`. Assert 409 `user.deleteConfirmationMismatch`; nothing mutated.
4. **Re_call_after_successful_erase_returns_404_UserNotFound** — erase once (200), POST the same id again. Assert the second call resolves to `user.notFound` (no Silent-Success); the first erasure's `admin_audit_log` row survives.
5. **Audit_row_written_referencing_deleted_user** — after a successful erase, assert exactly one `admin_audit_log` row with `action_code = "user.erase"`, `target_entity = "user"`, `target_id = <the erased userId>`, `notes = <reason>`, `admin_user_id = <caller>`, and before/after JSONB present. The row references the now-deleted `UserId` (correct — admin-action log, not an FK).

### Docs

- **`docs/architecture/roles/user.md`** — document the erasure matrix + the `IUserDataDeletionService` seam (the single hard-delete path); cross-reference ADR 0013 §"Hard delete (GDPR)" and US-admin-0016. Note `Maker.IsRetainedForLegal` + `AnonymizeForErasure()`.
- **`docs/architecture/roles/maker.md`** — note the `IsRetainedForLegal` tombstone flag + that IČO/BankAccount are retained on erasure for tax records.
- **`docs/tickets/INDEX.md`** — PM flips T-0110 to done post-merge.

### NSwag regen

The new `POST /api/v1/users/{id}/erase` endpoint is a contract change → **NSwag regen REQUIRED in the same PR** (admin host client only). Per the pre-commit hook (T-0013), `frontend/src/lib/api-client/` cannot be edited manually — `npm run generate:api` produces the diff carrying `DeleteUserPermanentlyResponse` + the request body shape.

## Alternatives Considered

- **Option A — Orchestrate the erasure matrix inline in the handler.** *Rejected per ADR 0013 + C.1* — the handler would become a 60-line destructive sequence mixing query bypasses, `Remove()` calls, and anonymization across five aggregates; impossible to review at a glance and impossible for the Reviewer to assert "this is the ONLY hard-delete path". The `IUserDataDeletionService` seam isolates the entire blast radius behind one architect-owned, Reviewer-audited interface.
- **Option B — Hard-delete every entity FK'd to the user (orders, invoices, reviews, maker).** *Rejected per Q-A* — illegal. Czech accounting-record retention + GDPR Art. 17(3)(b) require immutable tax records (invoices) to survive; maker-aggregate reporting + the public review corpus must persist (de-identified). Anonymize-the-PII, retain-the-record is the lawful posture.
- **Option C — Soft-delete the user (set `IsActive = false`) and call it erasure.** *Rejected per Q-A* — a soft delete leaves every PII column in the row; it does not satisfy a right-to-erasure request. Hard-delete of the `User` row is the point.
- **Option D — `DELETE /api/v1/users/{id}` verb.** *Rejected per C* — `DELETE` implies idempotent resource removal; this op runs a multi-aggregate matrix, has a retype body + reason, and deliberately is NOT idempotent (a re-call is `user.notFound`, not a no-op 200). `POST .../erase` names the side-effecting operation honestly.
- **Option E — Silent-Success on re-call (return 200 if the user is already gone).** *Rejected per C* — Silent-Success is the right idempotency posture for T-0108 (same config values) and T-0109 (already-acknowledged), but a re-call here either targets a non-existent id (always `user.notFound`) or would falsely claim a second erasure happened. The one bundle command that does NOT get Silent-Success.
- **Option F — Skip the in-flight interlock; let the admin erase anytime.** *Rejected per Q-B* — scrubbing `ContactName/Email/Phone` off an order in `Paid` or `Shipped` strands the maker mid-fulfilment (no one to ship to) and breaks refund routing if money still needs to move. The interlock forces resolution first; it is the real safety mechanism, not the retype.
- **Option G — Confirmation modal only (no email retype).** *Rejected per Q-B* — a single "Are you sure?" click is too weak for the only irreversible action in the system. Retyping the exact user email forces the admin to confirm they have the right person (mirrors the T-0108 retype-the-provider-code high-stakes idiom).
- **Option H — Two-phase erasure (request → cooldown → confirm).** *Rejected per Q-B* — over-engineered for 2 trusted admins at MVP. The retype + in-flight interlock + mandatory audited reason are sufficient friction. Revisit if the admin population or regulatory exposure grows.
- **Option I — Anonymize maker PII AND scrub IČO + bank account.** *Rejected per Q-A* — the IČO + payout bank account are referenced by retained tax records (invoices, payout batches); scrubbing them would orphan those legal records. `IsRetainedForLegal = true` marks the row as a lawful tombstone that keeps exactly the columns tax law requires.

## Out of scope

- **The internal orchestration of `IUserDataDeletionService`** — owned by the parallel architect engagement. T-0110 declares + consumes the seam and asserts its observable effects (the matrix) via integration tests; the architect finalizes the staging order, the unreferenced-address detection, and the exact per-entity mutation surface.
- **T-0108 country-config update, T-0109 outbox retry/acknowledge, T-0111 admin read queries** — the other three bundle tickets; T-0110 ships after them in the same PR.
- **A self-service "delete my account" customer flow** — admin-only at MVP (a GDPR request goes through the admin per US-admin-0016). A customer-initiated erasure is a separate post-MVP ticket.
- **Credit-note / invoice errata on erasure** — invoices are RETAINED UNTOUCHED; no credit note is issued by an erasure.
- **Outbox events / emails on erasure** — none (PM default; notifying a just-deleted user is pointless + a re-identification risk).
- **Frontend erase UI (the admin confirmation screen with the email-retype field)** — T-0118 owns the admin views.
- **Bulk erasure / scheduled retention sweeps** — single-user, admin-triggered only at MVP.
- **Q-0011 (secops follow-up)** — TOUCHED not closed by this bundle (admin endpoints are admin-JWT-gated, 2 trusted users, lower spam risk than the customer surface Q-0011 was raised against). Q-0011 stays open as a standalone secops item; re-confirm at secops Gate 3. T-0110 does NOT expand scope to address it.

## Acceptance criteria

- **AC-1** Given a user with no in-flight orders, when admin POSTs `/api/v1/users/{id}/erase` with `confirmedEmail` matching the user's email and a non-empty `reason`, then 200 `{ erasedUserId: <id> }`; the `User` row is hard-deleted (absent even under `IgnoreQueryFilters`).
- **AC-2** Given the same successful erase, when the DB is inspected, then the user's `RefreshToken` rows are hard-deleted AND the user's unreferenced `Address` rows are hard-deleted.
- **AC-3** Given the same successful erase, when the user's orders are inspected, then every order's `ContactName`, `ContactEmail`, `ContactPhone` == `"Anonymized"`; no pricing, state, or timestamp column is changed.
- **AC-4** Given the user authored reviews, when erased, then those reviews' author is anonymized (`"Anonymized"`); the review rating + body are unchanged (the content is about the maker).
- **AC-5** Given the user is a maker, when erased, then the `Maker` PII fields are `"Anonymized"`, `RegistrationNumber` (IČO) + `BankAccount` are UNCHANGED, and `IsRetainedForLegal == true`.
- **AC-6** Given the user has issued invoices, when erased, then every `Invoice` row is byte-for-byte unchanged (no column touched). GDPR Art. 17(3)(b) retention.
- **AC-7** Given the user (as customer OR maker) has any order in `PendingPayment` / `Paid` / `Accepted` / `Shipped`, when erase is attempted, then 409 `user.cannotDeleteWithInFlightOrders`; NOTHING is mutated (user, orders, maker, invoices, refresh tokens all unchanged).
- **AC-8** Given `confirmedEmail` does NOT match the user's email (after normalization), when erase is attempted, then 409 `user.deleteConfirmationMismatch`; nothing mutated.
- **AC-9** Given `confirmedEmail` matches case-insensitively / NFC-normalized (e.g. ` ADMIN@X.COM ` vs stored `admin@x.com`), when erase is attempted with no in-flight orders, then the retype gate passes and the erasure proceeds.
- **AC-10** Given a non-existent user id (or one already erased), when erase is attempted, then `user.notFound` — NO Silent-Success 200. A second call after a successful erase resolves to `user.notFound`.
- **AC-11** Given a successful erase, when `admin_audit_log` is inspected, then exactly one row: `action_code = "user.erase"`, `target_entity = "user"`, `target_id = <erased id>`, `notes = <reason>`, `admin_user_id = <caller>`, before/after JSONB present. The row survives the user deletion and references the now-deleted id. Blocked (409) and validation (400) requests write no audit row.
- **AC-12** Given an anonymous or non-admin-audience JWT, when the endpoint is called, then 401 (host gate); given a request that reaches the handler with no session user, then fail-closed `Unauthorized` (no "system" attribution for a hard-delete).
- **AC-13** Given empty `confirmedEmail` OR empty `reason` OR `reason` > 2000 chars, when posted, then 400 with a FluentValidation error on the offending field. The erasure seam is never invoked.
- **AC-14** Build clean; ~12 new unit tests (the in-flight-state set + the anonymization transforms commit red-first — verifiable in commit history) + ~5 integration tests green; 2 new `BusinessErrorMessage` codes (`user.notFound`, `user.deleteConfirmationMismatch`) + reuse of `user.cannotDeleteWithInFlightOrders`, all 3 with parallel `cs-CZ` keys; the `AddMakerIsRetainedForLegal` migration applies cleanly; NSwag admin client regenerated in the same PR with no manual edits; `node scripts/check-consistency.mjs` exit 0. The Reviewer confirms `IUserDataDeletionService` is the ONLY code path calling `Remove()` on a `User`.

## Technical notes

### Why the erasure lives behind `IUserDataDeletionService` (not in the handler)

ADR 0013 §"Hard delete (GDPR)" names exactly one service as the single place EF Core `Remove()` runs against user data, "Reviewer enforces". Inlining the matrix would scatter `IgnoreQueryFilters()` bypasses + `Remove()` calls across the handler and make that enforcement impossible to audit. The seam gives the architect one file to design the staging order in, gives the Reviewer one interface to grep for, and gives T-0110 a thin, obviously-correct handler (load → two gates → invoke → return). The seam stages tracked changes; the command's `UnitOfWorkPipelineBehavior` commits them in one transaction — so the anonymizations and the hard-deletes are atomic (a mid-matrix failure rolls everything back, leaving the user intact).

### Why the in-flight interlock — not just the retype

The retype confirms WHO; the interlock confirms it is SAFE NOW. Scrubbing `ContactName/Email/Phone` off an order in `Shipped` leaves the maker with a parcel and no recipient; an order in `Paid` may still need a refund routed to a contact that just became `"Anonymized"`. The four blocked states are precisely the ones where money or fulfilment is still in motion; `Delivered`/`Completed`/`Cancelled`/`Refunded` are settled and safe to anonymize. The interlock is an existence check (`EXISTS`), not a list — cheap, and it short-circuits before the seam runs.

### Why no Silent-Success on re-call

T-0108 and T-0109 are idempotent by design (re-saving the same config / re-acknowledging an event is a benign no-op → 200). Erasure is not: the row is gone, so a re-call has nothing to act on. Returning 200 would falsely imply a second erasure occurred. `user.notFound` is the honest answer and also surfaces a double-click in the admin UI as a clear "this user is already gone" rather than a silent success.

### Why the audit row references a deleted UserId

`AdminAuditLogEntry.TargetId` is not a foreign key — it is a string record of "the admin acted on entity X". After erasure, X no longer exists, and that is exactly what the log should show: an immutable record that admin A erased user X at time T with reason R. The before/after JSONB captures the user's pre-erasure state for the forensic trail. Per Q-0021 (architect ruling), the audit behavior correctly writes on every success; there is no "suppress the audit row" requirement here.

### Why `Invoice` is touched by zero code

The invoice repository exposes no Update or Delete surface (role/invoice.md: "no updates after issuance"); the entity has no mutator beyond set-once `AttachPdfBlobPath`. T-0110 honors this by NOT reading or writing any invoice in the erasure seam — the GDPR Art. 17(3)(b) legal-obligation exemption means the right course of action is literally to do nothing to invoices. The integration test asserts the invoice row is byte-for-byte unchanged precisely to pin that "do nothing" contract.

## Risk

- **Security + correctness (CRITICAL — the only irreversible operation in the system).** A bug here destroys real user data with no undo, or — worse — silently fails to erase PII the platform is legally obligated to remove. Mitigations: admin-audience JWT (ADR 0013), `[Authorize]`, fail-closed session check, retype gate, in-flight interlock, the single-seam blast-radius isolation, mandatory audited reason, atomic UoW (all-or-nothing), and the architect-owned seam design. **Security review + architect review both required on the PR.**
- **Matrix incompleteness.** If a future entity gains a user-PII column not covered by the matrix, an erasure would leave PII behind. Mitigation: the integration test asserts the full matrix; the roles/user.md erasure-matrix table is the canonical checklist any new user-PII column must be added to.
- **Seam / handler contract drift.** T-0110 consumes `IUserDataDeletionService` whose internals the architect designs in parallel. Mitigation: the consuming contract (`EraseAsync(userId, ct)`, no `SaveChangesAsync`, stages-into-caller-UoW) is fixed in this ticket; the integration tests assert the observable matrix regardless of internal staging order. Implement T-0110 after the architect lands the seam in the same branch.
- **Bundle coupling.** T-0110 assumes T-0111 (admin read harness — verifies the post-erasure DB state), T-0109, and T-0108 shipped earlier in the same PR. Sequential implementation in one branch makes this safe; build T-0110 LAST (risk-ascending order).

## Test plan reference

Inline above (Scope > Tests). No separate `docs/test-plans/T-0110.md`.

## Files touched (expected)

**New:**
- `backend/src/Makables.Core.Domain/Identity/IUserDataDeletionService.cs`
- `backend/src/Makables.Core.AppServices/Features/Users/DeleteUserPermanently.cs`
- `backend/src/Makables.Infra.Database/Identity/UserDataDeletionService.cs`
- `backend/src/Makables.Web.Admin/Controllers/UsersController.cs`
- `backend/src/Makables.Infra.Database/Migrations/<timestamp>_AddMakerIsRetainedForLegal.cs`
- `backend/src/Makables.Tests/Domain/Identity/DeleteUserPermanentlyPredicateTests.cs`
- `backend/src/Makables.Tests/AppServices/Features/Users/DeleteUserPermanentlyHandlerTests.cs`
- `backend/src/Makables.IntegrationTests/Users/DeleteUserPermanentlyIntegrationTests.cs`

**Modified:**
- `backend/src/Makables.Core.Domain/Makers/Maker.cs` — `IsRetainedForLegal` property + `AnonymizeForErasure()`.
- `backend/src/Makables.Core.Domain/Common/BusinessErrorMessage.cs` — `UserNotFound`, `UserDeleteConfirmationMismatch` (reuse `UserCannotDeleteWithInFlightOrders`).
- `backend/src/Makables.Core.Domain/Identity/IUserRepository.cs` — `GetByIdIgnoringFiltersAsync`.
- `backend/src/Makables.Core.Domain/Orders/IOrderRepository.cs` — in-flight existence check.
- `backend/src/Makables.Config/Extensions/AddMakablesInfrastructure.cs` — register `IUserDataDeletionService`.
- `frontend/src/lib/i18n/cs-CZ.ts` — 3 error keys.
- `frontend/src/lib/api-client/admin-api.v1.ts` — NSwag regen (admin host).
- `docs/architecture/roles/user.md`, `docs/architecture/roles/maker.md` — erasure-matrix + tombstone notes.

## Commits hint

1. `test(T-0110): pin in-flight state set + AnonymizeForErasure + contact-anonymization transforms (red)`.
2. `feat(T-0110): Maker.IsRetainedForLegal + AnonymizeForErasure + EF migration + error codes (green)`.
3. `feat(T-0110): IUserDataDeletionService seam + UserDataDeletionService impl + DI registration`.
4. `feat(T-0110): DeleteUserPermanently feature + admin erase endpoint + i18n + NSwag regen`.
5. `test(T-0110): handler + full-matrix integration coverage`.

## §Architect-review: YES

The erasure matrix (Q-A) and the `IUserDataDeletionService` seam are **architect-owned deliverables** from the parallel architect engagement running this cycle. T-0110 declares the consuming contract (`EraseAsync(userId, ct)`, no `SaveChangesAsync`, stages-into-caller-UoW, the only `Remove()`-on-`User` path) and asserts the observable matrix via integration tests; the architect finalizes the seam's internal orchestration (staging order, unreferenced-address detection, per-entity mutation surface), the ADR 0013 §"Hard delete (GDPR)" alignment, and the Reviewer-enforced single-hard-delete invariant. The PR requires BOTH security review AND architect sign-off before merge. Build T-0110 only after the architect lands the seam in the same branch.

## Status log

- 2026-06-14 `draft` by PM. Created as the fourth and final ticket in the admin-completion bundle (T-0111 read harness → T-0109 outbox retry/ack → T-0108 country-config update → T-0110 GDPR hard-delete; risk-ascending, one PR, irreversible op built last). Precedents: VerifyMaker / T-0107 (IAdminAuditableCommand shape + fail-closed session + 2000-char reason cap), T-0108 (retype-to-confirm high-stakes idiom), ADR 0013 §"Hard delete (GDPR)" (the named single hard-delete path + IUserDataDeletionService), US-admin-0016 (the erasure matrix + in-flight interlock).
- 2026-06-14 `draft → ready` by PM. User locked Q-A (erasure matrix: HARD-DELETE User/RefreshToken/unreferenced-Address, ANONYMIZE Order contact snapshots + Review author + Maker PII with IČO/BankAccount retained + IsRetainedForLegal, RETAIN Invoices untouched) and Q-B (single command, email-retype gate + in-flight-order interlock on [PendingPayment, Paid, Accepted, Shipped]) in the 2026-06-14 batched deliberation. Q-0021 (architect ruling): no-op audit rows are benign; no special "no second audit row" assertion — here moot since re-calls fail at load with user.notFound. PM absorbed: architect-owned IUserDataDeletionService seam (no SaveChangesAsync, stages into caller UoW); Maker.IsRetainedForLegal column + migration + AnonymizeForErasure; NO Silent-Success on re-call (the one bundle command without it); POST .../erase endpoint; 2 new error codes + reuse of the in-flight code; fail-closed session; NSwag admin regen; security_touching YES. **Ready for dotnet-backend** (implement LAST in the bundle branch/PR, after the architect lands the seam). **§Architect-review: YES — security review + architect sign-off both required on the PR.**
