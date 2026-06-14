# Gate 8 (Performance) - payout-settlement-bundle

**Verdict: GATE8_PASS**
**Date:** 2026-06-13 - **Branch:** feat/payout-settlement-bundle (8 commits since e67ac97)
**Scope:** T-0103 settlement, T-0112 maker payout queries, T-0112a fee-invoice download, T-0116 maker payout frontend
**Tickets vs hot-path table:** none of the listed public hot paths. The maker dashboard list budget (ADR 0023 sec.1: 400 ms p95 / 1000 ms p99) governs T-0112 list/detail. Settlement T-0103 is a synchronous weekly admin action - no listed budget; cost-bounded, noted below.

Severity counts: BLOCKER 0, High 0, Medium 0, Nit 2.

---

## Backend

### 1. T-0103 complete-loop - materialize + loop + one UoW (PASS, upper-bound noted)
MarkPayoutBatchCompleted.Handle loads the claimed orders tracked
(GetByPayoutBatchIdForCompletionUnscopedAsync, OrderRepository.cs:311), runs one in-memory
pass calling Order.Complete + accumulating per-maker totals, then commits batch + N order
updates + N outbox rows + audit in one UoW. No nested mediator.Send (Q-0008 MARS lesson
honoured). IgnoreAutoIncludes skips the Attachments eager-load.

- B1 N+1: clean - single materialization, no per-row navigation read.
- B3 index: WHERE payout_batch_id = X is backed by partial index ix_orders_payout_batch_id
  (OrderConfiguration.cs:210; migration 20260613061353_PayoutBatches.cs confirms
  filter payout_batch_id IS NOT NULL). NO INDEX GAP.
- B5 .Result/.Wait(): none. B4 CT: accepted + propagated to every await.
- Cost model (upper bound): a weekly batch claims up to one week of delivered orders. At
  ADR 0023 sec.2 MVP scale (<=200 orders/day, ~7-day window -> <=1,400 orders, realistically
  far fewer once filtered to Delivered+unclaimed), the tracked loop issues 1 SELECT + ~N
  UPDATE statements at SaveChanges, batched by EF. Per-entity tracked updates over hundreds
  of rows is acceptable for a once-weekly admin click off any latency budget. If a batch
  ever exceeds ~5k orders, revisit with ExecuteUpdateAsync for the bulk state flip - NOT
  warranted at MVP scale. Watch-item, not a finding.

### 2. Per-maker email grouping - in-memory GroupBy, serial per-maker lookups (PASS, Nit)
The per-maker accumulation is an in-memory Dictionary fold over the already-materialized
list - O(n), correct, no extra query. The email loop (MarkPayoutBatchCompleted.cs:189) then
does, per distinct maker M: makers.GetByIdAsync + users.GetByIdAsync +
languageResolver.ResolveForUserAsync. Order has no Maker EF navigation (deviation 3 - scalar
MakerId), so this is a deliberate explicit-lookup shape, NOT a hidden N+1 over the order
list: it is M round-trips, not N. M = distinct makers in one weekly batch (MVP: low tens).
All three lookups are PK/indexed single-row reads inside the open transaction.

- [Nit] MarkPayoutBatchCompleted.cs:189-215 - B1-adjacent. Serial await per maker
  (3 round-trips x M) inside the settlement transaction. Cost: ~3M sequential DB round-trips;
  at M~20 and ~1 ms/round-trip on the same connection, ~60 ms added to a weekly admin call -
  immaterial. Fix (defer): if M grows, add IMakerRepository.GetByIdsAsync /
  IUserRepository.GetByIdsAsync batch variants and pre-resolve once before the loop. Not
  worth the interface surface at MVP scale. Refs: ADR 0023 sec.2 scale; no budget applies.

### 3. T-0112 queries (PASS)
PayoutQueries.cs - all three are AsNoTracking + projection-only; list + outbox paginate via
PagedData<T> (patterns sec.A.8). B2/B6 satisfied.

- GetMakerPayoutsPagedAsync (:23): GROUP BY payout_batch_id over the maker claimed orders ->
  two-pass CountAsync then paged JOIN to the batch header, AsNoTracking + IgnoreAutoIncludes.
  Fee-invoice ids resolved in ONE follow-up IN query over the page batch ids (:73) - bounded
  at pageSize (<=50), NOT an N+1. B3: grouping predicate o.MakerId == makerId AND
  o.PayoutBatchId != null served by ix_orders_payout_batch_id (partial) for the claimed-rows
  scan; maker_id is the lead column of ix_orders_maker_state_created. Header JOIN hits the
  payout_batches PK. Fee IN query hits IX_invoices_payout_batch_id + maker_id filter. Indexed.
