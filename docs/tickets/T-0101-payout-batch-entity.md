---
id: T-0101
title: PayoutBatch entity + Order.PayoutBatchId + IPayoutBatchRepository + payout_batches migration
status: ready
size: M
owner: dotnet-backend
created: 2026-06-12
updated: 2026-06-12
depends_on: [T-0007, T-0060, T-0068a]
blocks: [T-0102a, T-0103, T-0104, T-0112]
user_stories: [US-admin-0007]
adrs: [0003, 0009, 0013, 0014]
phase: 5
manual_steps: [ef-migration]
security_touching: false
layers: [domain, infra-database]
---

# T-0101 — PayoutBatch entity + Order.PayoutBatchId + IPayoutBatchRepository + payout_batches migration

## Context

T-0101 is the **first ticket in the payout bundle** (T-0101 entity slice → T-0102a CreatePayoutBatch command + fee invoices + CSV + `MakablesMeters.Payouts` instrumentation, same PR; T-0103 MarkPayoutBatchCompleted + `payout-sent` settlement emails, PR #2; T-0104 timer Function downstream). It follows the **T-0068a entity-slice precedent**: pure domain + database PR — entity, EF configuration, migration, scoped repository, DI registration. No commands, no controllers, no contract change, no NSwag regen.

Three pre-existing seams land here. (1) `IPayoutBatchNumberGenerator` exists since T-0007 (`VYP-{CC}-{YYYY}-W{ww}`, `For(countryCode, batchDate)`, no `NumberingSequence` row per ADR 0009) — this ticket creates the `payout_batches` table whose unique `(country_code, batch_number)` index IS the uniqueness enforcement ADR 0009 promised. (2) `Invoice.PayoutBatchId` column exists since T-0068a with an inline comment naming T-0101 as the FK-constraint owner — **closing that TODO is this ticket's job**. (3) Q-0017 (email subject placeholders seeded single-brace — `$@"` interpolation ate one brace from `{{order_number}}` in 4 prior seed migrations; bodies were non-interpolated consts and are unaffected) gets its sanctioned data-fix migration here, leading this ticket's migration train.

This ticket satisfies the schema half of **US-admin-0007 — Run weekly payout batch**. The behavioral ACs (claim, fee invoices, CSV, audit) are T-0102a/T-0103; T-0101 ships the aggregate they mutate.

## Locked design decisions

Captured per `docs/process/deliberation.md`. The user locked 5 bundle-wide dimensions at the 2026-06-12 deliberation. These bind the whole bundle; T-0101 implements their schema consequences.

### A. User-locked at deliberation (non-negotiable, bundle-wide)

1. **Q1 — Generic documented CSV behind a format seam.** `IPayoutCsvFormatter` (keyed-service-ready per the provider-adapter pattern); columns: account, amount in CZK decimal display, VS = batch/order number, message. Bank-native exporters are follow-up tickets once the operator names the bank. **T-0101 consequence:** `CsvBlobPath` is a nullable set-once string on the entity — the formatter itself ships in T-0102a. **Rejected:** picking a specific bank's native format now (guesses the operator's bank; the seam costs nothing).
2. **Q2 — Fee invoices per-batch at CreatePayoutBatch.** One `InvoiceType.Fee` invoice per maker per batch; DUZP = batch creation date; shared FV-CZ sequence per T-0068a lock 4. **T-0101 consequence:** the `invoices.payout_batch_id` FK constraint must exist before T-0102a writes the first Fee row. **Rejected:** per-order fee invoices (16× the document volume for zero legal benefit); monthly fee invoicing (decouples invoice from the payout it explains).
3. **Q3 — Partially-refunded Delivered orders excluded from auto-claim.** `RefundedAmountMinor > 0` ⇒ stay unclaimed, surfaced in batch response + audit; ride the next batch after admin resolution. **Rejected:** netting the refund into the payout line (silently pays a disputed amount; admin must look first).
4. **Q4 — Batch is IMMUTABLE once created.** Born directly in `Processing` (no `Pending` — creation is atomic in one UoW, so `Pending` would be an unobservable instant). No order removal — Fee invoices are already issued and legally immutable. Whole-batch cancel = deferred follow-up. **T-0101 consequence:** `PayoutBatchState` has exactly two values; no repository `UpdateAsync`/`DeleteAsync`; role doc updated.
5. **Q5 — NULL-`BankAccount` makers' orders excluded from claim.** Excluded-maker count surfaced in response + audit. **Rejected:** failing the whole batch (one incomplete maker profile would block every other maker's payout).

