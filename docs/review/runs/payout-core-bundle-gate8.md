# Gate 8 (Performance) - payout-core bundle

Branch feat/payout-core-bundle, 9 commits (e6640f9..fe167f9). Scope: Q-0017 data-fix + T-0101 PayoutBatch entity + T-0102a claim + T-0102b artifacts + T-0104 weekly Function + NSwag admin regen.
Hot-path note: this bundle touches NO public/SSR hot path. The claim path is an admin + weekly-Function batch path running once/week off-peak (Mon 02:00 UTC). Budgets are ADR 0023 sec.3 background-job tolerance + sec.2 MVP scale (<=200 orders/day -> hundreds Delivered/week; <=5 GB DB).

## Verdict: GATE8_PASS  (0 BLOCKER, 0 HIGH, 0 MEDIUM, 2 LOW notes)

## Check-by-check

### 1. Eligibility query - N+1? indexed?
PASS. OrderRepository.GetPayoutEligibleUnscopedAsync (OrderRepository.cs:234) is TWO queries, not N+1:
- Q1: orders WHERE State==Delivered AND PayoutBatchId==null AND CountryCode==cc (IgnoreAutoIncludes, tracked - correct, the claim mutates each row via AssignToPayoutBatch and rides the UoW).
- Q2: one batched maker slice Maker WHERE makerIds.Contains(m.Id), projected to Id/CompanyName/BankAccount, AsNoTracking, into a dictionary. NO per-order maker lookup. Order to Maker is a scalar FK (no EF navigation), so the explicit two-step is right - Contains becomes one WHERE id = ANY(...).
Cost model: N Delivered orders across M makers -> 2 queries total, independent of M. At hundreds/week, sub-50 ms.
Index coverage: the orders filter seeks on State via ix_orders_state (partial WHERE is_active, OrderConfiguration.cs:203). PayoutBatchId IS NULL is NOT index-served - ix_orders_payout_batch_id is partial WHERE payout_batch_id IS NOT NULL (the inverse), so the planner seeks Delivered then filters payout_batch_id IS NULL + country_code in-heap. Acceptable at MVP scale; see NOTE-1 for the growth gap. Maker slice seeks PK makers.id - covered.

### 2. Claim write - bulk vs per-entity
PASS (per-entity, intended). CreatePayoutBatch.Handler (CreatePayoutBatch.cs:254) loops AssignToPayoutBatch over the already-tracked entities + 1 payoutBatches.AddAsync. N tracked UPDATEs + 1 INSERT in one UoW, not ExecuteUpdate. Orders are already tracked (Q1 is tracked) so no second load. ExecuteUpdate would break single-UoW atomicity with the batch INSERT + audit row + fee invoices, so per-entity is CORRECT here, not merely tolerable. Acceptable at hundreds/week.

### 3. Exclusion counts
PASS. Computed from the SINGLE materialised candidates set in one foreach (CreatePayoutBatch.cs:174-200) via PayoutEligibility.Classify - no second query. excludedNoBankMakerCount derives from a HashSet of distinct maker ids in the same pass. Perf + correctness clean.

### 4. Fee invoices - per-maker loop, N+1 inside?
PASS, one redundant read flagged (NOTE-2). PayoutArtifactService.GenerateAsync (PayoutArtifactService.cs:106) loops over perMaker groups (bounded by maker count, tens). Per maker: 1 makers.GetByIdAsync, 1 users.GetByIdAsync, 1 invoice issue, 1 QuestPDF render, 1 blob upload, 1 outbox enqueue - sequential, off-peak, acceptable. NO per-order N+1 inside: line items built from the already-loaded makerOrders group (line 174); CSV amount sums the same group. Batch orders come from one query (GetByPayoutBatchIdUnscopedAsync, AsNoTracking+IgnoreAutoIncludes, ordered) or the passed-in claimed list on first run.

### 5. CSV
PASS. Single pass over the per-maker aggregate (PayoutArtifactService.cs:225), built in memory then one blob upload. Buffered not streamed - fine: tens of lines, a few KB.

### 6. PayoutBatch indexes
PASS, all present.
- ux_payout_batches_country_batch_number unique (country_code, batch_number) - backs week guard GetByNumberAsync + ADR 0009 uniqueness.
- ux_payout_batches_open_per_country partial unique WHERE state=Processing AND is_active - backs GetOpenBatchAsync (exact-match seek) + read-then-write race guard.
- ix_orders_payout_batch_id partial WHERE payout_batch_id IS NOT NULL - backs GetByPayoutBatchIdUnscopedAsync + T-0112/T-0118 orders-in-batch reads.
- IX_invoices_payout_batch_id FK index - backs GetByPayoutBatchIdAsync re-entrancy lookup. Non-partial (NULL for all Customer invoices); harmless at scale.

