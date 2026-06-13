# Payout-core bundle (Q-0017 + T-0101 + T-0102a + T-0102b + T-0104) — Reviewer preliminary verdict (draft)

> Written in PARALLEL with the dotnet-backend implementer per the parallel-reviewer rule (`docs/process/routing.md` §Sequencing). NOT the final verdict — a structural read of the 4 tickets + ADRs + precedents (T-0068a/b, refund-dispute bundle) BEFORE the diff completes, so the PR-open review can focus on the diff.
> Branch `feat/payout-core-bundle`. State at draft time: `e6640f9` grooming → `749a18b` Q-0017 data-fix migration (leading, correct) → `cc1ed3b` red commit pinning the 4 pure-logic surfaces. No implementation commits yet.
> Inputs: T-0101/T-0102a/T-0102b/T-0104 tickets; quality-gates.md; checklist.md; ADR 0003/0009/0013/0014/0019/0020/0023; T-0068a/b artifacts; refund-dispute-bundle final review (B-1 lesson: multi-step money mechanisms need REAL Postgres e2e, not mocked-mediator units; i18n-parity recurring finding at count 2/3 — this bundle is the third-strike tripwire).

## Bundle scope (one PR; T-0103 = separate PR #2)

Five-part landing in ONE PR on `feat/payout-core-bundle`:

1. **Q-0017** — leading data-fix migration `FixEmailSubjectPlaceholders` UPDATEing the 16 single-brace `email_template_translations.subject` rows (SeedOrderEmailTemplates ×4, ShippingPipelineBundle ×4, DeliveryCloseBundle ×2, OrderCleanupBundle ×6) to double-brace. Already committed at `749a18b` — leads the PR per all three tickets. `Down` restores single-brace.
2. **T-0101** — `PayoutBatch` aggregate (born `Processing`, immutable, set-once `AttachCsvBlobPath`), `PayoutBatchState` enum (Processing=1, Completed=2; NO Pending), `IPayoutBatchRepository` (Add/GetByIdUnscoped/GetOpenBatch/GetByNumber — no Update/Delete), `Order.PayoutBatchId` + `AssignToPayoutBatch`, `payout_batches` migration + `orders.payout_batch_id` FK + closes the T-0068a `invoices.payout_batch_id` FK TODO, partial unique open-batch index.
3. **T-0102a** — `CreatePayoutBatch` claim command on the admin host: `PayoutEligibility.Classify` pure spec, money aggregation (Σ MakerPayoutAmountMinor), exclusion surfacing (Q3 partially-refunded, Q5 NULL-bank), re-run guard, week guard, currency guard, handler-written audit row, `IPayoutMetrics`, controller, 2 new error codes + cs-CZ keys.
4. **T-0102b** — financial artifacts inside the same handler invocation: per-maker `InvoiceType.Fee` invoices (shared FV-CZ sequence), `IPayoutCsvFormatter` seam + `GenericPayoutCsvFormatter`, fee-invoice maker emails (T-0069 attachment pattern via outbox), admin CSV download endpoint, `ProvizniDokladDocument` PDF template, re-entrancy contract, 3 new error codes + cs-CZ keys.
5. **T-0104** — `RunWeeklyPayoutBatchFunction` (timer Monday 02:00 UTC + HTTP escape hatch), thin `ISender` dispatch + 4-branch response interpretation. No new endpoints on Web.* hosts, no NSwag.

**Money-aggregation + financial-document generation across an artifact pipeline that commits once but produces blobs/emails non-transactionally. This is the highest-risk bundle since the refund-dispute set.** All of T-0102a/b are `security_touching: true`.

## Patterns / ADRs the diff MUST honour