### B. ADR-locked (no relitigation)

- **ADR 0003 (money).** `TotalAmountMinor BIGINT NOT NULL` + `Currency CHAR(3) NOT NULL`. Column names end `_minor`.
- **ADR 0009 (numbering).** `VYP-{CC}-{YYYY}-W{ww}` via the existing `IPayoutBatchNumberGenerator` — no sequence row; uniqueness enforced by this ticket's unique `(country_code, batch_number)` index. The generator stays pure (`For(countryCode, batchDate)`); TZ-aware local-date derivation happens in the calling handler per T-0062/T-0068a precedent (lands in T-0102a). ADR 0009's "tracked under T-0101" note gets an amendment paragraph recording this split.
- **ADR 0013 (scoping + soft delete).** Admin-only aggregate — repository methods use the `Unscoped` naming convention; no per-customer/per-maker scoped variants (maker-facing payout reads are T-0112's query seam). `Auditable` base ⇒ soft-delete query filter applies, though "Destroyed by: never" per the role doc.
- **ADR 0014 (UoW pipeline + admin audit).** Claim + batch insert are atomic in ONE UoW — the pipeline commits; handlers never call `SaveChangesAsync()` (T-0102a's handler). Mutation in T-0103 flows through EF change tracking; no repository update method.

### C. PM-absorbed (no user input needed)

- **Empty batch** → existing `BusinessErrorMessage.PayoutBatchEmpty` (`payoutBatch.empty`), NO row created, audit records the attempt (T-0102a). No new code needed for this path.
- **Re-run guard:** `GetOpenBatchAsync(countryCode)` returns the single `Processing` batch or null; T-0102a returns the existing batch, never creates a second (US-admin-0007 AC-4).
- **DB-level open-batch guard:** partial unique index `ux_payout_batches_open_per_country` on `(country_code) WHERE state = 'Processing' AND is_active` — belt-and-braces against the Monday-02:00-timer + admin-click race. `Completed` rows are unconstrained.
- **Disputed orders** naturally excluded — state ≠ `Delivered`; no extra predicate term.
- **Q-0017 fix:** leading data-fix migration UPDATEing all 16 single-brace subject rows: SeedOrderEmailTemplates ×4, ShippingPipelineBundle ×4, DeliveryCloseBundle ×2, OrderCleanupBundle ×6 (grep `subject` in those migrations for `{order_number}`-style single-brace values; fix to double-brace). Bodies unaffected (non-interpolated consts). Q-0017 flips open → resolved.
- **CSV blob path layout** `payouts/{cc}/{batchNumber}.csv`, mirroring the invoice blob layout (consumed by T-0102a; the entity only stores the path set-once).
- **Fee-invoice MAKER email** enqueued at batch creation reusing the T-0069 attachment pattern — T-0102a. `payout-sent` settlement emails stay in T-0103 (PR #2).
- **`MakablesMeters.Payouts` instrumentation** — T-0102a per ADR 0023.
- **`Order.AssignToPayoutBatch` enforces set-once ONLY.** The eligibility predicate (Delivered + unclaimed + unrefunded + maker-has-bank-account) lives in T-0102a's claim query — single source of truth. See Alternatives Option E.
- **`PayoutBatchState` stored as string** via `HasConversion`, matching `InvoiceConfiguration`.
- **DI registration** `services.AddScoped<IPayoutBatchRepository, PayoutBatchRepository>()` in `AddMakablesInfrastructure.cs`.

## Scope

### Domain layer

- **`Core.Domain/Payouts/PayoutBatch.cs`** — NEW. Sealed; `Auditable` base; all properties `private set;`. Carries: `Id` (ULID string), `BatchNumber` (immutable, unique-per-country), `CountryCode`, `State` (`PayoutBatchState`), `TotalAmountMinor` (long) + `Currency` (3 chars), `OrderCount` (int), `MakerCount` (int), `CsvBlobPath` (string?, set-once), `CompletedAt` (DateTimeOffset?), `CompletedBy` (string?). Static factory `PayoutBatch.Create(id, batchNumber, countryCode, totalAmountMinor, currency, orderCount, makerCount)` — born in `State = Processing` per lock A.4; throws `ArgumentException` for: blank id/batchNumber/countryCode, currency length ≠ 3, `totalAmountMinor <= 0`, `orderCount < 1`, `makerCount < 1`, `makerCount > orderCount` (empty batches never create a row per §C). Instance method `AttachCsvBlobPath(string)` — set-once, idempotent same-value Success, different-value `BusinessResult.Failure(PayoutBatchCsvPathAlreadySet)`; mirrors `Invoice.AttachPdfBlobPath` (T-0068a). **No `Complete(...)` method** — ships with its only caller in T-0103 (no dead code). `CompletedAt`/`CompletedBy` columns ship now so T-0103 is migration-free.
- **`Core.Domain/Payouts/PayoutBatchState.cs`** — NEW enum: `Processing = 1`, `Completed = 2`. No `Pending` per lock A.4.
- **`Core.Domain/Payouts/IPayoutBatchRepository.cs`** — NEW: `AddAsync`, `GetByIdUnscopedAsync` (IgnoreQueryFilters), `GetOpenBatchAsync(countryCode)` (the re-run guard — single `Processing` batch or null), `GetByNumberAsync`. No `UpdateAsync`/`DeleteAsync` (lock A.4; T-0103 mutates via change tracking). XML docs note the admin-only audience per ADR 0013.
- **`Core.Domain/Orders/Order.cs`** — MODIFIED: `PayoutBatchId` (string?, `private set`) + `AssignToPayoutBatch(string batchId)` domain method — throws `ArgumentException` on blank input; throws `InvalidOperationException` when `PayoutBatchId` is already non-null (**set-once — claiming twice throws**; the T-0102a claim predicate guards first). Update the existing T-0068a-era XML comment on `Invoice.PayoutBatchId` (FK now exists).
- **`Core.Domain/Common/BusinessErrorMessage.cs`** — add `PayoutBatchCsvPathAlreadySet = "payoutBatch.csvPathAlreadySet"` next to the existing `PayoutBatchEmpty`.

### Infrastructure / Database layer

- **`Infra.Database/Payouts/PayoutBatchRepository.cs`** — NEW; primary-ctor DI of `MakablesDbContext`; soft-delete filter automatic.
- **`Infra.Database/Configurations/PayoutBatchConfiguration.cs`** — NEW. Table `payout_batches`, snake-case columns, `state` as string via `HasConversion`. Indexes: unique `(country_code, batch_number)` (`ux_payout_batches_country_batch_number`); partial unique `(country_code) WHERE state = 'Processing' AND is_active` (`ux_payout_batches_open_per_country`).
- **Migration 1 (leading): `<timestamp>_FixEmailSubjectPlaceholders.cs`** — Q-0017 data fix. `UPDATE email_template_translations SET subject = <double-brace value>` for the 16 affected rows (ids enumerable from the 4 seed migrations). `Down` restores the original single-brace values (exact strings known). No schema change.
- **Migration 2: `<timestamp>_PayoutBatches.cs`** — creates `payout_batches`; adds `orders.payout_batch_id` (TEXT NULL) + FK → `payout_batches(id)` ON DELETE RESTRICT + partial index `ix_orders_payout_batch_id WHERE payout_batch_id IS NOT NULL`; adds the `invoices.payout_batch_id` FK constraint → `payout_batches(id)` ON DELETE RESTRICT (closes the T-0068a TODO — all existing invoice rows are Customer-type with NULL `payout_batch_id`, so the constraint applies cleanly).
- **`MakablesDbContext.cs`** — add `DbSet<PayoutBatch>`.
- **`Config/Extensions/AddMakablesInfrastructure.cs`** — register `IPayoutBatchRepository`.
- **`IntegrationTests/Common/PostgresHarness.cs`** — extend `ResetMutableTablesAsync` with `payout_batches` (per the T-0062 signposted expectation).

### Tests (TDD red-first)

`Makables.Tests/Domain/Payouts/PayoutBatchTests.cs` + `Makables.Tests/Domain/Orders/OrderAssignToPayoutBatchTests.cs` (~8 unit tests, red commit precedes green per `docs/process/tdd-policy.md`):

1. **Create_happy_path** — all fields set; `State == Processing`; `CsvBlobPath`/`CompletedAt`/`CompletedBy` null.
2. **Create_throws_on_blank_identity_inputs** (Theory: id / batchNumber / countryCode).
3. **Create_throws_on_invalid_currency_length**.
4. **Create_throws_on_nonpositive_amounts_and_counts** (Theory: total ≤ 0; orderCount < 1; makerCount < 1; makerCount > orderCount).
5. **AttachCsvBlobPath_sets_once** — Success; property set.
6. **AttachCsvBlobPath_idempotent_same_value_fails_different_value** — same value Success; different value `payoutBatch.csvPathAlreadySet`.
7. **AssignToPayoutBatch_sets_batch_id_once** — unclaimed order; `PayoutBatchId` set.
8. **AssignToPayoutBatch_throws_when_already_claimed** — second call (same or different id) throws `InvalidOperationException`; blank id throws `ArgumentException`.

**Integration coverage rides T-0102a in the same PR** (PostgresHarness): migration applies + idempotent re-run, unique-index rejections, open-batch partial unique, `GetOpenBatchAsync` contract, FK enforcement, and the Q-0017 assertion (zero `email_template_translations.subject` rows matching a single-brace placeholder pattern). No separate T-0101 integration files.

### Docs

- `docs/architecture/roles/payout-batch.md` — Status → `Processing | Completed` (no `Pending`, lock A.4); invariants amended with Q3/Q5 exclusions + `MakerCount`; implementation pointer → `Core.Domain/Payouts/PayoutBatch.cs`.
- `docs/adr/0009-numbering.md` — amendment: generator stays pure; TZ-aware `DateOnly` derivation is the calling handler's job (T-0102a).
- `docs/questions/open.md` — Q-0017 open → resolved (data-fix migration in T-0101).
- `docs/tickets/INDEX.md` — refresh T-0101 row (generator already shipped in T-0007; this ticket is the entity slice) — PM flips to done post-merge.

### NSwag regen

None. No controllers, no contract change.

## Alternatives Considered

- **Option A — `Pending → Processing → Completed` three-state lifecycle (role doc's original shape).** *Rejected per A.4* — creation is atomic in one UoW; the batch is never observable in `Pending`. An unreachable enum value is dead state.
- **Option B — Mutable batch with `RemoveOrder` for post-creation corrections.** *Rejected per A.4* — Fee invoices are issued at creation and are legally immutable; removing an order would orphan an issued invoice. Whole-batch cancel is the honest correction unit, deferred to a follow-up.
- **Option C — Net partially-refunded orders into the payout line.** *Rejected per A.3* — silently pays a disputed amount. Exclusion + surfacing forces admin eyes on the row first; the order rides the next batch after resolution.
- **Option D — Fail the whole batch when any maker lacks a `BankAccount`.** *Rejected per A.5* — one incomplete maker profile would block every other maker's weekly payout. Exclude + surface the count instead.
- **Option E — `AssignToPayoutBatch` also asserts `State == Delivered`.** *Rejected* — the claim predicate (T-0102a) is the single source of eligibility truth (Delivered + unclaimed + `RefundedAmountMinor == 0` + maker has bank account). Duplicating a subset inside the domain method creates two half-truths that drift; set-once is the only invariant the entity can own completely.
- **Option F — Allocate batch numbers via `NumberingSequence`.** *Rejected per ADR 0009* — the number is a pure function of country + ISO week; one batch per week max; the unique index is the cheaper, race-free enforcement. No sequence row.
- **Option G — Defer the `invoices.payout_batch_id` FK to T-0102a.** *Rejected* — T-0068a's inline comment names T-0101; the FK belongs in the migration that creates the referenced table, and T-0102a must be able to write Fee rows against an enforced constraint from its first test.
- **Option H — Fold the Q-0017 fix into the PayoutBatches migration.** *Rejected* — logically unrelated change; separate migrations keep `Down` paths independent and the review diff honest.

## Out of scope

- `CreatePayoutBatch` command, claim query, fee-invoice issuance, `IPayoutCsvFormatter` + CSV generation, blob upload, audit entry, maker fee-invoice email, `MakablesMeters.Payouts` — **T-0102a** (same PR).
- `MarkPayoutBatchCompleted` command + `PayoutBatch.Complete(...)` domain method + orders `Delivered → Completed` + `payout-sent` emails — **T-0103** (PR #2).
- Timer Function — **T-0104**. Maker payout queries — **T-0112**. Frontend — **T-0116/T-0118**.
- Bank-native CSV exporters — follow-up once the operator names the bank (lock A.1).
- Whole-batch cancel — deferred follow-up (lock A.4).
- TZ-aware date derivation for the batch number — caller's job, lands in T-0102a.

## Acceptance criteria

- **AC-1** Given `PayoutBatch.Create` with valid inputs, when the factory runs, then the batch is born in `State = Processing` with all fields set and `CsvBlobPath`/`CompletedAt`/`CompletedBy` null. Given blank id/batchNumber/countryCode, currency length ≠ 3, `totalAmountMinor <= 0`, `orderCount < 1`, `makerCount < 1`, or `makerCount > orderCount`, then it throws `ArgumentException` naming the failing invariant.
- **AC-2** Given a batch with `CsvBlobPath == null`, when `AttachCsvBlobPath("payouts/cz/VYP-CZ-2026-W24.csv")` is called, then Success and the property is set. A repeat call with the same value returns Success idempotently; a call with a different value returns `BusinessResult.Failure(payoutBatch.csvPathAlreadySet)`.
- **AC-3** Given an order with `PayoutBatchId == null`, when `AssignToPayoutBatch(batchId)` is called, then the id is set. A second call — same or different id — throws `InvalidOperationException`; a blank id throws `ArgumentException`.
- **AC-4** Given both migrations applied to an empty postgres:16-alpine container, when `MigrateAsync()` completes, then `payout_batches` exists with all columns + both indexes; `orders.payout_batch_id` exists with FK + partial index; the `invoices.payout_batch_id` FK constraint exists (T-0068a TODO closed). Re-run is idempotent.
- **AC-5** Given an existing batch row, when a second row with the same `(country_code, batch_number)` is saved, then Postgres rejects via `ux_payout_batches_country_batch_number`. Given an existing `Processing` batch for CZ, when a second `Processing` CZ row is saved, then Postgres rejects via `ux_payout_batches_open_per_country`; a `Completed` row saves fine.
- **AC-6** Given the data-fix migration has run, when `email_template_translations.subject` is scanned, then all 16 previously-affected rows contain double-brace placeholders and zero rows match a single-brace placeholder pattern (`{key}` not preceded/followed by another brace). `Down` restores the prior values.
- **AC-7** Given one `Processing` batch and N `Completed` batches for CZ, when `GetOpenBatchAsync("CZ")` runs, then it returns exactly the `Processing` batch; when none is open, it returns null. (Verified via T-0102a's PostgresHarness fixtures in the same PR.)
- **AC-8** Build clean. Unit tests: baseline + ~8 new, with the red commit preceding green per TDD policy. Integration: baseline (new assertions ride T-0102a). `node scripts/check-consistency.mjs` exit 0. No NSwag diff. Role doc, ADR 0009 amendment, Q-0017 resolution, and INDEX refresh committed in the same PR.

## Technical notes

### Why `Complete()` does not ship here

T-0068a shipped `AttachPdfBlobPath` ahead of its caller because the caller (T-0068b) was the very next slice of the same feature. `Complete()`'s caller is T-0103 in PR #2 — shipping the method now would leave dead code on master across a PR boundary, violating the no-dead-code rule. The `completed_at`/`completed_by` columns DO ship now so T-0103 is migration-free, matching the T-0068a "column now, behavior later" seam.

### Why `OrderCount`/`MakerCount` are denormalized ints

The batch is immutable (lock A.4), so the counts can never drift from the claimed rows — they are a creation-time snapshot, not a maintained aggregate. Storing them avoids a COUNT join on every admin list/detail render (T-0118) and on the maker payout list (T-0112), and makes the batch row self-describing in audit exports.

### Why the open-batch guard is a partial unique index (not just `GetOpenBatchAsync`)

The repository check is read-then-write: timer (Monday 02:00 UTC) and an admin click can interleave between the read and the commit. The partial unique index `(country_code) WHERE state = 'Processing' AND is_active` makes the second commit fail at the database, turning a race into a deterministic unique-violation the T-0102a handler maps to "return the existing batch". Same belt-and-braces philosophy as ADR 0009's numbering uniqueness.

### Why `RESTRICT` on both FKs

Orders and Fee invoices reference the batch that paid/charged them — legal traceability. Deleting a batch row (even soft-delete is "never" per the role doc) must not cascade into orphaning settlement history; `RESTRICT` makes any future hard-delete attempt fail loudly.

## Risk

- **FK addition on populated `invoices`** — low: every existing row is Customer-type with NULL `payout_batch_id`; the constraint validates trivially. Guard: T-0102a's harness asserts the constraint exists before writing Fee rows.
- **Migration ordering** — the data-fix migration MUST carry an earlier timestamp than `PayoutBatches` (generate it first). Both reverse cleanly: data-fix `Down` restores known strings; `PayoutBatches` `Down` drops constraints/column/table before any data exists.
- **Role-doc drift** — the published role doc says `Pending | Processing | Completed`; lock A.4 removes `Pending`. The doc update ships in this PR so the next reader sees one truth.
- **Open-batch partial index vs multi-country future** — the guard is per-country by design; a second country gets its own open batch. No CZ-hardcoding (ADR 0004 honoured).

## Test plan reference

Inline above (see Scope > Tests). No separate `docs/test-plans/T-0101.md`. Integration assertions land in T-0102a's files within the same PR.

## Files touched (expected)

### New
- `backend/src/Makables.Core.Domain/Payouts/PayoutBatch.cs`
- `backend/src/Makables.Core.Domain/Payouts/PayoutBatchState.cs`
- `backend/src/Makables.Core.Domain/Payouts/IPayoutBatchRepository.cs`
- `backend/src/Makables.Infra.Database/Payouts/PayoutBatchRepository.cs`
- `backend/src/Makables.Infra.Database/Configurations/PayoutBatchConfiguration.cs`
- `backend/src/Makables.Infra.Database/Migrations/<timestamp>_FixEmailSubjectPlaceholders.cs`
- `backend/src/Makables.Infra.Database/Migrations/<timestamp>_PayoutBatches.cs`
- `backend/src/Makables.Tests/Domain/Payouts/PayoutBatchTests.cs`
- `backend/src/Makables.Tests/Domain/Orders/OrderAssignToPayoutBatchTests.cs`

### Modified
- `backend/src/Makables.Core.Domain/Orders/Order.cs` — `PayoutBatchId` + `AssignToPayoutBatch`
- `backend/src/Makables.Core.Domain/Invoices/Invoice.cs` — XML comment update (FK now exists)
- `backend/src/Makables.Core.Domain/Common/BusinessErrorMessage.cs` — `PayoutBatchCsvPathAlreadySet`
- `backend/src/Makables.Infra.Database/MakablesDbContext.cs` — `DbSet<PayoutBatch>`
- `backend/src/Makables.Config/Extensions/AddMakablesInfrastructure.cs` — DI registration
- `backend/src/Makables.IntegrationTests/Common/PostgresHarness.cs` — `ResetMutableTablesAsync` + `payout_batches`
- `docs/architecture/roles/payout-batch.md`, `docs/adr/0009-numbering.md`, `docs/questions/open.md`, `docs/tickets/INDEX.md`

## Commits hint

1. `test(T-0101): pin PayoutBatch.Create + Order.AssignToPayoutBatch invariants (red)`
2. `feat(T-0101): PayoutBatch entity + repository + Order.PayoutBatchId + DI (green)`
3. `feat(T-0101): Q-0017 subject data-fix migration + PayoutBatches migration`
4. `docs(T-0101): role doc, ADR 0009 amendment, Q-0017 resolution, INDEX refresh`

## Status log

- 2026-06-12 `draft` by PM. Created as the entity slice of the payout bundle (T-0101 + T-0102a one PR; T-0103 PR #2), mirroring the T-0068a entity-slice precedent. Closes the T-0068a `invoices.payout_batch_id` FK TODO and Q-0017.
- 2026-06-12 `draft → ready` by BA. User locked 5 bundle-wide decisions (Q1 CSV format seam; Q2 per-batch fee invoices, DUZP = batch creation date; Q3 partially-refunded orders excluded; Q4 immutable batch, born Processing; Q5 NULL-bank-account makers excluded). 12 PM-absorbed decisions captured in §C with kill reasons in Alternatives. **Ready for dotnet-backend.** Implementer processes T-0101 → T-0102a sequentially in the same branch; one PR.

## Definition of Ready

- [x] Sized M (entity + repo + 2 migrations + 8 unit tests — no L-split needed)
- [x] Owner routed (dotnet-backend; pure backend slice, no contract change)
- [x] User decisions locked (5) and recorded in §A; PM-absorbed in §C
- [x] Dependencies on master: T-0007 (generator), T-0060 (Order), T-0068a (invoices column)
- [x] AC observable + traceable to US-admin-0007; test plan inline
- [x] No open questions blocking — Q-0017 resolved BY this ticket
