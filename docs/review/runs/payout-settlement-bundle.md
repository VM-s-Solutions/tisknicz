# Payout-settlement bundle — Final review verdict (T-0103 + T-0112 + T-0112a + T-0116)

Branch `feat/payout-settlement-bundle`, 8 commits (fec49af grooming + 086768d..f21f833). Scoped diff `fec49af..HEAD` = 54 files. PR #2 of the payout bundle (PR #1 = payout-core, on this branch via merge e67ac97; master base = f0a07e2).

## Verdict: APPROVE WITH MINOR FIXES (non-blocking)

No BLOCKERs. Every headline security concern (the three-query maker IDOR, the T-0112a download IDOR + Fee gate, CSV non-exposure, i18n parity) passes. Build 0/0, unit 1626/1626 green, frontend tsc clean, consistency exit-0 (133 tracked, no new non-baseline). The integration suite is pre-confirmed GREEN at reduced parallelism (env, not logic) — not re-blocked. Two real-but-minor gaps (mid-loop rollback test absence; invoice.md/outbox.md role docs not updated) and one ruling (AC-3 audit) below. None rise to request-changes; they are fix-forward conditions the implementer should land before merge, but I do not hold the line on them given the robust idempotency proof + the AC-3 reinterpretation is defensible.

## (b) GetMakerPayoutDetail IDOR verdict: PASS

`PayoutQueries.GetMakerPayoutDetailAsync` (`backend/src/Makables.Infra.Database/Payouts/PayoutQueries.cs:95-147`):
- IDOR+existence guard (`:103-107`): `AnyAsync(o.PayoutBatchId == batchId && o.MakerId == makerId)` → returns `null` for BOTH unknown and cross-maker. Same shape, no 403 oracle.
- Line projection (`:117-130`): `Where(o => o.PayoutBatchId == batchId && o.MakerId == makerId)` — projects ONLY this maker's lines, never the whole batch. Another maker's order numbers cannot appear.
- `MakerTotalPaidMinor` = `lines.Sum(l => l.MakerPayoutAmountMinor)` (`:142`) — the per-maker slice, NOT `PayoutBatch.TotalAmountMinor`. `long` throughout.
- `FeeInvoiceId` maker-scoped (`:132-134`: `i.MakerId == makerId && i.Type == Fee`).

Outbox-events query (`:149-190`): payload-free — the projection references only `EventType / ProcessedAt / NextRetryAt / LastErrorKind / CreatedAt`. `PayloadJson` / `LastErrorCode` / `RetryCount` are NEVER named (grep-clean). Cross-maker order → empty page (`:160-161`), not an oracle.

List query (`:23-93`): per-maker GROUP BY/SUM, Fee-invoice scoped to `i.MakerId == makerId && i.Type == Fee`. DTOs (`MakerPayoutListItemDto`, `MakerPayoutDetailDto`, `MakerPayoutOrderLineDto`, `MakerOutboxEventDto`) carry no `BankReference`, no `CsvBlobPath`, no cross-maker total.

## (c) AC-3 ruling: ACCEPT the reinterpretation + raise a Q-item (do NOT fold the hard rule)

The conflict is real and the ticket's stated reasoning is **factually wrong**. `AdminAuditPipelineBehavior` (`backend/src/Makables.Core.AppServices/Behaviors/AdminAuditPipelineBehavior.cs:46-75`) writes an audit row on EVERY `IAdminAuditableCommand` whose handler returns `IsSuccess == true` — it does NOT inspect whether the handler mutated. The idempotent re-call returns `BusinessResult.Success(... AlreadyCompleted = true)` (`MarkPayoutBatchCompleted.cs:144`), which IS a success → the pipeline WILL write a SECOND audit row. The ticket's claim (T-0103 line 116: "the audit pipeline records nothing because the handler returns Success with no mutation") does not hold against the shared pipeline.

