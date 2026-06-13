# Payout-core bundle (Q-0017 + T-0101 + T-0102a + T-0102b + T-0104) — Final review

> Branch `feat/payout-core-bundle`, 9 commits (`e6640f9` grooming → `fe167f9` NSwag). Final PR-open verdict following the preliminary draft (`payout-core-bundle-draft.md`, 6 HIGH tripwires armed). Real-Postgres verification re-run completed.

## Verdict

**REQUEST CHANGES** — 2 BLOCKERs (1 code, 1 i18n/harvest) + 3 required folds. The money mechanism itself is correct (cross-foot reconciles, FV-CZ re-entrancy is gap-free, Q-0017 is precise, Gate 5 red-first holds), but the **concurrent open-batch race resolves to a raw 500 instead of the promised Silent-Success** (HIGH-1 unmet), and one new error code ships **without its cs-CZ key** (third-strike i18n-parity hit).

## Tripwire dispositions

| # | Tripwire | Disposition |
|---|---|---|
| HIGH-1 | Open-batch race serialization | **CONFIRMED DEFECT (BLOCKER).** Partial unique index `ux_payout_batches_open_per_country WHERE state='Processing' AND is_active` exists in config + migration. BUT it is **not registered** in `UniqueConstraintTranslator.Mappings` and carries no "intentionally unmapped" comment. On a concurrent second `CreatePayoutBatch` (Monday timer + admin click, or two admins), both pass the `GetOpenBatchAsync` null-check, the loser's `SaveChangesAsync` throws `UniqueConstraintViolationException`, the translator returns `null`, and `UnitOfWorkPipelineBehavior` **rethrows → raw 500**. Money integrity is NOT compromised (the loser's entire UoW — batch + claims + fee invoices — rolls back atomically; no split/double-claim, no orphaned invoices), so this is a degraded-UX/observability defect, not a corruption risk. But the design intent the draft armed ("23505 → AlreadyExisted Silent-Success") is **unimplemented and untested**. The `Re_run_returns_existing_batch_with_no_second_row` integration test is **sequential** (awaits `first` before `second`), so it exercises the app read-path, NOT the concurrent commit race. **Fix:** register `ux_payout_batches_country_batch_number` → `PayoutBatchWeekAlreadyProcessed` and `ux_payout_batches_open_per_country` → a re-read-then-return-existing translation (or, minimally, an explicit intentionally-unmapped comment justifying that a 500 is acceptable for the concurrent open-batch loser, given the T-0104 timer swallows it as an Error log). Either way, the chosen disposition must be documented and a comment added; a money command silently 500-ing under its single most-likely race is not signable as-is. |
| HIGH-2 | Money cross-foot (batch == claims == CSV == fee invoices) | **PASS.** `batch.TotalAmountMinor = Σ MakerPayoutAmountMinor` (handler:236). CSV line `AmountMinor = Σ MakerPayoutAmountMinor` per maker (artifact-service:216). Fee invoice `amountWithVatMinor = Σ PlatformFeeAmountMinor` per maker (artifact-service:123,158-161). **The asymmetry is correct — CSV pays the payout, the Fee invoice charges the platform fee — no swap.** All aggregation in `long`; the only decimal conversion is `GenericPayoutCsvFormatter.FormatAmount` (`minor/100m → "0.00"`, invariant culture) at the display edge. Currency-homogeneity guard fires before summing (handler:221, LogCritical). Integration test #1 asserts the batch total; the per-maker fee math is pinned in `PayoutArtifactServiceTests` (15000 / 7000). Minor gap: integration test does not assert each CSV line amount nor each fee-invoice amount against seeded rows (only batch total + CSV-contains-bank). Acceptable — the unit covers the per-maker split. |
| HIGH-3 | FV-CZ re-entrancy (no gap/duplicate) | **PASS.** `PayoutArtifactService` loads `existingFeeInvoices = invoices.GetByPayoutBatchIdAsync(batch.Id)` into `feeInvoiceByMaker` and skips already-invoiced makers (artifact-service:94-95,127); PDF re-render gated on `PdfBlobPath == null` (172); CSV gated on `CsvBlobPath == null` (223). `NumberingSequenceAllocator` Local-set fix is correct — multiple in-UoW Fee allocations chain off the same tracked `NumberingSequence` instance (deviation 4). Unit AC-8 no-op proven (`DidNotReceive` renderer/blob/AddAsync). Integration re-run asserts Fee-invoice count stays 1. Gap: no explicit multi-maker PARTIAL-resume test asserting "exactly N NEW FV numbers" (AC-7 strongest leg) — covered structurally, not by a dedicated assertion. Test-thoroughness note, not a code defect. |
| HIGH-4 | Exclusion-count snapshot consistency | **PASS.** Handler loads candidates ONCE (`GetPayoutEligibleUnscopedAsync`, handler:167), classifies in memory via `PayoutEligibility.Classify`, derives all three exclusion counts + the eligible set from that single materialized list (handler:169-200). No second COUNT query. Distinct-maker count via `HashSet<string>(Ordinal)` (handler:172,200). |
| HIGH-5 | Q-0017 precision | **PASS.** All 16 affected subjects (SeedOrderEmailTemplates ×4, ShippingPipelineBundle ×4, DeliveryCloseBundle ×2, OrderCleanupBundle ×6 — verified by grep) carry ONLY `{order_number}` as their placeholder; none carries `{order_url}`/`{customer_name}`/etc. The migration `REPLACE(subject,'{order_number}','{{order_number}}') WHERE subject LIKE '%{order_number}%' AND subject NOT LIKE '%{{order_number}}%'` hits exactly those rows, is idempotent, and the `NOT LIKE %{{order_number}}%` guard protects the already-double-brace T-0105/T-0106 payout seed (`{{batch_number}}`). The draft's "generic single-brace scan" concern is moot — there are no other single-brace placeholders. Integration test asserts 0 single-brace + ≥16 double-brace post-migration. `Down` restores single-brace. |
| HIGH-6 | Real-Postgres e2e (B-1 lesson) | **PASS.** `CreatePayoutBatchIntegrationTests` runs against real Postgres (`PostgresHarness`), the REAL QuestPDF renderer, and an in-memory blob. Covers claim e2e (batch row + claim links + 2 Fee invoices with PDFs + CSV blob + 2 outbox rows + 1 audit row), re-run idempotency, Q3/Q5 exclusions, empty run (no row, 409), Q-0017 subject fix, and admin CSV download + anon-401/unknown-404. Not a mocked-mediator repeat of B-1. |
| MEDIUM-1 | `Order.AssignToPayoutBatch` contract | **RESOLVED.** Ships the **T-0101 throw shape** (set-once ONLY, no state assertion; double-claim → `InvalidOperationException`, blank → `ArgumentException`; Order.cs:1180-1190) matching red `cc1ed3b`. The state/eligibility predicate lives ONLY in `PayoutEligibility.Classify` + the repo query (Option E). T-0102a's contradicting `BusinessResult`+state prose did NOT leak into code. |
| Deviation 6 | Duplicate set-once CSV code | **RESOLVED to one.** Only `PayoutBatchCsvPathAlreadySet = "payoutBatch.csvPathAlreadySet"` exists; `csvBlobPathAlreadySet` did not ship. |

