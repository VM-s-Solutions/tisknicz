# Admin-ops bundle — Reviewer preliminary verdict (draft)

> Bundle-scope parallel-reviewer draft, written WHILE the implementer codes. Final verdict happens at PR-open. Bundle: **T-0111** (admin unscoped read queries) → **T-0109** (outbox retry/acknowledge) → **T-0108** (country-config update) → **T-0110** (GDPR hard-delete) — sequential, one branch, one PR, risk-ascending. All four `security_touching: true` → **Gate 3 SecOps mandatory on the whole PR**; T-0110 additionally requires **Architect sign-off** (§Architect-review: YES). Implementation has NOT started yet — HEAD is the grooming commit `2a9ee86`; all "verified on master" claims below were re-checked against the working tree on 2026-06-14.

## Bundle scope

Four backend-led admin-completion tickets. The read harness (T-0111) ships first so the three mutations that follow can assert their side effects by reading them back through the admin list endpoints. ~42 ACs total (T-0111: 12, T-0109: 9, T-0108: 11, T-0110: 14).

Bundle layout (expected):
- **1 EF migration** — T-0110 `AddMakerIsRetainedForLegal` (`is_retained_for_legal BOOLEAN NOT NULL DEFAULT false`). T-0111/T-0109/T-0108 ship **no** migration.
- **New interfaces:** `IAdminQueries` (T-0111, `Core.Domain/Admin/`), `IProviderRegistry` + `ProviderKind` (T-0108, `Core.Domain/Configuration/`), `IUserDataDeletionService` (T-0110 — namespace conflict, see HIGH-0 below).
- **New one-file features (7):** `GetAllOrders`, `GetAllInvoices`, `GetAdminAuditLog` (T-0111 read queries — NOT `IAdminAuditableCommand`); `RetryOutboxEvent`, `AcknowledgeOutboxEvent` (T-0109); `UpdateCountryConfiguration` (T-0108); `DeleteUserPermanently` (T-0110). The four mutations implement `IAdminAuditableCommand`.
- **New domain methods:** `OutboxEvent.RequeueForRetry(now)` (T-0109); `Maker.AnonymizeForErasure()` + `Maker.IsRetainedForLegal` property (T-0110); `Order` contact-anonymization surface (T-0110, architect picks the exact mutator).
- **New error codes (5 net):** `CountryProviderConfirmationMismatch` (T-0108); `OutboxAlreadyProcessed` (T-0109); `UserNotFound` + `UserDeleteConfirmationMismatch` (T-0110). T-0111 mints **zero**. Reused: `CountryProviderNotRegistered`/`CountryConfigurationNotFound` (T-0108), `OutboxRowNotFound` (T-0109), `UserCannotDeleteWithInFlightOrders` (T-0110).
- **cs-CZ i18n keys (~8 new):** see Gate 9 — this is the HARVESTED zero-tolerance surface.
- **New Web.Admin controllers (4):** `AdminQueriesController`, `OutboxEventsController`, `CountryConfigurationsController`, `UsersController`.
- **NSwag regen: admin host only** (all four tickets; one regen at the end of the branch covering all new admin endpoints).

### Seams verified present on master (the precedents the implementer builds on)

- `IOrderRepository.Unscoped()` (`IOrderRepository.cs:68`) + `IInvoiceRepository.Unscoped()` (`IInvoiceRepository.cs:80`) — T-0111's admin escape hatch. **Confirmed.**
- `OutboxEvent.Acknowledge(adminUserId, now)` (`OutboxEvent.cs:107`), `RecordFailure` (:65), `MarkProcessed` (:52), `AcknowledgedAt`/`AcknowledgedBy` (:24-25) — T-0109 reuses; `RequeueForRetry` is the ONLY new method. **Confirmed.** `IOutboxConsumerRepository.GetByIdAsync` (`IOutboxConsumerRepository.cs:32`) — T-0109 reuses, no new admin repo. **Confirmed.**
- `CountryConfiguration` mutators `UpdateVatRates` (:204), `UpdateInvoicingMode` (:215), `UpdatePlatformFeeRate` (:221), `UpdateDefaultShippingPrice` (:237), `UpdateProviders` (:247) — all present, T-0108 adds no entity method. **Confirmed.**
- `BusinessErrorMessage` reuse targets present: `CountryProviderNotRegistered` (:267), `CountryConfigurationNotFound` (:275), `UserCannotDeleteWithInFlightOrders` (:584), `OutboxRowNotFound` (:619). **Confirmed.**
- `Maker.RegistrationNumber` (:44, non-null), `Maker.BankAccount` (:98, **nullable**) — `AnonymizeForErasure` must preserve both; `IsRetainedForLegal` does NOT yet exist (correct — T-0110 adds it). **Confirmed.**
- `UniqueConstraintTranslator` (`Infra.Database/UniqueConstraintTranslator.cs`) — the finding-#3 registry. T-0110 adds NO unique index (the new column is plain BOOLEAN), so finding #3 is a watch-only here (see Gate 9).
- `AdminAuditPipelineBehavior` + `IAdminAuditableCommand` — refund-dispute bundle verified the pipeline order is Validation → UnitOfWork → **AdminAudit** → Handler (UoW WRAPS audit; audit row commits atomically with the mutation; failed commands write no audit row). The four mutations rely on this exact registration.

## Patterns / ADRs the diff must honour