Ruling: keeping `IAdminAuditableCommand` (the hard rule — money settlement must be attributed, never "system") is correct and non-negotiable. The literal AC-3 "no second audit row" is unattainable without changing the shared pipeline (which also governs Refund/Dispute/ChangeState — out of scope for this PR). The implementer's robust reinterpretation — asserts NO second outbox row + order stays Completed + first bank-ref authoritative (`MarkPayoutBatchCompletedIntegrationTests.cs:252-284`), and does NOT assert audit-count in the idempotent test — is the right call. The second audit row is benign: a no-op `Processing→Completed` (before==after JSONB) audit entry on a re-call is harmless audit noise, not a money or state defect.

This is NOT a fold of the hard rule. It IS an undocumented AC reinterpretation — the only required correction is to **record it as a Q-item in `docs/questions/open.md`** so the deviation is auditable, and **recommend the Architect make `AdminAuditPipelineBehavior` skip no-op audits (before==after) platform-wide** as a separate ticket (it would let AC-3 hold literally and de-noise every idempotent admin re-call). Architect ping warranted.

## (d) BLOCKERs: NONE

## (e) Fold list (deviations — accept all, with the two fixes below)

1. **AC-3 "no second audit row"** — reinterpreted to robust idempotency (no 2nd outbox, order unchanged). ACCEPT; needs a Q-item (see c). Architect: no-op-audit-skip ticket.
2. **DTO layering** — T-0112 DTOs in `Core.Domain/Payouts/Queries/`, `IPayoutQueries` a domain interface. CORRECT per the `IOrderQueries` precedent; no AppServices leakage into Domain. Not a fold.
3. **Handler collaborator count** — `MarkPayoutBatchCompleted.Handler` ctor = **10** collaborators (draft predicted 8; it added `IMakerRepository` + `IUserRepository` to resolve maker email per group). Exceeds the ADR 0015 ~5 soft cap. Domain collaborators = 4 (batch repo, order repo, makers, outbox); the rest (users, clock, languageResolver, publicAppUrls, session, logger) are cross-cutting. Consistent with the RefundOrder/PayoutArtifactService money-handler shape. Architect ping (same as draft RDD note) — ACCEPT under "cross-cutting don't count", but flag the email-enrichment loop as an extract candidate.
4. **Deep-link base** — handler uses `IOptions<PublicAppUrlsOptions>.WebBaseUrl` (not the draft-predicted `MakerAppUrlsOptions`, which does not exist). Matches the PR#1 sibling `PayoutArtifactService.cs:285` exactly. Link = `{WebBaseUrl}/dashboard/maker/vyplaty/{batchId}` — batchId only, no cross-maker id (HIGH-2 satisfied). ACCEPT. Stale-link note: PR#1 used `/dashboard/maker/payouts`; T-0116's actual route is `/vyplaty` — T-0103's link is the correct live route; the PR#1 `/payouts` link is pre-existing and stale (not this PR's bug; worth an Architect note).

## Fixes requested before merge (non-blocking, fix-forward)

- **F1 (AC-8 traceability gap).** No mid-loop rollback test exists at integration OR handler level (`MarkPayoutBatchCompletedIntegrationTests` has only Settle_e2e / Idempotent / Multi_maker; handler tests have no rollback case). AC-8's "a forced mid-loop failure persists nothing" is UNPROVEN — the happy path proves the one-UoW commit but not the rollback wiring. The handler code is correct (`MarkPayoutBatchCompleted.cs:166-176` surfaces a mid-loop `order.Complete` refusal → UoW rolls back). Add an integration test seeding one non-Delivered order into the batch → assert batch stays `Processing`, no order flipped, zero outbox/audit rows. Pure-logic-adjacent but integration-only (rollback is a transaction property), so NOT a Gate-5 hard-fail; it is an AC traceability hole.
- **F2 (RDD parity, docs).** `invoice.md` and `outbox.md` exist on master but were NOT updated. T-0112a adds a Fee-invoice maker download read-surface (invoice responsibility changed); T-0112 adds a maker-scoped payload-free outbox-events read (outbox responsibility changed). Workflow step 5 requires the role file be updated in the same PR. `payout-batch.md` WAS correctly updated (the `Processing→Completed` transition). Append the two read-surface notes.
- **F3 (Q-item).** Record the AC-3 audit-pipeline reinterpretation in `docs/questions/open.md` (see ruling c).

## AC matrix (spot-traced)