## BLOCKERs

1. **HIGH-1 — concurrent open-batch race 500s.** `ux_payout_batches_open_per_country` + `ux_payout_batches_country_batch_number` are unregistered in `UniqueConstraintTranslator` and uncommented. The concurrent loser rethrows a raw 500 instead of the promised `AlreadyExisted` Silent-Success. Register both (open → re-read/return-existing or week-conflict; country_batch_number → `PayoutBatchWeekAlreadyProcessed`) OR add an explicit intentionally-unmapped justification comment, and add a concurrency test (or document why the sequential test suffices). `backend/src/Makables.Infra.Database/UniqueConstraintTranslator.cs`, `backend/src/Makables.Core.AppServices/Features/Payouts/CreatePayoutBatch.cs:132`.

2. **i18n parity — missing cs-CZ key (third-strike).** `BusinessErrorMessage.PayoutBatchCsvPathAlreadySet` = `payoutBatch.csvPathAlreadySet` has **no** `cs-CZ.ts` key. Its direct mirror `invoice.blobPathAlreadySet` (T-0068a, same admin/log-only set-once shape) HAS a key, so the established pattern requires one. This is the genuine THIRD occurrence-of-concern of the recurring "code without a cs-CZ key" finding the draft armed. **Fold:** add `'payoutBatch.csvPathAlreadySet'` to `frontend/src/lib/i18n/cs-CZ.ts` (mirror the `invoice.blobPathAlreadySet` wording). **Harvest:** append a recurring-findings.md row (count 3, T-0068a/refund-dispute/T-0102b) + ping Architect to propose a mechanical `BusinessErrorMessage` ↔ `cs-CZ.ts` parity check in `check-consistency.mjs`.