- GetMakerPayoutDetailAsync (:95): queries the maker orders directly (WHERE PayoutBatchId ==
  batchId AND MakerId == makerId) - it does NOT load the whole cross-maker batch then filter.
  IDOR AnyAsync pre-check, header projection, line projection, single fee-invoice lookup -
  4 small reads, all AsNoTracking. B3: the (payout_batch_id, maker_id) predicate is served by
  ix_orders_payout_batch_id (partial, high selectivity - one batch ~ one maker slice). No
  composite (maker_id, payout_batch_id) index exists, but the partial single-column index is
  selective enough at MVP scale (one batch = tens of rows). NOT a gap.
- GetMakerOutboxEventsForOrderAsync (:149): paged, AsNoTracking, payload-free projection
  (never references PayloadJson - PII-safe). IDOR AnyAsync guard. B3: WHERE aggregate_id =
  orderId served by ix_outbox_event_aggregate_id (OutboxEventConfiguration.cs:40). ORDER BY
  CreatedAt DESC is an unindexed sort over one order events (bounded, single-digit rows) -
  trivial.

### 4. T-0112a fee-invoice download (PASS)
FilesController.DownloadFeeInvoice (:195): single GetForMakerReadOnlyAsync lookup
(InvoiceRepository.cs:92, AsNoTracking, i.Id == invoiceId AND i.MakerId == makerId IDOR
shield). Streams the blob via File(download.Content, ...) - STREAMED, not buffered into a
MemoryStream (contrast the label path deliberate dual-stream buffer). ETag/304 short-circuit;
private no-store cache. B2 satisfied; no buffering. CT propagated.

### Money (B8) - PASS
All payout/line math stays long minor units end-to-end (handler accumulators, DTOs, formatCzk
on the client). No decimal round-trip.

---

## Frontend

### 5. vyplaty route (PASS)
- F1 Server Components: list (page.tsx), detail ([batchId]/page.tsx), rows, pagination are
  all server-rendered. The ONLY use-client island is fee-invoice-download.tsx (needs a click
  handler + blob anchor) - justified.
- F2 no useEffect fetch: confirmed - both pages fetch on render via getMakerPayouts /
  getMakerPayoutDetail (payouts-client.ts); the client island calls downloadFeeInvoice in an
  event handler only. Non-negotiable upheld.
- SSR single request: list = 1 request; detail = 1 request. No client re-fetch of SSR data
  (F5). Pagination is Link-based (URL page), no client store.
- F3 next/image: N/A - no product/maker photos on these routes.
- F4/F6 deps: no new runtime dependency; no charting/PDF/markdown module-scope imports. Blob
  download uses the existing apiFetch runtime (parse blob, 120 s). No bundle baseline
  regression beyond the route own JS (one small client island).
- [Nit] [batchId]/page.tsx:108 - F7-adjacent: detail.orders.length for the order count is
  fine (the breakdown list is already materialized for the table; no extra fetch, no .filter
  chain). Noted only for completeness.

### 6. i18n-chunk duplication (Q-0014) - noted, owned elsewhere
This bundle adds ~56 cs-CZ keys (cs-CZ.ts). Marginal contribution to the known i18n-chunk
duplication tracked under Q-0014; growth is linear in keys and immaterial here. Owned by the
i18n/bundling workstream - not a finding against this bundle.

---

## Index summary
NO INDEX GAP. Every new WHERE/JOIN/GROUP BY column ships with a backing index in migration
20260613061353_PayoutBatches.cs:
- ix_orders_payout_batch_id (partial, payout_batch_id IS NOT NULL) - completion loop + maker
  list grouping + maker detail.
- IX_invoices_payout_batch_id - fee-invoice resolution (list IN-query + detail).
- ix_outbox_event_aggregate_id (pre-existing) - maker outbox-events query.

## Watch-items (not findings)
1. T-0103 tracked loop scales linearly with batch size; revisit ExecuteUpdateAsync only
   beyond ~5k orders/batch (well past MVP).
2. Email loop is 3 serial round-trips x M makers; add batch-fetch repo variants only if M
   grows past low tens.