- **patterns.md §A.23 (NEW — orchestrated multi-entity GDPR erasure in one UoW)**: architect codified this for T-0110. Rules: (1) one seam, one transaction — the whole pass runs in the handler's pipeline UoW, the service **never** calls `SaveChangesAsync()`; (2) the disposition matrix in [extension-points.md §14](../../architecture/extension-points.md) is the documented contract; (3) legal-retention beats erasure (Invoices RETAINED, Maker IČO+BankAccount kept + `IsRetainedForLegal=true`); (4) sentinel is replace-in-place `"Anonymized"`, NOT NULL preserved, no tombstone table; (5) irreversible — no Silent-Success re-call.
- **extension-points.md §14 (NEW — User data deletion / GDPR erasure)**: the seam contract. `EraseAsync(userId, ct) → BusinessResult`, runs the full matrix in ONE UoW, never `SaveChangesAsync`. **The architect's pass order puts the in-flight guard FIRST, INSIDE the seam** (§14 step 1). See HIGH-0 — the ticket puts it in the handler. Reconcile before final review.
- **ADR 0013 (data scoping + soft-delete + the single GDPR hard-delete path)**: T-0111 `Unscoped()` admin-host only; `.IgnoreQueryFilters()` only where an AC needs soft-deleted rows, each call commented. T-0110 is the named single hard-delete path; `Remove()` against `User` data runs ONLY inside `IUserDataDeletionService`. **Reviewer enforces: this is the ONLY `Remove()`-on-`User` call path in the system** (AC-14).
- **ADR 0014 (admin audit)**: the four mutations are `IAdminAuditableCommand`; reads (T-0111) carry NO audit and run only `ValidationPipelineBehavior`. Per Q-0021 (architect ruling): no-op/Silent-Success commands STILL write a benign audit row (T-0108 no-op save, T-0109 re-acknowledge) — this is intended, do not flag as a defect. T-0110 is the exception (irreversible; re-call fails at load with `user.notFound`, never reaching the success branch).
- **ADR 0020 (outbox)**: T-0109 manipulates outbox bookkeeping only — no enqueue, no queue message, no email. The `ProcessOutboxFunction` sweep is the sole re-publish path.
- **ADR 0023 (read-side / write-side split)**: T-0111 new `IAdminQueries` read-side; `IOrderRepository`/`IInvoiceRepository` stay write-scoped. AsNoTracking + projection-only + `IgnoreAutoIncludes()` + two round-trips (CountAsync + Skip/Take).
- **patterns §A.4/§A.7 (one-file feature, globally-unique Response names)**: `GetAllOrdersResponse`, `GetAllInvoicesResponse`, `GetAdminAuditLogResponse`, `RetryOutboxEventResponse`, `AcknowledgeOutboxEventResponse`, `UpdateCountryConfigurationResponse`, `DeleteUserPermanentlyResponse`. NEVER a bare `record Response` (PR #38 NSwag convention).
- **patterns §A.15 (provider adapter / keyed services)**: T-0108's `IProviderRegistry` probes the keyed container for payment/shipping; the static `{ "ares" }`/`{ "sendgrid" }` fallback carries a `// TODO(T-0124)` owner. The handler depends on the domain seam, NOT `IServiceProvider` — no DI-container reach-through in `Core.AppServices`.
- **Centralized codes**: 5 new constants; no inline strings.

## Pre-flight risks (HIGH first — T-0110 dominates)

### HIGH

- **HIGH-0 (act before T-0110 implementation): seam contract drift between the ticket and the architect's locked design.** Two divergences — the architect's extension-points.md §14 + patterns §A.23 are the source of truth (written 2026-06-14, AFTER the ticket prose) and SUPERSEDE the ticket:
  1. **Namespace.** Ticket says `Core.Domain.Identity.IUserDataDeletionService` + `Infra.Database.Identity.UserDataDeletionService` (T-0110 Scope + Files-touched). Architect §14 says `Core.Domain.Privacy.IUserDataDeletionService` + `Infra.Database.Privacy.UserDataDeletionService`. **The implementer must use `Privacy`** (the architect owns the seam). Final review rejects the `Identity` namespace.
  2. **Where the in-flight guard runs + the seam's return type.** Ticket: handler runs the in-flight interlock (step 4) and the seam is `Task EraseAsync(...)` (void-ish). Architect §14: the seam is `Task<BusinessResult> EraseAsync(...)` and the in-flight guard is **pass #1 INSIDE the seam**. These are mutually exclusive designs. **The architect's design wins** — the guard belongs in the seam (so "the whole pass is one reviewable unit") and `EraseAsync` returns `BusinessResult`. If the implementer follows the ticket literally, the guard ends up duplicated or in the wrong layer. Flag to PM/Architect NOW so the implementer codes against §14, not the stale ticket prose. (Either way, the OBSERVABLE behavior — 409 `cannotDeleteWithInFlightOrders`, nothing mutated — is identical; the integration tests are agnostic to which layer holds the guard. But the layering must match §14.)

- **HIGH-1 (T-0110 — the headline): erasure correctness + atomicity. The whole matrix in ONE UoW, all-or-nothing.** This is the single most sensitive change in the backlog. Verify line-by-line at final review:
  - The seam stages ALL tracked changes (anonymize Order/Review/Maker PII + hard-delete User/RefreshToken/unreferenced Address) and returns; the command's `UnitOfWorkPipelineBehavior` issues the single `SaveChangesAsync`. **The seam must NOT call `SaveChangesAsync`** (§A.23 rule 1; extension-points §14). A mid-matrix throw must roll EVERYTHING back — no half-erased user (User still present, PII still scrubbed, or vice versa). **Integration test must assert the all-or-nothing boundary** (a forced mid-matrix failure leaves the user fully intact).
  - **Invoices byte-identical post-erasure.** AC-6 — the seam must NOT read or write a single invoice column (the repo exposes no Update/Delete). Integration test #1 asserts the Invoice row is byte-for-byte unchanged. This is the GDPR Art. 17(3)(b) contract; the test pins the "do nothing to invoices" promise.
  - **Maker tombstone correctness.** `AnonymizeForErasure()` sets PII → `"Anonymized"`, **retains `RegistrationNumber` (IČO) AND `BankAccount`** (nullable — if null, stays null; if set, untouched), sets `IsRetainedForLegal = true`. Pure-logic transform test (red-first) + integration assertion both required (AC-5).
  - **Anonymization sentinel = the literal `"Anonymized"`** everywhere (Order.ContactName/Email/Phone, Review author, Maker PII). Columns stay NOT NULL. No tombstone/archive table.
  - **Unreferenced-address detection.** Hard-delete an Address ONLY when no live entity still FKs it (a maker legal-seat address stays). The architect owns the exact detection; the integration test seeds one unreferenced Address (deleted) and — ideally — one referenced (retained) to prove the predicate.

- **HIGH-2 (T-0110): in-flight interlock covers BOTH roles.** `[PendingPayment, Paid, Accepted, Shipped]` orders block erasure whether the user is the **customer OR the maker** on the order. The predicate must be `(CustomerUserId == userId OR MakerId == user'sMakerId) AND State ∈ InFlightOrderStates` — a single `EXISTS`, not a list materialization. Verify a test covers the **maker-side** block specifically (the easy bug is checking only `CustomerUserId`). The static `InFlightOrderStates` set is shared by the test and the query (one source of truth); the predicate test asserts it is exactly the four states and EXCLUDES `Delivered/Completed/Cancelled/Refunded/Disputed`.

- **HIGH-3 (T-0110): irreversibility — NO Silent-Success re-call.** This is the ONE bundle command without idempotent re-success (§A.23 rule 5). A second erase of the same id finds no user (the row is gone) → `user.notFound`, NOT a benign 200. Verify: (a) handler has no "already erased → 200" branch; (b) integration test re-calls after a successful erase and asserts `user.notFound`; (c) the first erasure's `admin_audit_log` row SURVIVES and references the now-deleted `UserId` as `TargetId` (a dangling reference that is correct — it's an admin-action log, not an FK). AC-10 + AC-11.