## Required folds (non-blocking but mandated)

- **`must-cover-tests.md` registry not updated.** Two new set-once properties landed (`PayoutBatch.CsvBlobPath` via `AttachCsvBlobPath`; `Order.PayoutBatchId` via `AssignToPayoutBatch`). The tests EXIST and are red-first (no Gate 5 coverage fail), but the "Known set-once invariants" table (line 162-168, "Add a row here when a new set-once property lands … reviewer keeps it current") was not extended. Add the two rows. `docs/process/must-cover-tests.md`.
- **HIGH-3 AC-7 partial-resume assertion** (optional hardening): add a multi-maker re-run-after-partial-failure test asserting exactly N new FV-CZ numbers. Structurally correct today; the assertion would close the gap-free legal invariant under test.
- **HIGH-2 cross-foot in integration** (optional): assert each CSV line amount + each fee-invoice amount against seeded rows, not just the batch total.

## AC matrix (38 ACs)

| Ticket | ACs | Result |
|---|---|---|
| T-0101 (8) | AC-1..8 | PASS — entity/enum/repo/migration/indexes/Q-0017 all verified; role doc folded |
| T-0102a (12) | AC-1..12 | PASS except **AC-2/AC-5 concurrent-race leg (BLOCKER-1)** — sequential re-run passes; concurrent loser 500s |
| T-0102b (11) | AC-1..11 | PASS — fee invoices, PDFs, CSV golden, re-entrancy, CSV download all verified |
| T-0104 (7) | AC-1..7 | PASS — thin ADR-0020 wrapper, 4-branch, timer+HTTP, no logic |

## Gates 1–7

| Gate | Result |
|---|---|
| G1 Layering | PASS — `Core.Domain/Payouts/*` BCL-only; `IPayoutMetrics` pure port, impl in `Config/Observability`; `GenericPayoutCsvFormatter` pure in AppServices |
| G2 CQRS/UoW | PASS — one-file feature; no `SaveChangesAsync` in handler/service; claim+invoices+outbox+audit in one UoW |
| G3 Money | PASS — `total_amount_minor BIGINT`, all `long`; only display-edge decimal in formatter; no `decimal` columns |
| G4 Security | PASS — `[Authorize]` admin audience on controller; fail-closed session check first (handler:112); CSV streamed through host (no direct blob link); HTTP hatch `AuthorizationLevel.Function` |
| G5 TDD red-first | PASS — `cc1ed3b` tests-only, precedes all impl; 4 pure-logic surfaces pinned; red test files byte-identical `cc1ed3b`..HEAD (no post-impl rewrite). FOLD: must-cover registry rows |
| G6 Errors/i18n | **FAIL → BLOCKER-2** — 1 of 6 codes (`csvPathAlreadySet`) lacks a cs-CZ key |
| G7 Docs/RDD | PASS w/ fold — role doc fully folded (State/CompletedAt/MakerCount/exclusion invariants/impl pointer); ADR 0009 amendment present; INDEX/open.md updated. FOLD: must-cover registry |

## Verification re-run

- `dotnet build backend/src/Makables.Api.slnx` — **0 errors / 0 warnings**
- `dotnet test Makables.Tests` — **1579 passed**, 0 failed
- `dotnet test Makables.IntegrationTests` — **200 passed**, 0 failed
- `frontend tsc --noEmit` — **0 errors**
- `node scripts/check-consistency.mjs` — **exit 0, 129 tracked** (+4 vs baseline: `CreatePayoutBatch.cs`, `GenericPayoutCsvFormatter.cs`, `IPayoutArtifactService.cs`, `PayoutArtifactService.cs` — all T1 "static-class-wrapper" false positives, exactly the claimed class)
- NSwag — only `admin-api.v1.ts` (+244, 2 new payout endpoints) + `admin-api.v1` spec hash; no bare `Response` class (typed `CreatePayoutBatchResponse`); customer/maker/public untouched

## Routing

- **SecOps mandatory** (T-0102a/b security_touching — money aggregation, financial documents, admin CSV/bank file). Flag HIGH-1 race for their concurrency review.
- **Architect ping** — i18n-parity third strike (BLOCKER-2 harvest).
- Optimizer — handler is a multi-step money pipeline but the eligibility query materializes once (HIGH-4 satisfied); no algorithmic concern surfaced.