- **A.1 Layering.** `Core.Domain/Payouts/*` (PayoutBatch, PayoutBatchState, PayoutEligibility, IPayoutBatchRepository, IPayoutCsvFormatter, PayoutCsvBatch/Line) and `Core.Domain/Observability/IPayoutMetrics.cs` reference ONLY BCL — no EF, no MediatR, no QuestPDF, no Azure SDK. `IPayoutMetrics` is the pure interface; the `System.Diagnostics.Metrics` impl (`PayoutMetrics.cs`) lives in `Config/Observability`. `GenericPayoutCsvFormatter` is in AppServices (pure, zero DI). Grep `Core.Domain/Payouts/*` and `Core.Domain/Observability/*` for non-BCL usings at PR-open.
- **A.4 `BusinessResult` for expected failures.** `PayoutBatch.AttachCsvBlobPath` → `BusinessResult` (set-once: same value Success, different value `Failure(payoutBatch.csvPathAlreadySet)`). Handler failures (empty/week/currency/configMissing) → `BusinessResult.Failure(<code>)`. **Programmer-error invariants throw, not Failure:** `PayoutBatch.Create` arg validation throws `ArgumentException`; `GenericPayoutCsvFormatter` blank-bank throws `ArgumentException` (Q5 invariant already guaranteed upstream). No inline error strings — Gate 1 #5 + T5.
- **A.5 Pipeline behaviors.** Claim + batch insert + N order claims + Fee invoice rows + outbox rows + audit row commit in ONE UoW (ADR 0014). **No `SaveChangesAsync()` in `CreatePayoutBatch.Handler` or `PayoutArtifactService`.** Blob uploads + CSV upload are non-transactional side effects (acceptable per T-0068b precedent; rendering deterministic, uploads overwrite-safe).
- **A.11 Auditable.** `PayoutBatch : Auditable` (T4 check). Factory sets `CountryCode` (Order.Create precedent). Config has `ConfigureAuditable`.
- **A.18 / ADR 0003 Money.** `TotalAmountMinor BIGINT NOT NULL` + `Currency CHAR(3) NOT NULL`. All sums in `long` minor units. The ONLY decimal/double conversion is at the CSV formatter edge (`123456` minor → `1234.56`, invariant culture). Grep the migration for `decimal`/`numeric` — none expected. T6 check.
- **A.19 Soft delete.** `PayoutBatchConfiguration` must NOT set `HasQueryFilter` (the global filter applies). Partial unique indexes use `HasFilter(...)`.
- **A.20 Idempotency.** The artifact re-entrancy contract (T-0102b §C.4) is the idempotency surface — re-run resumes missing artifacts, never duplicates. See HIGH-3.
- **ADR 0009 Numbering.** Batch number via `IPayoutBatchNumberGenerator.For(countryCode, batchDate)` (T-0007, on master, confirmed pure). Uniqueness via the `(country_code, batch_number)` unique index, NOT a sequence row. Fee invoices allocate from the shared FV-CZ `NumberingSequence` FOR-UPDATE row via `IInvoiceNumberGenerator` (gap-free, interleaves with Customer invoices). **TZ-aware local-date derivation is the HANDLER's job** (`ToCountryLocalDate(clock.UtcNow, config.TimeZoneId)` → generator), per the T-0062/T-0068a amendment — ADR 0009's "tracked under T-0101" note needs the split-recording amendment paragraph (T-0101 docs scope).
- **ADR 0013 Scoping.** Admin host only. `[Authorize]` admin JWT audience on `PayoutBatchesController` (POST + CSV GET). Repository `*Unscoped*` naming; `GetPayoutEligibleUnscopedAsync` admin-only. A customer/maker JWT must not replay against the admin host (AC-10 / AC-9).
- **ADR 0014 UoW + admin audit.** Audit row written by the HANDLER via `IAdminAuditLogWriter` (not `IAdminAuditableCommand` — a create-command can't name `TargetId` at command time; D-rebuttal in T-0102a §C.7). Fail-closed session check FIRST (RefundOrder precedent — money is never attributed to "system").
- **ADR 0019/0020 Outbox + email chokepoint.** Fee-invoice maker emails go through the outbox; `EmailSendService` (interface `IEmailSendService.cs` confirmed present) is the only `IEmailProvider` consumer; attachment looked up at SEND time (T-0069 pattern), `PdfBlobPath` null at send → `Transient(InvoiceNotYetRendered)`. Function is a thin wrapper (ADR 0020): no business logic, schedule in config, `UseMonitor = true`, `AuthorizationLevel.Function` on the HTTP hatch.
- **ADR 0023 Observability.** `MakablesMeters.Payouts` (registered name, instrument-less) gets its first instruments in T-0102a. Singleton via `IMeterFactory`. Note the documented telemetry-before-commit caveat.

## Pre-flight risks — HIGH first

### HIGH-1 — Claim atomicity + concurrent-creation race (the Monday-timer + admin-click double-claim)

`GetOpenBatchAsync` is **read-then-write**: the timer (Monday 02:00 UTC) and an admin click — or two admins — can both pass the open-batch read-check and both proceed to claim, OR a same-week re-fire after completion. What serializes this MUST be a DB constraint, not the app-level read:

- **Partial unique index `ux_payout_batches_open_per_country` on `(country_code) WHERE state = 'Processing' AND is_active`** (T-0101 §C, AC-5). This turns the second concurrent commit into a deterministic 23505, which T-0102a's handler maps to "return the existing batch". **VERIFY at PR-open:** (a) the index exists in `PayoutBatchConfiguration` + the migration with EXACTLY this predicate; (b) the handler's `GetOpenBatchAsync` path AND the unique-violation path both resolve to the Silent-Success `AlreadyExisted` shape — i.e. the 23505 is caught/translated, not surfaced as a raw 500. If only the app-level read-check exists (no partial index), this is a **HARD BLOCK**: double-claim splits orders across 2 batches or claims an order twice → `Order.AssignToPayoutBatch` throws `InvalidOperationException` mid-transaction (set-once), aborting the second run — survivable but the FIRST run may already have orphaned Fee invoices if artifact generation ran. The week-guard (`GetByNumberAsync` pre-check) covers the after-completion same-week case but ALSO needs the unique `(country_code, batch_number)` index as backstop (AC-5).
- **Integration test demand:** AC-2 / AC-5 / T-0102a-IntegrationTest-2 must prove the second concurrent POST yields exactly ONE batch row. A serial re-run test is necessary but NOT sufficient — request a test that interleaves two creations against real Postgres (or at minimum asserts the partial-index 23505 is caught). The refund-dispute B-1 lesson applies: this race is exactly the kind of multi-step money mechanism that a mocked-mediator unit cannot exercise.

### HIGH-2 — Money reconciliation end-to-end (batch total == claims == CSV == fee invoices)

The sum the handler writes is the amount the operator wires from the company bank account. Four independent representations of the same money must agree:

1. `PayoutBatch.TotalAmountMinor` == Σ `Order.MakerPayoutAmountMinor` over claimed orders.
2. Per-maker Fee invoice amount == Σ that maker's `Order.PlatformFeeAmountMinor` over the maker's claimed orders.
3. Per-maker CSV line `amount` == Σ that maker's `Order.MakerPayoutAmountMinor` (the payout, NOT the fee) — **note the asymmetry: the CSV pays the maker the payout amount; the Fee invoice charges the maker the platform fee.** Verify the formatter sums the right column. A copy-paste of `PlatformFeeAmountMinor` into the CSV line would silently mis-pay every maker.
4. The pricing invariant at `Order.cs:449-453` (verified present — two-part: `Total == Product + Shipping` AND `MakerPayout + PlatformFee == Product + Shipping`) guarantees per-order integrity, so the batch never needs to recompute splits.

**VERIFY:** all aggregation in `long` minor units; zero `decimal`/`double` except the CSV display edge (`amount` → `0.00`); the currency guard (`payoutBatch.currencyMismatch`, LogCritical) fires before summing mixed currencies; `TotalAmountMinor > 0` (Create throws on `<= 0`). **Integration demand:** AC-1 (T-0102a) + AC-1/AC-3 (T-0102b) must assert the cross-foot — batch total, each Fee invoice amount, and each CSV line amount reconcile against the seeded orders in ONE real-Postgres e2e. This is the B-1 leg: a mocked-mediator handler test cannot prove the fee-sum query, the CSV golden, and the batch total all agree against actual rows.

### HIGH-3 — Artifact re-entrancy: committed claim + failed artifacts must NOT burn FV-CZ sequence numbers

The claim commits (UoW), THEN artifact generation runs and may fail (QuestPDF throws, blob upload fails, email enqueue fails). Per T-0102b §C.3 the handler catches, logs **Critical**, sets `ArtifactsComplete = false`, and STILL returns Success so the claim + completed-maker artifacts commit (a throw would roll back the claim — strictly worse, T-0102b Alternatives "Throw on artifact failure"). The re-run path (T-0102a returns the existing open batch) resumes via `IPayoutArtifactService` (§C.4): makers without a Fee invoice → full unit; Fee invoices with `PdfBlobPath == null` → re-render + attach + enqueue; `CsvBlobPath == null` → format + upload + attach. **The CRITICAL guard:** Fee invoices use the shared FV-CZ sequence. A retry that re-issues invoices for an already-invoiced maker BURNS sequence numbers (gaps — illegal in CZ per ADR 0009) or duplicates. **VERIFY at PR-open:** the artifact service guards issuance with an exists-check per (maker, batch) — `IInvoiceRepository.GetByPayoutBatchIdAsync` (T-0102b new method) → skip makers that already have a Fee row. AC-7 ("makers 2+3 get invoices, maker 1 untouched — no duplicate invoice, number, blob, or email") and AC-8 (`Received(0)` on a fully-artifacted re-run) are the guards. **Integration demand:** a re-run-after-simulated-artifact-failure test against real Postgres asserting (a) no duplicate FV-CZ numbers, (b) a single CSV upload, (c) the FV-CZ `last_used_value` advanced by exactly the number of NEW invoices. If the resume re-issues, that is a HARD BLOCK — gap-free is a legal invariant.

### HIGH-4 — Eligibility exclusion-count snapshot consistency (one query, not a racy second count)

The claimed set + the three exclusion counts (`ExcludedPartiallyRefundedOrderCount`, `ExcludedNoBankAccountOrderCount`, `ExcludedNoBankAccountMakerCount`) are persisted as immutable columns on `payout_batches` (T-0102a §C.10) AND echoed in the response + audit `afterJson`. They MUST come from ONE `GetPayoutEligibleUnscopedAsync` snapshot partitioned by `PayoutEligibility.Classify` — never a second `COUNT` query that could race against a concurrent state change. **VERIFY:** the handler loads candidates once, classifies in memory, and derives all counts + the eligible claim-set from that single materialized list. The distinct-maker count for the no-bank exclusion must be `.Distinct()` on maker id (AC-4). If the implementer issues a separate count query, request changes — the immutability promise (Q4) only holds if the snapshot is atomic with the claim.

### HIGH-5 — Q-0017 fix precision (hits exactly 16 rows, idempotent, leaves T-0105/T-0106 double-brace rows intact)

Committed at `749a18b` as `20260613060609_FixEmailSubjectPlaceholders`. The body confirms an idempotent UPDATE (single-brace present AND not already double-brace) with `Down` restoring single-brace and a design-time DbContext factory. **VERIFY at PR-open:** (a) the UPDATE predicate (`subject LIKE '%{order_number}%' AND subject NOT LIKE '%{{order_number}}%'` or equivalent enumerated-id form) hits EXACTLY the 16 rows and does not touch the correct T-0105/T-0106 double-brace seeds (those came in via quadruple-brace source → already `{{order_number}}` stored, so the `NOT LIKE %{{...}}%` term excludes them — confirm the term is present); (b) placeholders OTHER than `order_number` that were also single-braced in those subjects are covered (the open.md entry and tickets name `order_number` specifically, but if any subject also carries `{order_url}` / `{customer_name}` single-braced, the REPLACE-one-token form misses them — VERIFY the migration enumerates every affected placeholder, or uses a per-row id-targeted UPDATE that rewrites the whole subject). The AC (T-0101 AC-6 / T-0102a AC-11 / T-0102b AC-10) demands ZERO single-brace `{x}` rows post-migration — a generic single-brace scan, not just `order_number`. (b) is the most likely silent gap.

### HIGH-6 — B-1 lesson: CreatePayoutBatch + artifacts need REAL Postgres e2e, not mocked-mediator units

Directly from the refund-dispute B-1 blocker (AC-9 e2e leg was missing; only a mocked-`ISender` unit existed). The payout claim + artifact pipeline is a MORE complex multi-step money mechanism: claim → Fee invoices issued from a FOR-UPDATE sequence → PDF blobs → CSV blob → outbox rows → audit row, all spanning one UoW commit plus non-transactional side effects. **Mocked-mediator/NSubstitute units cannot exercise:** the real FV-CZ FOR-UPDATE lock + gap-free numbering, the partial-unique open-batch race, the cross-foot reconciliation against real rows, the re-entrancy resume against real invoice rows, the audit row riding the committed UoW. **DEMAND (will be a hard block at PR-open if absent):** the integration suites named in the tickets must actually land — `CreatePayoutBatchIntegrationTests` (~4 + migration check) and `PayoutBatchArtifactsIntegrationTests` (~3, incl. the re-run-after-failure leg and the admin CSV golden-content stream). A bundle that ships only `CreatePayoutBatchHandlerTests` (mocked) + `PayoutArtifactServiceTests` (mocked) repeats B-1 and will be rejected.

### MEDIUM-1 — `Order.AssignToPayoutBatch` contract contradiction between T-0101 and T-0102a

T-0101 §Domain + AC-3 specifies `AssignToPayoutBatch` **throws** `InvalidOperationException` when already claimed (set-once; "claiming twice throws"). T-0102a §Domain (`Order.cs` bullet, line 79) specifies it returns `BusinessResult` and **refuses with `OrderInvalidTransition`** unless `State == Delivered && PayoutBatchId == null`. These are two different signatures AND two different invariant sets (T-0101 = pure set-once, no state check, per Option E; T-0102a = set-once PLUS state assertion). **The red commit `cc1ed3b` pins the T-0101 shape** (`OrderAssignToPayoutBatchTests`: "set-once only (no state assertion per T-0101 Option E); double-claim throws InvalidOperationException; blank throws ArgumentException"). So the tests follow T-0101. The implementer MUST pick one and the diff must match the pinned tests. **VERIFY:** the shipped `AssignToPayoutBatch` throws (T-0101 shape, matching the red tests) and the claim predicate (state==Delivered) lives ONLY in `PayoutEligibility.Classify` / the repository query (single source of eligibility truth, Option E). If the diff ships the T-0102a `BusinessResult`+state-check shape, it contradicts the red tests → either the tests fail (Gate 5 red→green breaks) or the tests were quietly rewritten (after-the-fact = Gate 5 HARD FAIL). Flag this to PM/implementer NOW so it is resolved before green; the T-0101 ticket is the authority and the tests already match it.

### MEDIUM-2 — Generator year-boundary quirk (observed, locked-deferred — do NOT let the implementer "fix" it here)

`PayoutBatchNumberGenerator.For` (verified at `Numbering/PayoutBatchNumberGenerator.cs:21-22`) uses `batchDate.Year` with `ISOWeek.GetWeekOfYear` — a Jan 1–3 batch in ISO week 52/53 of the PRIOR year gets the new-year label (`VYP-CZ-2027-W53`). Cosmetic (uniqueness holds via the index). T-0102a Risk notes flag it as a micro-follow-up for PM, NOT a fix in this bundle. **VERIFY the diff does not touch the generator** — it is shared infrastructure on master; a "drive-by fix" here is out of scope and would need its own red test. If the diff modifies `PayoutBatchNumberGenerator.cs`, request changes (scope creep) and route to a follow-up ticket.

### MEDIUM-3 — Role-doc drift (T-0101 must fix in the same PR)

`docs/architecture/roles/payout-batch.md` (verified) currently says `Status (Pending | Processing | Completed)`, `ProcessedAt`, no `MakerCount`, no Q3/Q5 exclusion invariants, and an implementation pointer to a non-existent `Core.AppServices/Services/PayoutService.cs`. Per ADR 0015 RDD-parity and T-0101 §Docs, this PR MUST: drop `Pending` (lock A.4 → `Processing | Completed`), rename `Status`→`State` and `ProcessedAt`→`CompletedAt`/`CompletedBy`, add `MakerCount` + the three exclusion-count fields, add Q3/Q5 exclusion invariants, and fix the implementation pointer to `Core.Domain/Payouts/PayoutBatch.cs`. The collaborators list (Order/Maker/Invoice/Numbering) is fine but should note the CSV formatter seam. If the role doc is unchanged, Gate 7 + RDD-parity FAIL.

### MEDIUM-4 — RDD parity for the new domain types (ADR 0015)

New aggregates/services/interfaces in the diff each need a role file (or an update to an existing one) in the same PR: `PayoutBatch` (update existing `payout-batch.md` — MEDIUM-3); `PayoutEligibility` (pure spec — a role-file or a documented exemption like the `ManualOrderTransitionPolicy` precedent from refund-dispute F-2); `IPayoutCsvFormatter` (adapter/seam interface — extension-point note); `IPayoutArtifactService` (domain service); `IPayoutMetrics` (observability port). The refund-dispute bundle was dinged (F-2) for missing role files on `Dispute`/`IDisputeRepository`/`ManualOrderTransitionPolicy` — do NOT repeat. **Handler collaborator count (ADR 0015 ~5):** `CreatePayoutBatch.Handler` lists 12 constructor deps (orders, payoutBatches, numberGenerator, countries, defaultCountry, auditWriter, session, idGenerator, clock, metrics, logger, + artifactService in T-0102b = 12). This exceeds ~5 and matches the refund-dispute observation (ResolveDispute=10, RefundOrder=9) — the enrichment/audit/metrics ambient-dep cluster recurs. NOT a block (ambient deps clock/logger/options/metrics are arguably exempt), but FLAG to Architect as the **second formal instance** of the "email/audit-emitting handler exceeds ADR 0015 collaborator budget" recurring observation (refund-dispute raised count 1). If it lands a third time, harvest.

## AC traceability matrix (~34 ACs)

| Ticket | AC | Proof surface (verify at PR-open) |
|---|---|---|
| **T-0101 (8)** | AC-1 Create born Processing + arg guards | `PayoutBatchTests` (red `cc1ed3b`) |
| | AC-2 AttachCsvBlobPath set-once idempotent | `PayoutBatchTests` (red) |
| | AC-3 Order.AssignToPayoutBatch set-once throws | `OrderAssignToPayoutBatchTests` (red) — see MEDIUM-1 |
| | AC-4 both migrations apply clean + idempotent re-run; FKs + indexes exist | integration migration check (rides T-0102a) |
| | AC-5 unique `(cc,batch_number)` + partial open-batch index reject | integration (rides T-0102a) — HIGH-1 |
| | AC-6 Q-0017: zero single-brace subjects, 16 rows double-brace, Down restores | migration assertion — HIGH-5 |
| | AC-7 GetOpenBatchAsync returns the Processing batch or null | integration (rides T-0102a) |
| | AC-8 build clean, ~8 unit red-first, consistency exit 0, no NSwag diff, docs committed | Gate 5/9 + MEDIUM-3 |
| **T-0102a (12)** | AC-1 3 orders/2 makers → batch Processing, total=ΣMakerPayout, counts, orders claimed | `CreatePayoutBatchIntegrationTests`#1 — HIGH-2/HIGH-6 |
| | AC-2 re-run open batch → AlreadyExisted, no 2nd row/audit | integration#2 — HIGH-1 |
| | AC-3 partially-refunded excluded + surfaced 3 ways | `PayoutEligibilityTests` (red) + integration#3 |
| | AC-4 NULL-bank maker excluded + distinct-maker count surfaced | `PayoutEligibilityTests` (red) + integration#3 — HIGH-4 |
| | AC-5 Disputed/Shipped/Completed/batched not claimed | `PayoutEligibilityTests` (red) |
| | AC-6 empty → payoutBatch.empty, no row, warning, batch_runs{empty} | handler test + integration#4 |
| | AC-7 same-week-after-complete → payoutBatch.weekAlreadyProcessed | handler test (week guard) |
| | AC-8 claim+claims+audit in ONE tx, no SaveChangesAsync, mid-fail persists nothing | integration (forced-fail) — A.5 |
| | AC-9 Sunday 23:30 UTC → Monday local ISO week | handler test (TZ pin) |
| | AC-10 anon/customer/maker JWT → 401/403; empty session fail-closed | integration audience + handler |
| | AC-11 Q-0017 zero single-brace | migration assertion — HIGH-5 |
| | AC-12 build clean, ~10 unit + ~5 integration, consistency 0, cs-CZ keys, NSwag in final regen | Gate 5/6/9 |
| **T-0102b (11)** | AC-1 2 Fee invoices, PayoutBatchId set, OrderId null, amounts=ΣPlatformFee, zero VAT, DUZP, FV-CZ gap-free | `PayoutBatchArtifactsIntegrationTests`#1 — HIGH-2/HIGH-3 |
| | AC-2 PDF at `{cc}/payouts/{batchId}/{num}.pdf`, line items, balanced | integration#1 + renderer test |
| | AC-3 CSV at `payouts/{cc}/{num}.csv`, BOM/CRLF/semicolon, golden, CsvBlobPath set-once | `GenericPayoutCsvFormatterTests` (red `cc1ed3b`) + integration#1 — HIGH-2 |
| | AC-4 2 outbox rows, payload fields, email carries PDF + double-brace subject | integration + EmailSendService test |
| | AC-5 PdfBlobPath null at send → Transient(InvoiceNotYetRendered) | EmailSendService test |
| | AC-6 render throws maker 2/3 → Success, ArtifactsComplete=false, Critical log, maker-1 committed, no CSV | `PayoutArtifactServiceTests` — HIGH-3 |
| | AC-7 re-run completes makers 2+3, maker 1 untouched (no dup invoice/number/blob/email) | integration re-run — HIGH-3 |
| | AC-8 fully-artifacted re-run → Received(0) on renderer/formatter/blob | `PayoutArtifactServiceTests` — HIGH-3 |
| | AC-9 admin CSV stream 200 text/csv; anon 401; unknown 404; null CsvBlobPath 409 | integration endpoint + audience |
| | AC-10 Q-0017 zero single-brace | migration assertion — HIGH-5 |
| | AC-11 build clean, formatter red-first, ~8 unit + ~3 integration, consistency 0, NSwag regen, cs-CZ keys | Gate 5/6/9 |
| **T-0104 (7)** | AC-1 schedule from %RunWeeklyPayoutBatch:Schedule% default Mon 02:00 UTC, UseMonitor, one Send/tick, no logic | `RunWeeklyPayoutBatchFunctionTests` |
| | AC-2 created → Information with batch/counts/totals/exclusions, no Warn/Error | Function test#1 |
| | AC-3 AlreadyExisted → Information, no 2nd Send, no 2nd row | Function test#2 |
| | AC-4 empty → Information (quiet week), no throw | Function test#3 |
| | AC-5 other failure → Error with code, no throw | Function test#4 |
| | AC-6 HTTP hatch → 200/422/500 mapping, AuthorizationLevel.Function | Function test (HTTP path) |
| | AC-7 build clean, ~4 tests, schedule key in local.settings + env-vars, consistency 0, no NSwag | Gate 7/9 |

**Total: 8 + 12 + 11 + 7 = 38 ACs** (the brief's ~34 estimate; the +4 are T-0102b's artifact-failure/re-entrancy ACs). Every AC has a named proof surface; the HIGH-tagged ones demand REAL Postgres e2e, not mocked units (HIGH-6).

## Gate 5 expectations (TDD red-first — HARD-FAIL surfaces)

Per quality-gates Gate 5 + `tdd-policy.md`, pure logic in `must-cover-tests.md` categories MUST show test-before-impl. **Already satisfied at draft time:** red commit `cc1ed3b` pins ALL FOUR pure-logic surfaces BEFORE any implementation commit:

1. **Batch invariants** — `PayoutBatchTests`: Create (born Processing, blank/length/count guards, negative-exclusion guards) + AttachCsvBlobPath set-once. ✓ red.
2. **Set-once claim** — `OrderAssignToPayoutBatchTests`: set-once only, double-claim throws `InvalidOperationException`, blank throws `ArgumentException`. ✓ red (T-0101 shape — see MEDIUM-1).
3. **Eligibility table** — `PayoutEligibilityTests`: `Classify` verdict matrix with locked precedence NotClaimable → ExcludedPartiallyRefunded → ExcludedNoBankAccount → Eligible. ✓ red.
4. **CSV golden-file** — `GenericPayoutCsvFormatterTests`: golden header + CRLF + semicolons, minor→`0.00`, VS digit extraction, 140-char truncation, row order, blank-bank throws. ✓ red.

**VERIFY at PR-open:** (a) `git log --reverse origin/master..HEAD` keeps `cc1ed3b` (red) strictly BEFORE every `feat(...)` commit touching `PayoutBatch.cs` / `Order.AssignToPayoutBatch` / `PayoutEligibility.cs` / `GenericPayoutCsvFormatter.cs`; (b) the red tests were NOT silently rewritten after impl (diff the test files between `cc1ed3b` and HEAD — a substantive change to assertions post-impl = after-the-fact = **Gate 5 HARD FAIL**); (c) `must-cover-tests.md` grows rows for the new set-once invariants (`PayoutBatch.CsvBlobPath`, `Order.PayoutBatchId`) per the file's "add a row when a new set-once property lands" mandate — the T-0068a precedent hard-failed without this. Handler/integration/Function/renderer/artifact-service tests may land alongside impl (not pure logic). The Q-0017 migration (`749a18b`) is data-only, not pure logic — its position before the red commit is fine.

## Gate 9 expectations (mechanical + i18n parity third-strike)

`check-consistency.mjs` must exit 0 with only baseline-or-accounted NEW violations:

- **T1 one-file feature** — `CreatePayoutBatch.cs` will fire the static-class-wrapper heuristic (same false-positive class as the 50+ existing T1 rows + the 6 in refund-dispute). Expect +1 T1. Account it explicitly.
- **T4 Auditable** — `PayoutBatch : Auditable` (not BaseEntity).
- **T5 BusinessErrorMessage via constant** — every new code referenced as `BusinessErrorMessage.X`, never inline. New codes: `PayoutBatchCsvPathAlreadySet` (T-0101), `PayoutBatchWeekAlreadyProcessed` + `PayoutBatchCurrencyMismatch` (T-0102a), `PayoutBatchNotFound` + `PayoutBatchCsvNotReady` + `PayoutBatchCsvBlobPathAlreadySet` (T-0102b). Note T-0102b §C.6 uses `payoutBatch.csvBlobPathAlreadySet` while T-0101 uses `payoutBatch.csvPathAlreadySet` — **two near-identical set-once codes; VERIFY they are intentional/distinct or consolidate** (likely the same concept named twice across the split — request one canonical code to avoid a dead duplicate). Watch for a T5 BlockedCode-style indirection false-positive if any `Error.Conflict("x", someVar)` appears.
- **T6 money column** — `total_amount_minor BIGINT NOT NULL`; no decimal columns.
- **Gate 9 / NSwag** — T-0102a + T-0102b add admin endpoints (`POST /api/v1/payout-batches`, `GET /api/v1/admin/payout-batches/{id}/csv`). One regen in the bundle's FINAL regen commit covering both. `admin-api.v1.ts` already exists (created in refund-dispute, MEDIUM-3 resolved). T-0104 adds NO public endpoint → no NSwag. Verify `.spec-hashes.json` updated, no bare `export class Response`, pre-commit manual-edit hook covers the regen.

**New T1 count projection:** +1 (`CreatePayoutBatch.cs`). T-0102b's `IPayoutArtifactService`/`GenericPayoutCsvFormatter` live under `Features/Payouts/` but are NOT one-file `<Entity>/<UseCase>.cs` features, so they should NOT fire T1 (verify the heuristic — if it flags the service files, account them). Projected baseline delta ≈ +1 to +3; every entry must be a verified false positive (account each, refund-dispute precedent).

**i18n parity — THIRD-STRIKE TRIPWIRE (the headline Gate 9 risk).** The recurring finding "ticket claims i18n parity / catalog disagrees" is at **count 2/3** (refund-dispute INFO-1: checkout HIGH-5 + order-dashboards MEDIUM-3; the refund-dispute final review confirmed it did NOT fire — all 13 codes had keys, stayed 2/3). This bundle adds **5–6 new `BusinessErrorMessage` codes** (T-0102a ×2 + T-0102b ×3, plus T-0102a also ships the cs-CZ key for the pre-existing `PayoutBatchEmpty`, and `country.configMissing` if not already keyed). Every new code MUST have a parallel `cs-CZ.ts` key in the SAME PR, and no key may exist without a code. **If the final diff ships ANY new code without its cs-CZ key (or a key without a code), that is hit #3 → append a `recurring-findings.md` row + ping Architect** (proposed codification: a mechanical `BusinessErrorMessage` ↔ `cs-CZ.ts` parity check in `check-consistency.mjs`). Pre-verify all 5–6 codes ↔ keys at PR-open; this is the single most likely harvest trigger. Note the i18n file is the single `frontend/src/lib/i18n/cs-CZ.ts` (NOT a `cs-CZ/*` dir — refund-dispute LOW-1).

## Bundle Definition of Ready (assessment)

- **T-0101** DoR all checked; entity-slice precedent (T-0068a) clean; deps on master (T-0007 generator ✓, T-0060 Order ✓, T-0068a invoices column ✓). **READY.**
- **T-0102a** DoR all checked; deps T-0101 (in-bundle) + T-0105 `RefundedAmountMinor` (✓ verified at `Order.cs:919`) + T-0007 generator. Q1–Q5 locked. **READY.**
- **T-0102b** DoR checked; deps T-0101/T-0102a (in-bundle) + T-0068b (`IInvoicePdfRenderer`, FV-CZ generator ✓) + T-0069 (attachment pattern). CSV spec frozen §C.5. **READY.**
- **T-0104** DoR has UNCHECKED boxes (depends on T-0102 "merged or earlier in the same branch"; field-name verification "at implementation time"). Since the whole bundle ships in ONE PR/branch, the dependency is satisfied intra-branch — acceptable, but the implementer MUST verify the response field names (`BatchNumber`, `AlreadyExisted`, `OrderCount`, `MakerCount`, `TotalAmountMinor`/`Currency`, both exclusion counts) match T-0102a's actual `CreatePayoutBatchResponse` before wiring the Function logs. **READY-with-verification.**

Bundle DoR: **PASS** — all four ready; T-0104's open boxes close intra-branch.

## Open items the implementer should confirm in the PR description

1. **MEDIUM-1 — `Order.AssignToPayoutBatch` shape:** confirm the diff ships the T-0101 throw-on-double-claim shape (matching red `cc1ed3b`), with the state/eligibility check living ONLY in `PayoutEligibility.Classify` + the repo query (Option E). Reconcile T-0102a §Domain's contradictory `BusinessResult`+state wording — it should defer to T-0101.
2. **HIGH-1 — open-batch serialization:** confirm `ux_payout_batches_open_per_country` partial unique index exists with the exact `WHERE state='Processing' AND is_active` predicate, AND that the handler catches the 23505 → Silent-Success `AlreadyExisted` (not a raw 500).
3. **HIGH-3 — re-entrancy exists-check:** confirm `GetByPayoutBatchIdAsync` gates Fee-invoice issuance per (maker, batch) so a resume never re-allocates an FV-CZ number.
4. **HIGH-5 — Q-0017 placeholder coverage:** confirm the migration's `NOT LIKE %{{order_number}}%` (or id-targeted) guard protects T-0105/T-0106 seeds AND that EVERY single-braced placeholder in the 16 subjects is covered, not just `{order_number}` (the AC scans for ANY single-brace `{x}`).
5. **Gate 9 — duplicate set-once code:** confirm `payoutBatch.csvPathAlreadySet` (T-0101) vs `payoutBatch.csvBlobPathAlreadySet` (T-0102b) — consolidate to one canonical code or justify both.
6. **MEDIUM-3/4 — docs:** confirm `payout-batch.md` role doc updated (drop Pending, State/CompletedAt, MakerCount, exclusion invariants, fix impl pointer) + ADR 0009 amendment paragraph + Q-0017 → resolved in open.md + INDEX refresh + role files / documented exemptions for `PayoutEligibility`/`IPayoutCsvFormatter`/`IPayoutArtifactService`/`IPayoutMetrics`.
7. **HIGH-6 — integration suites:** confirm both `CreatePayoutBatchIntegrationTests` and `PayoutBatchArtifactsIntegrationTests` land with the reconciliation cross-foot + the re-run-after-failure leg against real Postgres (the B-1 guard).
8. **i18n parity:** confirm all 5–6 new codes ↔ cs-CZ keys present in the same PR (third-strike tripwire).

## Preliminary verdict

**STRUCTURALLY_SOUND_PENDING_DIFF — with six HIGH pre-flight tripwires armed.**

The four tickets are exhaustive, the locks (Q1–Q5) have a clean paper trail, the precedents (T-0068a/b entity slice + invoice numbering, refund-dispute money-movement) are honoured, and the ADRs (0003/0009/0013/0014/0019/0020/0023) line up. The red commit `cc1ed3b` already pins all four pure-logic surfaces before implementation — Gate 5 discipline is on track. The Q-0017 leading migration is in place.

This bundle does NOT get an automatic approval at PR-open. It is the highest-risk landing since refund-dispute, and the refund-dispute review HARD-BLOCKED on exactly the failure mode most likely here (B-1: a multi-step money mechanism tested only with mocked mediator). The six HIGH tripwires — open-batch race serialization (HIGH-1), money cross-foot reconciliation (HIGH-2), FV-CZ re-entrancy gap-free guard (HIGH-3), snapshot-consistent exclusion counts (HIGH-4), Q-0017 placeholder coverage (HIGH-5), and real-Postgres e2e for claim+artifacts (HIGH-6) — are each a candidate HARD BLOCK if the diff does not demonstrably resolve them. The i18n-parity third-strike is the most likely harvest trigger.

Routing at PR-open: **SecOps mandatory** (T-0102a/b security_touching — money aggregation, financial documents, admin CSV download, bank file). **Optimizer mandatory** (hot path: `CreatePayoutBatch.Handler` touches Order + Maker + PayoutBatch + Invoice + outbox in a multi-step pipeline; the eligibility query `Include(o => o.Maker)` is an N+1 candidate — verify the single materialized snapshot from HIGH-4). **Architect ping** if the handler-collaborator-count observation lands a third time, and on the i18n-parity third strike.

Reviewer is ready for PR-open.
