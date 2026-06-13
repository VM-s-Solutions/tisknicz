---
id: T-0102a
title: CreatePayoutBatch command — claim Delivered orders into an immutable weekly batch
status: ready
size: M
owner: dotnet-backend
created: 2026-06-12
updated: 2026-06-12
depends_on: [T-0101, T-0105]
blocks: [T-0102b, T-0103, T-0104, T-0118]
user_stories: [US-admin-0007]
adrs: [0009, 0013, 0014, 0023]
phase: 5
manual_steps: []
security_touching: true
layers: [domain, appservices, infra-database, web-admin]
---

# T-0102a — CreatePayoutBatch command — claim Delivered orders into an immutable weekly batch

## Context

T-0102a is the **claim ticket of the payout bundle** (T-0101 PayoutBatch entity + repository → T-0102a claim command → T-0102b fee invoices + CSV export → T-0103 MarkPayoutBatchCompleted → T-0104 timer Function). It ships the `CreatePayoutBatch` one-file feature on the **admin host**: claim every payout-eligible `Delivered` order for CZ into a new `PayoutBatch` row born in `Processing`, sum `Order.MakerPayoutAmountMinor` into the batch total, and surface what was excluded and why. This directly satisfies **US-admin-0007** AC-1 (claim), AC-3 (empty run) and AC-4 (re-run guard); fee invoices + CSV (AC-1 remainder) land in T-0102b extending this handler in the same PR; AC-2 (mark paid + `payout-sent` emails) is T-0103 (PR #2).

This is a **money-aggregation admin command** (`security_touching: yes`): the sum it writes is the amount the operator will wire from the company bank account. The pricing invariant on `Order.Create` (`backend/src/Makables.Core.Domain/Orders/Order.cs:449-453` — MakerPayout + PlatformFee == Product + Shipping) guarantees per-order payout integrity; this ticket's job is to claim the right rows, exactly once, atomically. The **eligibility predicate is the red-first TDD surface**, pinned as a pure, table-driven specification class before any infrastructure exists.

The ticket also carries the bundle's **leading data-fix migration resolving Q-0017**: 16 `email_template_translations.subject` rows were seeded with single-brace placeholders (`#{order_number}`) because the seed SQL was built inside C# `$@"..."` interpolated strings where `{{` collapses to `{` — the subject literals inline in the SQL got mangled while the bodies (built via non-interpolated constants) stayed correct. The renderer expects double-brace `{{order_number}}` tokens, so every affected subject currently renders the raw placeholder to recipients.

## Locked design decisions

Captured per `docs/process/deliberation.md`. The user locked 5 dimensions at the 2026-06-12 payout-bundle deliberation (Q1–Q5 below; Q1/Q2 are implemented by T-0102b but recorded here because the deliberation covered the bundle). PM absorbed the rest.

### A. User-locked 2026-06-12 (non-negotiable)

1. **Q1 — Generic documented CSV behind a format seam.** `IPayoutCsvFormatter` (keyed-service-ready per the provider-adapter pattern); columns: account, amount as CZK decimal display, VS = batch/order number, message. Implementation lands in T-0102b; bank-native exporters (ABO/Gpc etc.) are follow-up tickets once the operator names the bank. **Rejected:** picking a bank-native format now (operator's bank unknown; wrong guess = rework + a useless exporter).
2. **Q2 — Fee invoices per-batch at CreatePayoutBatch.** One `InvoiceType.Fee` invoice per maker per batch; DUZP = batch creation date; shared FV-CZ sequence per T-0068a lock 4. Implementation in T-0102b (same PR, extends this handler). **Rejected:** per-order fee invoices (invoice-count explosion, no legal need); monthly fee invoicing (decouples invoice from the payout it explains).
3. **Q3 — Partially-refunded Delivered orders (`RefundedAmountMinor > 0`) EXCLUDED from auto-claim.** They stay unclaimed, are surfaced in the batch response + audit, and ride the next batch after admin resolution (e.g. further refund to full, or dispute settlement). **Rejected:** claim at net payout-minus-refund-share (couples refund apportionment math into the claim; how a partial refund splits between maker and platform is an admin judgement call at MVP); claim at full payout (knowingly overpays the maker).
4. **Q4 — Batch IMMUTABLE once created.** Born `Processing`; no order removal (T-0102b's fee invoices are issued per batch and are legally immutable once numbered). Whole-batch cancel is a deferred follow-up. **Rejected:** mutable batch with remove-order (every removal would require a credit note + total recompute + CSV regeneration — a correctness minefield for MVP volume).
5. **Q5 — Orders of makers with NULL `BankAccount` EXCLUDED from claim** (`Maker.BankAccount` is nullable, `backend/src/Makables.Core.Domain/Makers/Maker.cs:94-98`). Excluded-maker count + their order count surfaced in response + audit. **Rejected:** fail the whole batch (one negligent maker blocks every other maker's money); claim-and-park (creates a payable row that cannot appear in any CSV — an unpayable liability).

### B. ADR-locked (no relitigation)

- **ADR 0009 (numbering).** Batch number `VYP-CZ-YYYY-Www` via the existing `IPayoutBatchNumberGenerator.For(countryCode, batchDate)` (T-0007, on master). No sequence-table allocation; uniqueness via the `(country_code, batch_number)` unique index (T-0101).
- **ADR 0013 (per-audience hosts).** Admin host only; `[Authorize]` with admin JWT audience; unscoped repository reads are admin-host-only.
- **ADR 0014 (UoW + audit).** Claim + batch insert + audit row commit **atomically in ONE UnitOfWork** — the pipeline commits; the handler never calls `SaveChangesAsync()`. Audit rows ride only committed transactions.
- **ADR 0023 (observability).** `MakablesMeters.Payouts` (`Makables.Config/Observability/MakablesMeters.cs:29`, name registered but instrument-less until now) gets its first instruments in this ticket.
- **Patterns §A.4 `BusinessResult<T>`.** Every expected failure is a `BusinessErrorMessage` code; no exceptions for business outcomes. One-file feature; globally-unique response name (post-PR-#38 NSwag convention).

### C. PM-absorbed (no user input needed)

1. **Empty run → `payoutBatch.empty` failure, NO batch row.** Code already exists (`BusinessErrorMessage.PayoutBatchEmpty`, `BusinessErrorMessage.cs:416`); this ticket ships its cs-CZ key. The attempt is recorded via a structured warning log (with exclusion counts, so ops sees *why* it was empty) + the `batch_runs{outcome=empty}` counter. **Documented deviation from US-admin-0007 AC-3's literal "audit entry":** an `admin_audit_log` row cannot ride a failed command — `AdminAuditPipelineBehavior` skips failures and the UoW rolls back — so the attempt trail is telemetry, not a DB row.
2. **Re-run with an open `Processing` batch → return the existing batch, never a second.** `IPayoutBatchRepository.GetOpenBatchAsync(countryCode)` guard; Silent-Success shape (T-0067/T-0076/T-0105 precedent) with `AlreadyExisted = true`, stored counts echoed, no mutation, no new audit row.
3. **Same-ISO-week re-run after the batch completed → `payoutBatch.weekAlreadyProcessed` (Conflict), pre-checked via `GetByNumberAsync`** (BA-caught gap: the number generator is deterministic per week, so a second same-week batch would die on the unique index as a raw 500 without this guard). Excluded orders from Q3/Q5 therefore ride **next week's** batch.
4. **Disputed orders naturally excluded** — `State != Delivered` (dispute moves state to `Disputed`; escrow holds). No special-casing.
5. **TZ-aware local-date derivation in the handler** per the T-0062/T-0068a precedent (`IssueInvoice.ToCountryLocalDate`, `CountryConfiguration.TimeZoneId`): a Sunday 23:30 UTC run is Monday 01:30 Prague — next ISO week.
6. **Country resolution:** `AuthDefaultCountryOptions.CountryCodePrimary` (the existing default-country seam) → `ICountryConfigurationRepository.GetByCodeAsync`; missing row → `country.configMissing`. No `if (countryCode == "CZ")` anywhere.
7. **Audit row written by the handler via `IAdminAuditLogWriter`**, NOT via `IAdminAuditableCommand` — the pipeline contract requires `TargetId` at command time, which a create-command cannot name. `actionCode = "payoutBatch.create"`, `targetEntity = "payout_batch"`, `targetId = batch.Id`, `beforeJson = null`, `afterJson` = run summary (batch number, total, currency, order/maker counts, all three exclusion counts). Rides the same UoW. Fail-closed session check first (RefundOrder.cs step-1 precedent — money movement is never attributed to "system").
8. **Q-0017 = leading data-fix migration** `FixEmailTemplateSubjectBraces` UPDATEing all 16 single-brace subject rows: SeedOrderEmailTemplates ×4 (order-paid-customer, order-placed-maker × cs/en), ShippingPipelineBundle ×4 (order-accepted-customer, order-shipped-customer × cs/en), DeliveryCloseBundle ×2 (order-delivered-customer × cs/en), OrderCleanupBundle ×6 (order-message-posted-customer, order-message-posted-maker, order-cancelled-auto-customer × cs/en). Old migrations stay untouched — fresh DBs replay the buggy seeds then this fix corrects them in sequence.
9. **Currency-homogeneity guard → `payoutBatch.currencyMismatch`** (Conflict + `LogCritical`). Defensive money math: summing mixed currencies into one `TotalAmountMinor` would silently corrupt the wire amount. Batch currency = `CountryConfiguration.DefaultCurrencyCode`.
10. **Exclusion counts persisted as columns on `payout_batches`** (set at creation, immutable per Q4) — the re-run path and the T-0118 admin UI read them back without re-scanning. Consumed contract from T-0101's entity.
11. **Bundle placement:** CSV blob path `payouts/{cc}/{batchNumber}.csv` mirroring the invoice blob layout + fee-invoice **maker** email reusing the T-0069 attachment pattern → both T-0102b. `payout-sent` settlement emails → T-0103 (PR #2). NSwag admin regen → the bundle's final regen commit.
12. **Eligibility = repository coarse filter + pure specification fine filter.** The DB query fetches candidates (`Delivered` + unbatched + country); `PayoutEligibility.Classify` (pure, table-driven-testable) does the per-order verdict. Classification precedence: not-claimable → partially-refunded → no-bank-account → eligible.

## Scope

### Domain layer

- **`Core.Domain/Payouts/PayoutEligibility.cs`** — NEW pure static specification (**the red-first TDD surface**):
  ```csharp
  public enum PayoutEligibilityVerdict { Eligible, ExcludedPartiallyRefunded, ExcludedNoBankAccount, NotClaimable }

  public static class PayoutEligibility
  {
      public static PayoutEligibilityVerdict Classify(
          OrderState state, string? payoutBatchId, long refundedAmountMinor, string? makerBankAccount);
  }
  ```
  `NotClaimable` when `state != Delivered` or `payoutBatchId != null` (belt-and-braces — the repository query already filters these); then `ExcludedPartiallyRefunded` when `refundedAmountMinor > 0` (Q3); then `ExcludedNoBankAccount` when `makerBankAccount` is null/whitespace (Q5); else `Eligible`.
- **`Core.Domain/Orders/Order.cs`** — NEW `AssignToPayoutBatch(string payoutBatchId)`: `BusinessResult`; refuses with `BusinessErrorMessage.OrderInvalidTransition` unless `State == Delivered && PayoutBatchId == null` (set-once). Order stays in `Delivered` — the `Delivered → Completed` transition is T-0103's.
- **`Core.Domain/Orders/IOrderRepository.cs`** — NEW `GetPayoutEligibleUnscopedAsync(string countryCode, CancellationToken ct)`: tracked, `Include(o => o.Maker)`, `Where(o => o.State == OrderState.Delivered && o.PayoutBatchId == null && o.CountryCode == countryCode)`. Doc comment states it returns *candidates*; fine-grained eligibility is `PayoutEligibility.Classify`. Unscoped = admin host only (ADR 0013).
- **`Core.Domain/Observability/IPayoutMetrics.cs`** — NEW interface (pure, no packages): `RecordRun(string outcome)`, `RecordClaimed(int orderCount, long totalAmountMinor)`, `RecordExcluded(string reason, int orderCount)`.
- **`Core.Domain/Common/BusinessErrorMessage.cs`** — extend the `=== Payout batch ===` block: `PayoutBatchWeekAlreadyProcessed = "payoutBatch.weekAlreadyProcessed"`, `PayoutBatchCurrencyMismatch = "payoutBatch.currencyMismatch"` (`PayoutBatchEmpty` already exists).

### AppServices layer

- **`Core.AppServices/Features/PayoutBatches/CreatePayoutBatch.cs`** — NEW one-file feature:
  - `Command() : ICommand<CreatePayoutBatchResponse>` — **no parameters**; claims everything eligible for the default country at MVP. No Validator (nothing to validate).
  - `CreatePayoutBatchResponse(string BatchId, string BatchNumber, PayoutBatchState State, long TotalAmountMinor, string Currency, int OrderCount, int MakerCount, int ExcludedPartiallyRefundedOrderCount, int ExcludedNoBankAccountOrderCount, int ExcludedNoBankAccountMakerCount, bool AlreadyExisted)` — globally-unique name.
  - `Handler(IOrderRepository orders, IPayoutBatchRepository payoutBatches, IPayoutBatchNumberGenerator numberGenerator, ICountryConfigurationRepository countries, IOptions<AuthDefaultCountryOptions> defaultCountry, IAdminAuditLogWriter auditWriter, IUserSessionProvider session, IIdGenerator idGenerator, IClock clock, IPayoutMetrics metrics, ILogger<Handler> logger)`; steps (NO `SaveChangesAsync()`):
    1. **Fail-closed session check** → `Error.Unauthorized()` when `session.GetUserId()` is empty (RefundOrder precedent).
    2. **Resolve country + config** — `defaultCountry.Value.CountryCodePrimary` → `GetByCodeAsync`; null → `country.configMissing`.
    3. **Re-run guard** — `GetOpenBatchAsync(countryCode)` → existing `Processing` batch → `metrics.RecordRun("already_open")`; return Silent Success with `AlreadyExisted = true` built from the stored row (incl. persisted exclusion counts). No mutation, no audit row.
    4. **Derive batch number** — `ToCountryLocalDate(clock.UtcNow, config.TimeZoneId)` → `numberGenerator.For(countryCode, localDate)`.
    5. **Week guard** — `GetByNumberAsync(countryCode, batchNumber)` non-null (necessarily a closed batch, given step 3) → `payoutBatch.weekAlreadyProcessed` Conflict; `RecordRun("week_already_processed")`.
    6. **Load + classify** — `GetPayoutEligibleUnscopedAsync(countryCode)`; partition via `PayoutEligibility.Classify` into eligible / excluded-partially-refunded / excluded-no-bank (order list + distinct maker count).
    7. **Empty** — eligible set empty → `LogWarning` with all exclusion counts + `RecordRun("empty")` + `RecordExcluded(...)`; return `payoutBatch.empty` failure. **No row.**
    8. **Currency guard** — any eligible order's `Currency != config.DefaultCurrencyCode` → `LogCritical` + `payoutBatch.currencyMismatch`; `RecordRun("currency_mismatch")`.
    9. **Claim (ONE UoW)** — `PayoutBatch.Create(idGenerator.Next(), batchNumber, countryCode, currency, total = Σ MakerPayoutAmountMinor, orderCount, distinct makerCount, three exclusion counts)` born `Processing` → `payoutBatches.AddAsync`; then `order.AssignToPayoutBatch(batch.Id)` per eligible order (a refusal here is a programmer error → surface the failure; UoW rolls everything back).
    10. **Audit + metrics + return** — `auditWriter.AppendAsync(AdminAuditLogEntry.Record(... "payoutBatch.create", "payout_batch", batch.Id, beforeJson: null, afterJson: run-summary JSON, notes: null))`; `RecordRun("created")`, `RecordClaimed(...)`, `RecordExcluded(...)`; return Success. The pipeline commits batch + N claims + audit atomically (ADR 0014).

### Infrastructure / Database layer

- **`Infra.Database/Orders/OrderRepository.cs`** — implement `GetPayoutEligibleUnscopedAsync` (tracked, `Include(o => o.Maker)`, no `IgnoreQueryFilters` — soft-deleted orders stay invisible).
- **`Infra.Database/Migrations/2026xxxx_FixEmailTemplateSubjectBraces.cs`** — NEW **data-only leading migration** (first commit of the bundle PR): `UPDATE email_template_translations SET subject = REPLACE(subject, '{order_number}', '{{order_number}}') WHERE subject LIKE '%{order_number}%' AND subject NOT LIKE '%{{order_number}}%'` (16 rows). `Down()` reverses the REPLACE. Model snapshot unchanged.
- **`Config/Observability/PayoutMetrics.cs`** — NEW `IPayoutMetrics` impl on meter `MakablesMeters.Payouts`: counter `makables.payouts.batch_runs` (tag `outcome` ∈ created | already_open | empty | week_already_processed | currency_mismatch), counter `makables.payouts.orders_claimed`, counter `makables.payouts.orders_excluded` (tag `reason` ∈ partially_refunded | no_bank_account), histogram `makables.payouts.batch_amount_minor`. Singleton via `IMeterFactory`; registered where `AddMakablesObservability` already calls `AddMeter(MakablesMeters.Payouts)`. Note: instruments record before the UoW commit — a failed commit leaves a counted-but-uncommitted run; acceptable telemetry noise, documented in the class doc.
- **DI:** register `IPayoutMetrics` (observability wiring); repository method needs no new registration.

### Web.Admin host

- **`Web.Admin/Controllers/PayoutBatchesController.cs`** — NEW; `[Authorize]` (admin audience per ADR 0013); `[HttpPost("")]` → `POST /api/v1/payout-batches`; `[ProducesResponseType(typeof(CreatePayoutBatchResponse), StatusCodes.Status200OK)]`; one-liner `mediator.Send(new CreatePayoutBatch.Command(), ct)`. 200 on both created and `AlreadyExisted` paths (Silent-Success shape).

### i18n

- **`frontend/src/lib/i18n/cs-CZ.ts`** — `'payoutBatch.empty': 'Žádné objednávky připravené k výplatě.'`, `'payoutBatch.weekAlreadyProcessed': 'Výplatní dávka pro tento týden už byla zpracována.'`, `'payoutBatch.currencyMismatch': 'Objednávky v dávce nemají jednotnou měnu. Kontaktujte podporu.'`

### Tests

#### PayoutEligibilityTests (NEW, table-driven, RED-FIRST — commit before any implementation)

`backend/src/Makables.Tests/Domain/Payouts/PayoutEligibilityTests.cs` — `[Theory]` matrix pinning: Delivered + unbatched + 0 refund + bank → `Eligible`; `refundedAmountMinor > 0` → `ExcludedPartiallyRefunded` (Q3); bank null/whitespace → `ExcludedNoBankAccount` (Q5); refund > 0 AND bank null → `ExcludedPartiallyRefunded` (precedence pinned); each non-Delivered state (incl. `Disputed`, `Completed`) → `NotClaimable`; `payoutBatchId != null` → `NotClaimable`. Counts as ~4 of the unit budget.

#### CreatePayoutBatchHandlerTests (NEW, ~6 unit tests)

NSubstitute mocks. 1. **Happy path** — 3 eligible orders across 2 makers: batch created `Processing`; total = Σ `MakerPayoutAmountMinor`; `AssignToPayoutBatch` called per order; audit appended once with `afterJson` carrying counts + exclusions; metrics `created`. 2. **Re-run open batch** — `GetOpenBatchAsync` returns a batch: response `AlreadyExisted = true`, `AddAsync` never called, no audit append. 3. **Empty** — only excluded candidates: `payoutBatch.empty`, no `AddAsync`, exclusion counts logged + metered. 4. **Exclusions partitioned** — mixed candidate set: response carries all three exclusion counts; only eligible rows claimed. 5. **Week guard** — `GetByNumberAsync` returns a closed batch → `payoutBatch.weekAlreadyProcessed`. 6. **TZ week boundary** — clock pinned Sunday 23:30 UTC, `TimeZoneId = Europe/Prague` → generator called with Monday's local date (next ISO week). (Plus: empty session → Unauthorized; currency mismatch → failure — fold into 1–2 asserts where natural.)

#### CreatePayoutBatchIntegrationTests (NEW, ~4 integration tests + 1 migration check)

Testcontainers Postgres + admin `WebApplicationFactory`. 1. **Claim e2e** — seed Delivered orders for 2 makers with bank accounts; POST → 200; one `payout_batches` row in `Processing` with correct number/total/counts; each order's `payout_batch_id` set; one `admin_audit_log` row `payoutBatch.create`. 2. **Re-run** — second POST → 200, same `BatchId`, `AlreadyExisted = true`, still exactly one batch row. 3. **Exclusions** — seed a partially-refunded Delivered order + an order whose maker has NULL `BankAccount` + one eligible: only the eligible row claimed; response counts match; excluded rows stay unbatched. 4. **Empty** — no candidates → error envelope `payoutBatch.empty`; zero batch rows. 5. **Q-0017** — post-migration, `SELECT count(*) FROM email_template_translations WHERE subject LIKE '%{order_number}%' AND subject NOT LIKE '%{{order_number}}%'` returns 0 and the 16 known rows contain `{{order_number}}`.

### NSwag regen

New admin endpoint → contract change; regenerated admin client lands in the **bundle's final regen commit** (one regen for T-0102a + T-0102b + T-0103 surfaces). No manual edits to `frontend/src/lib/api-client/`.

## Alternatives Considered

- **Option A — Claim partially-refunded orders at net amount.** *Rejected per Q3* — apportioning a partial refund between maker payout and platform fee is an admin judgement call at MVP; auto-netting bakes a business rule nobody locked into money math.
- **Option B — Fail the whole run when any maker lacks a bank account.** *Rejected per Q5* — one incomplete profile would freeze every maker's weekly money. Exclude + surface + ride next batch.
- **Option C — Mutable batch with order removal.** *Rejected per Q4* — T-0102b issues numbered fee invoices per batch; removal would demand credit notes + total recompute + CSV regeneration.
- **Option D — `IAdminAuditableCommand` with a sentinel `TargetId` ("CZ").** *Rejected per C.7* — the pipeline's before/after snapshots resolve by `FindAsync(TargetId)` and would both be null; the audit row would name no batch. Handler-written entry carries the real batch id + a meaningful `afterJson`, inside the same UoW.
- **Option E — Skip the week guard, let the unique index catch same-week re-runs.** *Rejected per C.3* — a constraint violation surfaces as an unexplained 500; the pre-check returns a typed, translated Conflict.
- **Option F — Cron-triggered creation in this ticket.** *Rejected* — T-0104 owns the Monday 02:00 UTC timer; it will call this same command. Shipping command-first keeps the admin button and the timer on one code path.
- **Option G — Eligibility entirely in SQL.** *Rejected per C.12* — the maker-bank + refund predicates in a single EF query are testable only against a live DB; the pure spec gives a table-driven red-first surface and the DB query stays a trivially-reviewable coarse filter.

## Out of scope

- **Fee invoices + CSV generation/storage + fee-invoice maker email** — T-0102b (same PR; Q1/Q2 + blob path + T-0069 attachment pattern locked above).
- **Marking the batch paid, `Delivered → Completed`, `payout-sent` emails** — T-0103 (PR #2).
- **Timer/HTTP Function trigger** — T-0104.
- **Whole-batch cancel** — deferred follow-up per Q4.
- **Negative-balance carryover for post-payout refunds** (US-admin-0008 AC-2 warning exists in T-0105) — payout-side netting is post-MVP.
- **Admin payout UI** — T-0118. Maker payout list — T-0112/T-0116.
- **Multi-country iteration** — handler is country-parameterized internally; only the default country runs at MVP.

## Acceptance criteria

- **AC-1** Given 3 Delivered, unbatched, unrefunded orders across 2 makers with bank accounts, when an admin POSTs `/api/v1/payout-batches`, then 200 with one new batch: `State = Processing`, `BatchNumber = VYP-CZ-YYYY-Www`, `TotalAmountMinor` = Σ `MakerPayoutAmountMinor`, `OrderCount = 3`, `MakerCount = 2`, `AlreadyExisted = false`; each order's `payout_batch_id` set; orders remain `Delivered`.
- **AC-2** Given an open `Processing` batch exists, when the admin re-runs, then 200 returns **that** batch with `AlreadyExisted = true`; no second row, no new audit entry (US-admin-0007 AC-4).
- **AC-3** Given a Delivered order with `RefundedAmountMinor > 0`, when the batch runs, then it is NOT claimed; `ExcludedPartiallyRefundedOrderCount` reflects it in response, batch row, and audit `afterJson` (Q3).
- **AC-4** Given a Delivered order whose maker has `BankAccount = null`, when the batch runs, then it is NOT claimed; `ExcludedNoBankAccountOrderCount` + `ExcludedNoBankAccountMakerCount` (distinct makers) surfaced the same three ways (Q5).
- **AC-5** Given orders in `Disputed`, `Shipped`, `Completed`, or already batched, when the batch runs, then none are claimed (state/batch predicate; Disputed needs no special-casing).
- **AC-6** Given zero eligible orders, when the run triggers, then `payoutBatch.empty` failure, **no batch row**, a structured warning naming the exclusion counts, and `batch_runs{outcome=empty}` incremented (documented AC-3-of-US deviation per §C.1).
- **AC-7** Given this ISO week's batch was already created and completed, when the admin re-runs in the same week, then `payoutBatch.weekAlreadyProcessed` Conflict and no row.
- **AC-8** Given the claim path runs, then batch insert + all order claims + the `payoutBatch.create` audit row (real batch id, `afterJson` run summary) commit in ONE transaction; no `SaveChangesAsync()` in the handler; a forced mid-claim failure persists nothing.
- **AC-9** Given the clock reads Sunday 23:30 UTC with `TimeZoneId = Europe/Prague`, when the batch number derives, then it uses Monday's local date (next ISO week) — TZ-aware per §C.5.
- **AC-10** Given an anonymous request or a customer/maker JWT, when POSTing, then 401/403 — admin audience enforced per host; empty session inside the handler fails closed as Unauthorized.
- **AC-11** Given the data-fix migration has run, then zero `email_template_translations.subject` values contain single-brace `{order_number}` and all 16 affected rows contain `{{order_number}}` (Q-0017 closed).
- **AC-12** Build clean; unit baseline + ~10 new; integration baseline + ~5 new; `node scripts/check-consistency.mjs` exit 0; new error codes have cs-CZ keys; NSwag admin regen in the bundle's final regen commit.

## Risk notes

- **Money aggregation** — `long` minor-unit sums (overflow unrealistic at MVP scale; pricing invariant at `Order.cs:449-453` guarantees per-order integrity). The currency guard (§C.9) prevents silent cross-currency summing.
- **Generator year-boundary quirk (observed, not fixed here):** `PayoutBatchNumberGenerator` uses `batchDate.Year` with `ISOWeek.GetWeekOfYear` — a Jan 1–3 batch falling in ISO week 52/53 of the *prior* year would be labelled with the new year (e.g. `VYP-CZ-2027-W53`). Cosmetic (uniqueness holds); flag to PM as a micro-follow-up before the first year boundary.
- **US wording supersession:** US-admin-0007 AC-1 says "`Pending` → `Processing`"; Q4 locks born-`Processing` (no observable Pending window). The story should be updated by BA post-merge.
- **Metrics pre-commit emission** — see §Infrastructure note.

## Files touched (expected)

### New
- `backend/src/Makables.Core.Domain/Payouts/PayoutEligibility.cs`
- `backend/src/Makables.Core.Domain/Observability/IPayoutMetrics.cs`
- `backend/src/Makables.Core.AppServices/Features/PayoutBatches/CreatePayoutBatch.cs`
- `backend/src/Makables.Infra.Database/Migrations/2026xxxx_FixEmailTemplateSubjectBraces.cs` (+ Designer)
- `backend/src/Makables.Config/Observability/PayoutMetrics.cs`
- `backend/src/Makables.Web.Admin/Controllers/PayoutBatchesController.cs`
- `backend/src/Makables.Tests/Domain/Payouts/PayoutEligibilityTests.cs`
- `backend/src/Makables.Tests/AppServices/Features/PayoutBatches/CreatePayoutBatchHandlerTests.cs`
- `backend/src/Makables.IntegrationTests/PayoutBatches/CreatePayoutBatchIntegrationTests.cs`

### Modified
- `backend/src/Makables.Core.Domain/Orders/Order.cs` — `AssignToPayoutBatch`
- `backend/src/Makables.Core.Domain/Orders/IOrderRepository.cs` + `backend/src/Makables.Infra.Database/Orders/OrderRepository.cs` — `GetPayoutEligibleUnscopedAsync`
- `backend/src/Makables.Core.Domain/Common/BusinessErrorMessage.cs` — 2 new codes
- observability/DI wiring — `IPayoutMetrics` registration + meter
- `frontend/src/lib/i18n/cs-CZ.ts` — 3 keys
- `frontend/src/lib/api-client/*` — bundle's final NSwag regen commit
- `docs/questions/open.md` — mark Q-0017 resolved (this ticket)

## Commits hint

1. `fix(T-0102a): Q-0017 data-fix migration — double-brace email subject placeholders` (leading).
2. `test(T-0102a): pin PayoutEligibility verdict table (red)`.
3. `feat(T-0102a): CreatePayoutBatch feature + domain claim method + repository query + metrics + controller + error codes + i18n`.
4. `test(T-0102a): handler + integration coverage`.

## Test plan reference

Inline above (Scope > Tests). No separate `docs/test-plans/T-0102a.md`.

## Status log

- 2026-06-12 `draft` by BA. Split from INDEX T-0102 (L) at bundle grooming: T-0102a = claim command (this ticket, M); T-0102b = fee invoices + CSV behind `IPayoutCsvFormatter`; T-0103/T-0104 unchanged. Consumes T-0101's `PayoutBatch` entity + `IPayoutBatchRepository` (`GetOpenBatchAsync`, `GetByNumberAsync`, exclusion-count columns) and T-0105's `Order.RefundedAmountMinor`.
- 2026-06-12 `draft → ready`. User locked Q1–Q5 at the payout-bundle deliberation (recorded §A); 12 PM-absorbed decisions in §C, including the Q-0017 data-fix assignment, the handler-written audit row (D rebuttal), the week guard, and the currency guard. No manual_steps. **Ready for dotnet-backend** — sequence: Q-0017 migration → red `PayoutEligibility` table → feature → tests.

## Definition of ready

- [x] User story linked (US-admin-0007) with AC traceability (AC-1/3/4 here; AC-1 remainder T-0102b; AC-2 T-0103)
- [x] Blocking design decisions locked (Q1–Q5) with rebutted alternatives
- [x] Dependencies on master or in-bundle (T-0101 entity, T-0105 `RefundedAmountMinor`, T-0007 generator)
- [x] Error codes + i18n keys enumerated
- [x] Test surface named (pure spec = red-first; ~10 unit, ~5 integration)
- [x] Security posture stated (admin audience, fail-closed session, unscoped reads admin-only, atomic money claim)