### 7. Migrations
PASS. 20260613061353_PayoutBatches.cs: AddColumn orders.payout_batch_id (nullable, metadata-only, no rewrite) + CreateTable payout_batches + FK on invoices.payout_batch_id (column existed from T-0068a; FK constraint validates trivially, all existing rows NULL). Both FK adds are RESTRICT, validate against empty/all-NULL - no scan cost. Q-0017 FixEmailSubjectPlaceholders UPDATE touches 16 rows - trivial. All adds nullable/new-table -> backward-compatible per ADR 0023 sec.7.

### 8. RunWeeklyPayoutBatchFunction - MARS concern?
PASS. RunWeeklyPayoutBatchFunction.cs:86 is a single mediator.Send(CreatePayoutBatch.Command) dispatch, then reads scalar response fields for logging. No enumeration, no streaming, no second concurrent reader -> no MARS concern. CancellationToken propagated through DispatchAndInterpretAsync -> mediator.Send.

### 9. NumberingSequenceAllocator Local-set check (deviation 4)
PASS. NumberingSequenceAllocator.cs:53 adds an in-memory Local.FirstOrDefault scan per allocation before the FOR UPDATE round-trip. O(tracked NumberingSequence entries) - only NumberingSequence rows are tracked, exactly one per country/scope/year in a UoW, so O(1) in practice (one row: the FV-CZ sequence). Hit once per Fee invoice allocation; each hit scans a 1-element Local set. The check is CORRECT - it lets per-maker Fee invoices chain off the same tracked instance instead of creating duplicate Added rows (alternative is a PK clash). Negligible cost.

---

## Async hygiene sweep (B4, B5)
- B5: zero .Result / .Wait() / .GetAwaiter().GetResult() in the bundle.
- B4: handler + service + repository signatures accept CancellationToken and propagate to every await (EF ToListAsync/FirstOrDefaultAsync, blob UploadAsync, renderer, mediator.Send). The two OperationCanceledException rethrows (guarded by IsCancellationRequested) in PayoutArtifactService correctly let cancellation escape the catch-all instead of being swallowed as artifacts-incomplete.
- B2: read-only reads (maker slice, batch-orders read, maker/user lookups) NoTracking; the claim-mutating orders query correctly tracked.

---

## Notes (LOW - backlog, not gating)

NOTE-1  OrderConfiguration.cs:253  - B3, LOW  (no covering index for the eligibility predicate)
What: the claim scan filters State==Delivered AND PayoutBatchId IS NULL AND CountryCode==cc but the only seek index is ix_orders_state (state alone); payout_batch_id IS NULL and country_code are heap-filtered.
Cost: cost model - at MVP scale the post-seek filter is over a small row set, inside the best-effort background-job budget (ADR 0023 sec.3). Grows linearly with cumulative volume: claimed orders keep state=Delivered AND a non-null batch id, so they stay in the ix_orders_state seek set and are filtered out by IS NULL every week. By end of year 1 (thousands of historical Delivered orders) the seek returns the full Delivered history each run.
Fix: when Delivered volume warrants, add partial index ix_orders_payout_unclaimed ON orders (country_code) WHERE state=Delivered AND payout_batch_id IS NULL AND is_active - stays tiny (only unclaimed rows) and turns the weekly scan into a direct seek. Not needed now; perf-todo list per ADR 0023 sec.1.
Refs: ADR 0023 sec.1 (perf-todo), sec.2 (scale), CLAUDE.md Performance.

NOTE-2  PayoutArtifactService.cs:129 + :275  - LOW  (duplicate users.GetByIdAsync per maker on first run)
What: on a fresh run the new-invoice branch loads makerUser (:129) then EnqueueMakerEmailAsync (:275) loads the SAME users.GetByIdAsync(maker.UserId) again - 2 identical user reads per maker.
Cost: cost model - M makers -> 2M user PK lookups instead of M. M is tens; off-peak; PK seek sub-ms. Small real cost, clean avoidable double-read on a path already doing per-maker round-trips.
Fix: pass the already-loaded makerUser into EnqueueMakerEmailAsync instead of re-fetching. One-line signature change; implementer call.
Refs: B1 (avoid redundant reads in a loop).

---

## Self-check
- Every finding has file:line, severity, cost (NOTE-level uses stated cost models - no fabricated ms; the path is not measurable from the diff).
- No BLOCKER/HIGH -> no ADR 0023 budget citation needed; PASS rests on the path being a weekly off-peak batch under sec.3 best-effort + sec.2 scale, not a sec.1 hot-path budget.
- No finding contradicts an accepted ADR. NOTE-1 index is a future optimization, consistent with ADR 0023 sec.1 perf-todo discipline.
- No public/SSR/maker-dashboard hot path touched -> hot-path High floor does not apply.