- **HIGH-4 (T-0110): retype gate before any mutation.** `User.NormalizeEmail(command.ConfirmedEmail) != user.EmailNormalized` → `user.deleteConfirmationMismatch` 409, **before** the seam is invoked. The comparison is case/NFC/whitespace-insensitive (mirrors the login lookup invariant) — AC-9 asserts ` ADMIN@X.COM ` matches stored `admin@x.com`. Verify the gate runs AFTER load (needs the user's normalized email) and BEFORE the in-flight guard / seam. Mismatch mutates nothing.

- **HIGH-5 (T-0111): Unscoped exposure is admin-host + admin-audience ONLY.** The three read endpoints expose cross-tenant data (CustomerEmail + MakerName, no GDPR redaction) BY DESIGN — admin is privileged. The boundary moves from the SQL WHERE clause to the **host audience**. Verify at final review: (a) `IOrderRepository.Unscoped()` / `IInvoiceRepository.Unscoped()` are reachable ONLY from `Web.Admin` controllers — grep the whole solution and reject any call from `Web.Customer`/`Web.Maker`/`Web.Public`; (b) `[Authorize]` (admin scheme) on every endpoint; (c) integration test AC-9 pins that a customer/maker JWT (`aud != admin`) gets 401/403; (d) every `.IgnoreQueryFilters()` call is commented — present ONLY on `GetAllOrdersPagedAsync`, absent from the invoice + audit queries (AC-12).

- **HIGH-6 (T-0108): provider-change gate — retype + unregistered-code rejection, correct ordering.** Verify: (a) when ANY `Default*Provider` field changes, `ConfirmedProviderCode` must equal the NEW value of the changed provider (payment wins if multiple changed) → mismatch/null = `country.providerConfirmationMismatch`; (b) the unregistered-code check (`providerRegistry.GetRegisteredCodes(kind).Contains(newValue)`) runs **BEFORE** the retype gate, so a garbage code returns the more actionable `providerNotRegistered`, not the mismatch (unit test #2 pins the ordering); (c) in-flight orders WARN-not-block — `InFlightOrderCount` advisory, save proceeds, in-flight orders' `PaymentProviderRef` unchanged in the DB (AC-5); (d) both gates live in the HANDLER (stateful — need the loaded row + DI registry), NOT the Validator. The Validator covers shape only (ranges, non-empty, length, enum).

- **HIGH-7 (T-0109): retry backoff preservation — the pure-logic red-first surface.** `RequeueForRetry(now)` sets `NextRetryAt = now`, **increments** `RetryCount` (`checked(RetryCount + 1)`), and does **NOT** reset the ladder. The load-bearing assertion (locked A.1): after `RequeueForRetry` bumps `RetryCount` to N+1, a subsequent `RecordFailure` computes `OutboxRetryPolicy.NextAttempt(Transient, N+1, now)` from the bumped count — re-entering the ladder at rung N (or stalling if exhausted), NOT restarting the 31-hour budget. This MUST be a red-first domain test (`RequeueForRetry_does_NOT_reset_the_backoff_ladder`). Also verify: refuses an already-processed row (throws `InvalidOperationException`, belt-and-braces behind the handler's 409 guard); preserves `LastErrorKind`/`LastErrorCode` (stall diagnostic survives). See Gate 5.

### MEDIUM

- **MEDIUM-1 (T-0109): retry-on-processed is a LOUD 409, acknowledge-on-acknowledged is Silent-Success.** Asymmetry is deliberate and locked (A.3 vs A.4). `/retry` on a `ProcessedAt != null` row → `409 outbox.alreadyProcessed` (operator error surfaced); `/acknowledge` on an already-acknowledged/processed row → `200` Silent-Success echoing the EXISTING `AcknowledgedBy` (NOT the current caller), no re-mutation. Per Q-0021 the re-acknowledge still writes a benign audit row — do not flag. Verify the acknowledge `BuildResponse` falls back to `AcknowledgedBy ?? adminUserId` / `AcknowledgedAt ?? ProcessedAt!.Value` for a swept-but-unacknowledged row.

- **MEDIUM-2 (T-0108): no-op fast path + Q-0021 audit row.** All-values-unchanged → 200, no mutators, no provider gate, but the audit pipeline STILL writes a benign `country.update` row (Q-0021 — the attempt is audit-worthy). This is NOT a defect; the previously-circulated "no second audit row on no-op" wording is dropped platform-wide. Do not flag the no-op audit row. Verify the no-op path computes `inFlightCount` only on the non-no-op branch (cheap-first) and that `IProviderRegistry`/`IOrderQueries` are NOT queried on a true no-op (unit test #5).

- **MEDIUM-3 (T-0108): `IProviderRegistry` static-fallback TODO must carry an owner.** `// TODO(T-0124): replace static fallback with keyed-container probe once IEmailProvider + ICompanyRegistry are keyed.` — the "no TODO without owner" rule requires the `(T-0124)` tag. Reject a bare `// TODO`. Also: the registry probe must be case-insensitive (`StringComparer.OrdinalIgnoreCase`) and the `IProviderRegistry` impl lives in `Infra.Database` (the only place that may touch `IServiceProvider`); `Core.AppServices` depends on the domain interface only.

- **MEDIUM-4 (T-0108): `CountInFlightByCountryAsync` is a NEW read on `IOrderQueries`.** AsNoTracking, admin-host unscoped count across all makers/customers in the country, soft-deleted excluded by the global filter (no `IgnoreQueryFilters`). This is a new DB round-trip → Gate 8 Optimizer applies (see Gate 8). Verify the in-flight states reuse the same set as the existing in-flight definition (no drift between T-0108's count and T-0110's interlock — they should reference the same `OrderState` grouping concept; if they each define their own set, flag the duplication).

- **MEDIUM-5 (T-0111): two-round-trip paging + projection discipline.** Exactly two SQL statements per call (CountAsync + Skip/Take projection), AsNoTracking + IgnoreAutoIncludes on every query, `Select` projection straight to DTO (no entity materialization, no `.Include`). PageSize clamp `[1,50]` fast-fails 400 (no silent clamp). Sort `CreatedAt DESC` + `Id` tie-breaker on all three. AC-12 pins the SQL-statement count — Optimizer should confirm no `COUNT(*) OVER ()` window-function full-scan and no N+1 on the maker/product LEFT JOINs.

- **MEDIUM-6 (T-0110): `Order.PreDisputeState`-style null-forgive watch + `Maker.BankAccount` nullability.** `BankAccount` is nullable on master (`Maker.cs:98`) — `AnonymizeForErasure` must handle a null bank account gracefully (preserve null, don't NRE). Verify the `AnonymizeForErasure_is_idempotent` test covers a maker with a null bank account.

- **MEDIUM-7 (bundle): branch hygiene + first admin-NSwag-in-this-PR.** The branch must be cut fresh from master after the prior merges. The working tree currently carries unrelated drift (`.claude/settings.json`, `frontend/src/lib/api-client/*`, `.spec-hashes.json` modifications shown in `git status`) — these must NOT ride this PR. The admin-api.v1.ts regen in this PR must contain ONLY the four tickets' new endpoints; flag any unrelated hunk. The pre-commit manual-edit hook must cover `admin-api.v1.ts` (a hand-typed admin client that the hook ignores would pass silently).

### LOW / INFO

- **LOW-1 (T-0111):** invoice sort — US-admin-0012 AC-1 names `IssueDate DESC`, the ticket sorts `CreatedAt DESC` (they coincide at issuance). Locked PM divergence; do not ding, but the PR description should note it.
- **LOW-2 (T-0110):** the audit row references a deleted `UserId` — correct by design (admin-action log, not FK). Final review must not flag the dangling reference as a bug.
- **INFO-1 (recurring-finding #2 — HARVESTED, zero-tolerance):** every new `BusinessErrorMessage` code MUST ship its cs-CZ key in the same PR. This finding is HARVESTED at count 3 (a standing automated-gate candidate). For THIS bundle it is a HARD line — see Gate 9. No new harvest row is needed unless a NEW finding-type recurs a 3rd time.
- **INFO-2 (recurring-finding #3 — unique-index-translator, one strike from codification):** T-0110's `Maker.IsRetainedForLegal` is a plain BOOLEAN, NOT unique — finding #3 does NOT fire here. But if the implementer adds ANY unique/partial-unique index anywhere in this bundle (none is specced), it must have a `UniqueConstraintTranslator.Mappings` entry + a concurrent-write integration test, or it is **hit #3 → codification trigger** (flag HARD + Architect ping). Watch the migration diff for stray `IsUnique()` / `CREATE UNIQUE INDEX`.

## AC traceability (~42 ACs; how each is proven in the diff)

### T-0111 — admin read queries (12)

| AC | How I verify |
|---|---|
| AC-1 | Integration test 1: `GET /admin-orders` no filter → cross-tenant rows from BOTH makers + customers, `CreatedAt DESC`, page 1/size 20. Unscoped proof. |
| AC-2 | Integration: `?state=Paid&country=CZ&makerId=X&customerEmail=jana` → ALL-filters AND match, case-insensitive email, filtered `totalCount`. |
| AC-3 | DTO shape assertion: row carries non-empty `CustomerEmail` + `MakerName` + the full named field set (privileged, no redaction). |
| AC-4 | Integration test 2: soft-deleted/anonymized order appears with `IsActive == false`. `.IgnoreQueryFilters()` proof. |
| AC-5 | Integration test 3: `?type=Fee&country=CZ` → only CZ Fee invoices, `CreatedAt DESC`, named field set incl. nullable OrderId/PayoutBatchId. |
| AC-6 | Integration: `?recipient=jvm&dateFrom&dateTo` → case-insensitive recipient + inclusive `CreatedAt` range. |
| AC-7 | Integration test 4: `GET /audit-log` no filter → `CreatedAt DESC`; list DTO carries metadata but NOT `BeforeJson`/`AfterJson`. |
| AC-8 | Integration: `?adminUserId&actionCode&targetEntity&dateFrom&dateTo` → ALL-filters AND match. |
| AC-9 | Integration: anonymous + customer/maker JWT → 401/403 (host audience, ADR 0013). **HIGH-5.** |
| AC-10 | Validator unit tests: page=0 / pageSize=51 / inverted date → 400, no new error code. |
| AC-11 | Build + ~10 unit + ~4 integration; NSwag admin regen committed, no manual edits; consistency exit 0. |
| AC-12 | SQL-log inspection: exactly 2 statements/call; AsNoTracking + IgnoreAutoIncludes on every query; `.IgnoreQueryFilters()` ONLY on admin-orders (commented). **MEDIUM-5.** |

### T-0109 — outbox retry/acknowledge (9)

| AC | How I verify |
|---|---|
| AC-1 | Integration test 1 + handler test: stalled row → `/retry` 200, `next_retry_at ≈ now`, `retry_count += 1`, row matches the due-predicate. |
| AC-2 | **Domain test (red-first):** `retry_count = N+1`, ladder NOT reset; subsequent failure computes `NextAttempt(Transient, N+1, now)`. **HIGH-7, the load-bearing assertion.** |
| AC-3 | Integration test 2 + handler: `/acknowledge` 200, `processed_at`+`acknowledged_at`+`acknowledged_by` set, `next_retry_at` cleared, row leaves stalled set. |
| AC-4 | Both integration tests: `admin_audit_log` row with `outbox.retry`/`outbox.acknowledge` + admin id + reason in notes (acknowledge). |
| AC-5 | Handler test: missing row → `404 outbox.rowNotFound`. |
| AC-6 | Handler test: already-processed → `/retry` `409 outbox.alreadyProcessed`, unchanged. **MEDIUM-1.** |
| AC-7 | Handler test: already-acknowledged → `/acknowledge` 200 Silent-Success, echoes EXISTING acknowledger, no re-mutation. **MEDIUM-1.** |
| AC-8 | Handler test: empty session → `401`, fail-closed; acknowledge 400 on empty/>2000 reason. |
| AC-9 | Build + ~5 domain (RequeueForRetry red-first) + ~3 retry-handler + ~3 ack-handler + ~2 integration; new code + reused code both have cs-CZ keys; NSwag admin regen. |

### T-0108 — country-config update (11)

| AC | How I verify |
|---|---|
| AC-1 | Integration test 1: VAT/fee/shipping/mode change → 200, persisted on re-read, exactly one `country.update` audit row with before/after + notes. |
| AC-2 | Handler unit test #2 + integration: unregistered `Default*Provider` → `400 country.providerNotRegistered`, nothing mutated. **HIGH-6.** |
| AC-3 | Handler unit test #1 + integration test 2: registered alt + wrong/null `ConfirmedProviderCode` → `400 country.providerConfirmationMismatch`, nothing mutated. **HIGH-6.** |
| AC-4 | Handler unit test #3: correct retype → 200, provider updated, `providerChanged == true`. |
| AC-5 | Integration test 3: provider change → `inFlightOrderCount == 2`, save NOT blocked, in-flight orders' `PaymentProviderRef` unchanged. WARN-not-block. **HIGH-6.** |
| AC-6 | Handler unit test #4: VAT-only change, `ConfirmedProviderCode = null` → 200, no gate, `providerChanged == false`, `inFlightOrderCount == 0`. |
| AC-7 | Handler unit test #5: true no-op → 200, no mutator, no registry/queries call; audit pipeline writes the benign row (Q-0021). **MEDIUM-2.** |
| AC-8 | Handler test #6: empty session → `401`, repo never loaded (fail-closed). |
| AC-9 | Validator tests: reduced>standard / fee∉[0,10000] / negative shipping / empty provider / reason empty-or->2000 → 400 on the field. |
| AC-10 | Handler test #7: missing config → `404 countryConfiguration.notFound`. |
| AC-11 | Build + ~10 unit (2 red-first) + ~3 integration; NSwag admin regen; 2 new + 1 reused i18n key present. |

### T-0110 — GDPR hard-delete (14)

| AC | How I verify |
|---|---|
| AC-1 | Integration test 1: erase → 200, `User` row gone (absent even under `IgnoreQueryFilters`). **HIGH-1.** |
| AC-2 | Integration: RefreshTokens gone + unreferenced Address gone. **HIGH-1.** |
| AC-3 | Integration + transform test: order ContactName/Email/Phone == `"Anonymized"`, no pricing/state/timestamp column touched. |
| AC-4 | Integration: review author anonymized, rating + body unchanged. |
| AC-5 | Integration + `AnonymizeForErasure` red-first test: Maker PII `"Anonymized"`, IČO+BankAccount UNCHANGED, `IsRetainedForLegal == true`. **HIGH-1.** |
| AC-6 | Integration test 1: every Invoice row byte-for-byte unchanged. **HIGH-1 — the legal-retention contract.** |
| AC-7 | Integration test 2: in-flight order (customer OR maker) → `409 cannotDeleteWithInFlightOrders`, NOTHING mutated. **HIGH-2.** |
| AC-8 | Integration test 3 + handler test: retype mismatch → `409 deleteConfirmationMismatch`, nothing mutated. **HIGH-4.** |
| AC-9 | Handler test #9: ` ADMIN@X.COM ` vs `admin@x.com` → gate passes (normalization). **HIGH-4.** |
| AC-10 | Handler test #12 + integration test 4: re-call → `user.notFound`, NO Silent-Success; first audit row survives. **HIGH-3.** |
| AC-11 | Integration test 5: exactly one `user.erase` audit row, target_id = erased id, notes = reason, before/after JSONB; survives the deletion. Blocked/400 write no audit row. |
| AC-12 | Integration: non-admin-audience JWT → 401 (host gate); empty session in handler → fail-closed Unauthorized. |
| AC-13 | Validator tests: empty confirmedEmail / empty reason / reason>2000 → 400, seam never invoked. |
| AC-14 | Build + ~12 unit (in-flight set + transforms red-first, verifiable in git log) + ~5 integration; migration applies; 2 new + 1 reused code each with cs-CZ key; NSwag admin regen; consistency exit 0. **Reviewer confirms `IUserDataDeletionService` is the ONLY `Remove()`-on-`User` path.** |

## Gate 5 — Tests (TDD red-first: HARD requirement; commit order will be checked)

Pure-logic surfaces that MUST have a red commit BEFORE their implementation commit (T-0067+ hard rule; after-the-fact test on pure logic = HARD FAIL per quality-gates.md Gate 5):

1. **T-0109 `OutboxEvent.RequeueForRetry`** (~5 domain tests) — the backoff-ladder-preservation assertion (`RequeueForRetry_does_NOT_reset_the_backoff_ladder`) is the load-bearing one. Red commit before the `RequeueForRetry` method exists. The ticket's commit hint and status log both name the red-first ordering — verify in `git log --reverse`.
2. **T-0108 provider predicates** (~2 of the ~10 handler tests) — `Provider_change_without_matching_confirmation_is_rejected` + `Unregistered_provider_code_is_rejected`, both RED FIRST, both pinning the ordering (unregistered before mismatch). Note: these are HANDLER tests (need mocks), so "pure logic" is borderline — but the ticket explicitly mandates red-first for them, so the commit order must show test-before-impl.
3. **T-0110 in-flight set + anonymization transforms** (~4 predicate/transform tests) — `InFlightOrderStates_contains_exactly_the_four_locked_states`, `AnonymizeForErasure_*` (×2), `Order_contact_anonymization_*`. These are genuinely pure (entity transforms + a static set) → HARD red-first. The ticket commit hint #1 is `test(T-0110): pin in-flight state set + AnonymizeForErasure + contact-anonymization transforms (red)` — verify it precedes commit #2.

Verification method: `git log --reverse <branch> -- <test-file> <impl-file>` per tdd-policy.md; status-log red→green proof acceptable as fallback. **If any pure-logic test (esp. T-0109 ladder, T-0110 transforms) lands AFTER its implementation commit → HARD FAIL, request changes, no approval until rewritten under TDD.** Handler/integration tests follow per ticket. Negative-path Validator tests for all 5 new codes (must-cover §9).

**Real e2e mandate (the money-bundle lesson):** T-0110's full-matrix erasure MUST be a real-Postgres Testcontainers e2e (integration test #1), not a mocked seam. A mocked `IUserDataDeletionService` in the handler test proves the handler calls the seam; it does NOT prove the matrix is correct, atomic, or that invoices survive. The 5 integration tests against real Postgres are non-negotiable for the only irreversible operation in the system.

## Gate 9 — Mechanical checks + parity

- **Baseline drift:** 7 new feature files (3 read + 2 outbox + 1 config + 1 erase) → expect ~7 new T1 static-class-wrapper false-positives (the established per-feature-file pattern). HARD FAIL on any NEW non-T1 violation.
- **T5 / i18n parity (HARVESTED #2 — zero-tolerance, the load-bearing surface):** I re-checked `cs-CZ.ts` against the working tree — **NONE of `country.*`, `outbox.*`, `user.notFound`, `user.deleteConfirmationMismatch`, `user.cannotDeleteWithInFlightOrders` exist there today.** That means:
  - **T-0108:** `country.providerNotRegistered`, `country.providerConfirmationMismatch`, `countryConfiguration.notFound` — all 3 must be ADDED (the two reused codes have no cs-CZ key yet; the ticket correctly lists all three).
  - **T-0109:** `outbox.rowNotFound` (reused code, NO key today — ticket correctly notes the parity gap) + `outbox.alreadyProcessed` (new). Both must be added.
  - **T-0110:** `user.notFound` (new), `user.deleteConfirmationMismatch` (new), AND `user.cannotDeleteWithInFlightOrders` — the ticket says this third one "may already exist; reuse if so." **It does NOT exist in cs-CZ.ts.** The implementer MUST add it. If the final diff ships any of these codes without its key, that is a Gate 9 HARD FAIL.
  - **Net:** ~8 cs-CZ keys must land in this PR. Quote at final review: *"Every new `BusinessErrorMessage` constant MUST ship with a matching `cs-CZ` i18n key in the same PR"* (recurring-findings #2).
- **T6 money:** T-0110's only new column is `is_retained_for_legal BOOLEAN` — not monetary, no `_minor`/`currency` obligation. No money columns added in the bundle.
- **Unique-index-translator (#3 — one strike from codification):** no new unique index is specced. Watch the T-0110 migration for any stray `IsUnique()`/`CREATE UNIQUE INDEX` — if one appears it needs a translator mapping + concurrent-write test or it is hit #3 (HARD flag + Architect ping). The `is_retained_for_legal` column needs NO unique index.
- **T3/T4:** zero `SaveChangesAsync` in any of the 7 handlers AND in the `UserDataDeletionService` seam (the pipeline commits); zero `dynamic`/`any`; no `IServiceProvider` in `Core.AppServices` (T-0108 `IProviderRegistry` keeps it in Infra).
- **NSwag:** admin host only, all four tickets in one regen at branch end; `.spec-hashes.json` updated; pre-commit hook covers `admin-api.v1.ts` (MEDIUM-7); CI parity green; PR description flags the contract change (Gate 6).
- **Gate 7 docs:** role-file updates required — `country-configuration.md` (T-0108 `IProviderRegistry` seam), `outbox.md` (T-0109 retry/ack + ladder-preservation), `user.md` + `maker.md` (T-0110 erasure matrix + `IsRetainedForLegal` tombstone), `admin-audit-log-entry.md` + `order.md` + `invoice.md` (T-0111 admin read seam). **RDD parity (ADR 0015):** the new seams need role coverage — `IAdminQueries`, `IProviderRegistry`, `IUserDataDeletionService` each warrant a role-file note (the architect already documented `IUserDataDeletionService` in extension-points §14 + patterns §A.23, so the role-file cross-reference is light). Verify every handler depends on ≤5 collaborators: T-0108 handler injects 5 (session, configs, providerRegistry, orderQueries, logger) — at the limit, acceptable; T-0110 handler injects 4 (users, orders, deletion, session) — fine.
- **Gate 8 (Optimizer — applies):** T-0111 introduces THREE new paged queries (hot-path per the charter) → Optimizer ping mandatory (Specification/index + no N+1 on the maker/product joins + 2-statement proof + CancellationToken propagation). T-0108 adds `CountInFlightByCountryAsync` (a new blocking DB round-trip). T-0110's seam is a multi-aggregate destructive pipeline (multi-step, touches >5 entities) → Optimizer ping for the matrix-query shape (the unreferenced-address detection must not be an N+1 across addresses). T-0109 is bookkeeping-only — no Optimizer concern.
- **Gate 3 (SecOps — mandatory):** all four `security_touching`. Surfaces: privileged cross-tenant CustomerEmail exposure + Unscoped reachability (T-0111); delivery-infra mutation (T-0109); control-plane VAT/fee/provider mutation (T-0108); the only PII-erasure + hard-delete path (T-0110, also Architect-mandatory). **Q-0011 (rate-limit/abuse on admin surface) is TOUCHED-not-closed across all four — SecOps Gate 3 must re-confirm the rate-limiting posture on the admin mutation surface; the bundle does NOT expand scope to address Q-0011.**

## Bundle DoR compliance

- ✅ All four tickets satisfy DoR (status `ready`; Q-A…Q-E + Q-0021 user-locked/architect-ruled 2026-06-14).
- ✅ Bundle ordering documented and load-bearing (read harness first; irreversible hard-delete last on top of the verified-by-T-0111 read surface). Do NOT reorder.
- ⚠️ **T-0110 seam contract drift (HIGH-0)** — the ticket's namespace + guard-placement contradict the architect's extension-points §14 / patterns §A.23. The architect's design is the source of truth. PM/Architect should confirm the implementer codes against §14 (namespace `Privacy`, `EraseAsync → BusinessResult`, in-flight guard inside the seam as pass #1) — NOT the stale ticket prose.
- ⚠️ **Size:** M + S + M + M, one PR. The erase matrix + 5 integration tests + 4 controllers + ~42 ACs is a large diff; review will be staged per-ticket-commit. PM should be aware.
- ⚠️ **Branch:** must be cut fresh from master; the current working-tree drift (`.claude/settings.json`, `api-client/*`, `.spec-hashes.json`) must NOT ride this PR (MEDIUM-7).
- ✅ No `manual_steps` in any of the four (no env-var/deploy step — unlike the refund-dispute bundle's `ADMIN_NOTIFICATION_EMAIL`).
- ✅ Single parallel-reviewer artifact (this file).

## Open items the implementer should confirm before/while coding

1. **T-0110 seam: code against extension-points §14 / patterns §A.23, NOT the ticket prose** — namespace `Core.Domain.Privacy` + `Infra.Database.Privacy`; `EraseAsync` returns `BusinessResult`; in-flight guard is pass #1 INSIDE the seam (HIGH-0). Confirm with Architect.
2. **The seam NEVER calls `SaveChangesAsync`** — the pipeline UoW commits the whole matrix atomically; an integration test must prove all-or-nothing on a forced mid-matrix failure (HIGH-1).
3. **In-flight interlock covers customer AND maker role**; reuse ONE `InFlightOrderStates` set shared by T-0108's count and T-0110's interlock — flag if they each define their own (HIGH-2, MEDIUM-4).
4. **No Silent-Success on re-erase** — second call → `user.notFound`; first audit row survives referencing the deleted id (HIGH-3, LOW-2).
5. **Retype gate normalizes** (case/NFC/whitespace) and runs after load, before the seam (HIGH-4).
6. **`Unscoped()` reachable only from `Web.Admin`** — grep the whole solution; `.IgnoreQueryFilters()` only on admin-orders, commented; non-admin JWT → 401/403 integration pin (HIGH-5).
7. **T-0108 unregistered-code check BEFORE retype gate**; both in the handler not the Validator; `IProviderRegistry` impl in Infra only; static-fallback TODO carries `(T-0124)` owner (HIGH-6, MEDIUM-3).
8. **`RequeueForRetry` increments (never resets) `RetryCount`** — red-first ladder-preservation test before the method exists (HIGH-7, Gate 5).
9. **Q-0021 no-op audit rows are intended** — T-0108 no-op save + T-0109 re-acknowledge each write a benign audit row; do not "fix" (MEDIUM-1, MEDIUM-2).
10. **All ~8 cs-CZ keys land in this PR**, including the THREE reused-code keys that do NOT exist today (`outbox.rowNotFound`, `user.cannotDeleteWithInFlightOrders`, and the country reuse keys). The "may already exist; reuse if so" prose is wrong for `user.cannotDeleteWithInFlightOrders` — it must be added (INFO-1, Gate 9).
11. **No new unique index** without a `UniqueConstraintTranslator` mapping + concurrent-write test (finding #3 watch); `is_retained_for_legal` needs none (INFO-2).
12. **Role files + RDD parity** for `IAdminQueries`, `IProviderRegistry`, `IUserDataDeletionService` land in the same PR; handler collaborator counts ≤5 (T-0108 is at exactly 5) (Gate 7/RDD).
13. **Branch hygiene** — cut fresh from master; no unrelated working-tree drift in the PR; admin-NSwag hook coverage (MEDIUM-7).
14. **Real Postgres e2e for the erasure matrix** — not a mocked seam (Gate 5).

## Preliminary verdict

**STRUCTURALLY_SOUND_PENDING_DIFF — with one cross-lane action item before T-0110 implementation.**

The four tickets satisfy DoR with tight grooming; every "verified on master" claim I re-checked holds (the `Unscoped()` methods, the `OutboxEvent` state machine + `GetByIdAsync`, all five `CountryConfiguration` mutators, the four reused error codes, `Maker.RegistrationNumber`/`BankAccount` shapes, the absence of `IsRetainedForLegal`). The architecture is pre-codified (patterns §A.23, extension-points §14) rather than retrofitted, and the locked decisions are internally consistent across the bundle (risk-ascending order, retype-idiom reuse, Q-0021 audit-row disposition, the deliberate retry-409-vs-ack-Silent-Success asymmetry).

The **three named pre-flight concerns the final review will trace line-by-line** are all in T-0110 (the only irreversible operation in the system): **HIGH-1** (matrix atomicity + invoices byte-identical + seam-never-commits), **HIGH-2** (in-flight interlock covers both roles), and **HIGH-3** (no Silent-Success re-call, audit row survives). The **one item that needs action OUTSIDE the implementer's lane is HIGH-0** — the T-0110 ticket prose (namespace `Identity`, handler-side in-flight guard, void `EraseAsync`) contradicts the architect's locked seam contract (namespace `Privacy`, seam-side guard, `EraseAsync → BusinessResult`). The architect's design wins; PM/Architect should confirm the implementer codes against extension-points §14, not the stale ticket. The **i18n-parity HARVESTED finding (#2)** is the load-bearing Gate 9 surface: ~8 cs-CZ keys must land — including three reused-code keys that do NOT exist today (the ticket's "reuse if so" prose is wrong for `user.cannotDeleteWithInFlightOrders`).

Hold the line on: one UoW all-or-nothing for the erasure (never a half-erased user); invoices touched by zero code; `Remove()`-on-`User` ONLY inside the seam; Unscoped reachable only from Web.Admin; the backoff ladder preserved (never reset); red-first commits for the three pure-logic surfaces with commit-order proof; and every new code paired with its cs-CZ key. No approval until Gate 3 (SecOps), Gate 4 (Architect, T-0110), Gate 5 (TDD), and Gate 9 (parity) are all green.