- **T-0103 AC-1/2** PASS (Settle_e2e + PaymentDate_provided_sets_CompletedAt_to_midnight_utc; domain `Complete` `:270-272`). **AC-3** ACCEPT-reinterpreted (see c). **AC-4/5** PASS (Multi_maker grouping → 2 emails, summed per-maker totals deserialized). **AC-6** PASS (Batch_not_found). **AC-7** PASS (`PayoutBatchNotProcessing` guard `PayoutBatch.cs:263-265`, domain test). **AC-8** PARTIAL — one-UoW + no-SaveChanges proven; mid-loop rollback UNPROVEN (F1). **AC-9** PASS (admin host, fail-closed session `:121-125`). **AC-10** PASS (quadruple-brace seed `20260613164022_SeedPayoutSentMakerEmailTemplate.cs:58-59`). **AC-11** PASS (build, `payoutBatch.notProcessing` cs-CZ key present, migrations apply, regen deferred).
- **T-0112 AC-1..12** PASS. AC-4 per-line reconciliation: DTO carries Product/Shipping/Fee/Payout (`product − fee + shipping == payout`, projection maps shipping=2nd/fee=3rd correctly). AC-5 cross-maker+unknown → same `null`/404. AC-7/8 events payload-free + cross-maker empty page. AC-11 no Bank/CSV/cross-total on DTOs. AC-12 maker regen committed (globally-unique Responses, no bare `Response`).
- **T-0112a AC-1..7** PASS. Fee gate (`FilesController.cs:224-227`) fires after ownership load, before blob read; cross-maker/Customer-via-Fee/unknown all `order.notFound` identical; `private, no-store` + ETag/304; controller-direct (no MediatR feature).
- **T-0116 AC-1..12** PASS. Server Components default; `'use client'` only on `error.tsx` (Next.js boundary) + `fee-invoice-download.tsx` (download island); pagination is a Link-based Server Component (better than predicted). No useEffect fetch, no `any`/`console`, no client money math, formatCzk used, tykáni (`tvých`/`ti`/`najdeš`), state badges `Připravujeme`/`Vyplaceno` keyed, CSV-absence grep clean, blob `timeoutMs` present.

## Gates 1-7

- **Gate 1 (read ticket+ADRs):** done — tickets T-0103/T-0112/T-0112a/T-0116 + ADRs 0009/0013/0014/0015/0019/0020/0023/0025.
- **Gate 2 (checklist row-by-row):** all rows pass except G (F1 test gap) and the docs row (F2).
- **Gate 3 (AC traceability):** all ACs traced; AC-8 partial (F1).
- **Gate 4 (extension points):** no country branch in projection or email render; email via existing EmailSendService routing branch, not a new provider. PASS.
- **Gate 5 (RDD parity):** `IPayoutQueries`/repo interfaces correctly get NO standalone role file; `payout-batch.md` updated; `invoice.md`+`outbox.md` MISSED (F2). Handler collaborator count 10 > ~5 (Architect ping, fold #3).
- **Gate 6 (Tests / TDD HARD red-first):** PASS. `086768d` is tests-ONLY (verified: zero non-test files), contains `PayoutBatchCompleteTests.cs` (pure-domain guard/idempotency/midnight-UTC) + the handler grouping tests; impl `e6eb480` (PayoutBatch.Complete + handler) lands AFTER. Pure-logic surfaces are red-first. No after-the-fact pure-logic tests. No Gate-5 hard-fail.
- **Gate 7 (security ping):** SecOps confidence high — IDOR shields verified line-by-line, CSV non-exposure grep-clean, payload-free outbox, recipient-PII `no-store`. No ping needed beyond the standard money-touching sign-off.

## Optimizer note

`GetMakerPayoutsPagedAsync` runs the GROUP BY twice (count + page) and resolves Fee-invoice ids in a second IN query (acceptable — documented, page-bounded). `CompletedAt DESC` nulls-ordering for the in-flight Processing batch is MVP-acceptable (one in-flight batch). Not blocking; Architect/Optimizer may revisit the two-pass at scale.

## Harvest duty

i18n-parity (recurring-findings #2, count 3, HARVESTED) verified exhaustively and PASSED — no new strike. No new finding reaches count-3 this PR. No append.
