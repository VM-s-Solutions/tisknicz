# Gate 8 (Performance) — refund-dispute bundle (T-0105 + T-0106 + T-0107)

- **Branch:** `feat/refund-dispute-bundle` (6 commits, `abf5cb1..dfb731e`)
- **Reviewer:** Performance Optimizer (Gate 8, ADR 0023 NFRs)
- **Date:** 2026-06-12

## Verdict: GATE8_FOLD

No BLOCKER, no HIGH. One MEDIUM fold (refund call sits inside the shared
retry-on-timeout pipeline — a money-moving POST should not auto-retry), three
informational nits. All 7 mandated checks pass at the budget level. No hot
path from the charter table is touched — every new surface is an admin or
party exception action; ADR 0023 budgets N/A, sanity-level review applied.

---

## Check-by-check findings

### Check 1 — Refund path: provider call + single Order load + single UoW commit — PASS

`backend/src/Makables.Core.AppServices/Features/Orders/RefundOrder.cs` —
query census for the success path: **1** tracked Order load (`:120`,
`GetByIdUnscopedAsync`), **1** country-config lookup via
`providerFactory.ResolveAsync` (`:152`), **1** Comgate HTTP POST (`:157`),
**1** User load (`:186`), **0-1** for `languageResolver.ResolveForUserAsync`
(`:196` — zero when `PreferredLanguage` is set; the CountryConfiguration
repo caches in-request per `ILanguageResolver.cs:38-41`). No second Order
load, no navigation walk, no loop — N+1 impossible by shape. Zero
`SaveChangesAsync` in the handler; mutation + outbox row + admin-audit row
land in the single `UnitOfWorkPipelineBehavior` commit (ADR 0014).

Provider call is Polly-wrapped: `ComgatePaymentProvider.cs:358` →
`CallComgateAsync` → `RetryPipeline.ExecuteAsync` (`:409`), the registry
pipeline keyed `"comgate"` (3x exponential jittered backoff, 200 ms base, on
`HttpRequestException` / `TaskCanceledException` / 408 / 429 / 5xx), fresh
`HttpRequestMessage` per attempt (`:412-418`), named HttpClient. B7
satisfied mechanically — but see **[MEDIUM M-1]** for the retry-class
mismatch on this specific operation.

### Check 2 — Dispute queries: tracking + the partial-unique double-open check — PASS

**Which mechanism serves the double-open check: predicate query, with the
index as commit-time backstop — not insert-conflict.** The primary gate is
`order.State == OrderState.Disputed` on the already-loaded Order
(`OpenCustomerDispute.cs:100`, `OpenMakerDispute.cs:96`, `OpenDispute.cs:90`)
— **zero extra queries on the happy open path**; the dispute row is only
loaded in the Silent-Success branch. That branch's predicate query
(`DisputeRepository.cs:27-30`: `WHERE order_id = ? AND resolved_at IS NULL`)
matches the partial index `ux_disputes_order_open UNIQUE (order_id) WHERE
resolved_at IS NULL` predicate exactly → index-served (the global
`is_active` filter is a residual heap check on <=1 row). The concurrent-open
race (two requests both see non-Disputed) is settled by the same index at
UoW commit: the loser's INSERT hits 23505 and the transaction rolls back —
insert-conflict is the safety net, not the check.

`GetOpenByOrderIdAsync` is **tracked** by design — `ResolveDispute.cs:149`
mutates the returned entity (`dispute.Resolve`). The three Silent-Success
callers use it read-only; the cost is one change-tracker snapshot of a
single <=2 KB row on an idempotent-retry path. A split `*ReadOnlyAsync`
variant would save sub-ms on a cold path — below the charter's
micro-optimization threshold; the single-method contract is documented in
the repo XML doc (`IDisputeRepository.cs:10-14`). B2 deviation accepted.

### Check 3 — ResolveDispute nested dispatch: Order load count — PASS (2 roundtrips, assessed acceptable)

**Answer: 2 SQL roundtrips, 1 tracked instance.** Outer load at
`ResolveDispute.cs:112`, inner at `RefundOrder.cs:120` — both via
`GetByIdUnscopedAsync`, which is `IgnoreQueryFilters().FirstOrDefaultAsync`
(`OrderRepository.cs:129-131`). `FirstOrDefaultAsync` always issues SQL; the
EF identity map dedupes at **materialization**, not at query time — the
second query's row resolves to the already-tracked instance, so both
handlers mutate the SAME object (the inner `ValidateRefund` correctly
evaluates the restored, uncommitted state; reviewer-draft HIGH-3 leg 4
confirmed implemented). `FindAsync` would short-circuit to 1 roundtrip but
cannot compose `IgnoreQueryFilters` — switching would silently re-hide
soft-deleted orders from the admin path (ADR 0013 regression). The 2nd
roundtrip is the correct price for filter-bypassing unscoped loads.

Same shape duplicates the **customer User load**: `ResolveDispute.cs:157` +
`RefundOrder.cs:186` (`UserRepository.cs:11`, `FirstOrDefaultAsync`) = 2
roundtrips for the same row. `languageResolver` runs twice but the country
lookup caches in-request. **Total duplicate cost: 2 extra PK-index lookups,
~1-2 ms, on an admin action expected a few times per day.** Nit **[N-2]** —
do not churn.

Commits: Refunded path = 2 x `SaveChangesAsync` (inner UoW flush carrying
resolution + refund + inner audit row atomically; outer UoW for the outer
audit row). Cancelled/Resumed = 1. Matches the §C.3 design; no perf concern.

### Check 4 — Auto-deliver sweep unaffected — PASS

`OrderRepository.cs:179-186` (`GetAutoDeliverableUnscopedReadOnlyAsync`):
predicate `State == Shipped && AutoDeliverAt != null && AutoDeliverAt <
asOf` — the file is **not in the diff** (verified via diff stat). A dispute
flips `State` to `Disputed`, so disputed orders fall out of the sweep
predicate with zero predicate change and zero scan-cost change. On a
Resumed restore, `AutoDeliverAt` is deliberately not extended (§C.10): an
overdue order auto-delivers on the next sweep — documented design, not a
regression.

### Check 5 — Migrations: index coverage + no table rewrites — PASS (with N-1 index note)

- `20260612115151:27-32` — `orders.refunded_amount_minor BIGINT NOT NULL
  DEFAULT 0`: PG 11+ stores a non-volatile default in the catalog
  (`attmissingval`) — **metadata-only, no table rewrite, no backfill scan**,
  brief ACCESS EXCLUSIVE for the catalog update only.
- `20260612121152:32-36` — `orders.pre_dispute_state SMALLINT NULL`, no
  default: metadata-only, same story.
- `disputes` indexes: PK(`id`) + `ux_disputes_order_open` partial unique
  (`:70-75`). The explicit `HasIndex(OrderId)`
  (`DisputeConfiguration.cs:55-58`) suppresses EF's conventional FK index,
  so there is **no full index on `order_id`** — confirmed in
  `MakablesDbContextModelSnapshot.cs:1585-1588`. The only query shipped in
  this bundle (open lookup) is fully served by the partial index, so B3
  passes. Resolved-history reads (T-0118 admin UI) and the FK
  cascade/constraint check on GDPR hard-delete (T-0110) will seq-scan
  resolved rows — see **[N-1]**.
- Template seeds are constant INSERTs; trivial.

### Check 6 — Admin endpoints — sanity PASS (budget N/A)

`Web.Admin/Controllers/OrdersController.cs` — one-liner `Mediator.Send`
dispatches under `[Authorize]`; no projection work in controllers.
`ChangeOrderStateManually.cs:86-131` is the leanest handler in the bundle:
1 Order load + pure in-memory policy (`ManualOrderTransitionPolicy`) + UoW.
No emails, no outbox (PM default), no extra reads. ADR 0023 lists no budget
for admin surfaces — sanity only, nothing to flag.

### Check 7 — Email enrichment-at-enqueue: 3 new branches, per-event single-query shape — PASS

`IEmailSendService.cs` — `SendOrderRefundedCustomerEmailAsync`,
`SendOrderDisputedAdminEmailAsync`,
`SendOrderDisputeResolvedCustomerEmailAsync`: all three deserialize the
payload and call the shared `DispatchOrderEmailAsync` (template lookup, same
as the T-0067/T-0079 precedent). **Zero per-send entity loads, zero loops.**
The admin-digest recipient resolves from `IOptions<EmailOptions>` (memory)
at send time; missing config parks the row Configuration-class without a DB
cost (§C.9). Enqueue side: 1 User load + cached language resolve per event
— identical query shape to `AcceptOrder.cs:118` / `CancelExpiredOrder.cs:122`.

### Hygiene sweep — PASS

Diff-wide grep over `+` lines: zero `.Result` / `.Wait(` /
`.GetAwaiter()` / `ToListAsync(` / `ToList(`. No new list endpoints (B6
N/A). `CancellationToken` accepted and propagated to every await in all six
new handlers, the repository, and the Comgate adapter (B4). Money math is
`long` minor units end-to-end incl. the Comgate `amount` form field via
`ToString(CultureInfo.InvariantCulture)` (B8).

---

## Findings

```
[MEDIUM] backend/src/Makables.Infra.Clients/Comgate/ComgatePaymentProvider.cs:358 — B7
What: RefundAsync routes through the shared "comgate" retry pipeline, which retries on
      TaskCanceledException (timeout) and 5xx — a non-idempotent money-moving POST.
Cost: cost model — a refund that Comgate processes but whose response times out is
      re-POSTed within seconds, with no admin in the loop. For a PARTIAL refund the
      gateway cap (cumulative <= captured) does NOT bound intent: a retried 400 Kc
      partial re-issues 400 Kc. T-0105 Alternatives G / Risk line 182 accepted the
      ADMIN double-submit (bounded by T-0118 confirm UI); the in-pipeline automatic
      retry is the same failure shape minus the human mitigation.
Fix: exclude the refund operation from the retry pipeline (single attempt; let the
     Transient error surface to the admin, who re-checks the Comgate portal before
     re-firing) — or get an explicit ticket-level acceptance line naming the
     automatic-retry case.
Refs: T-0105 §Alternatives G + §Risk; charter B7; reviewer draft HIGH-1.
```

```
[Nit] backend/src/Makables.Infra.Database/Configurations/DisputeConfiguration.cs:55 — B3 (forward-looking)
What: explicit partial-unique HasIndex(OrderId) suppresses EF's conventional FK index;
      disputes.order_id has no full index covering resolved rows.
Cost: cost model — T-0118's dispute-history list and T-0110's GDPR cascade delete
      seq-scan resolved disputes. Table is near-empty at MVP; cost materializes only
      with the admin read model.
Fix: add ix_disputes_order_id in the T-0118 migration when the history read model lands.
Refs: T-0118 (admin dispute UI); T-0110 (GDPR delete).
```

```
[Nit] backend/src/Makables.Core.AppServices/Features/Orders/ResolveDispute.cs:112,157 — B1-adjacent
What: nested RefundOrder dispatch re-issues the Order PK lookup (RefundOrder.cs:120)
      and the customer User lookup (RefundOrder.cs:186) — 2 duplicate PK roundtrips.
Cost: ~1-2 ms total per resolve on a cold admin path (a few/day). Identity map keeps
      a single tracked instance, so correctness is unaffected.
Fix: none recommended — FindAsync would dedupe but cannot compose IgnoreQueryFilters
     (ADR 0013 regression); the duplicate roundtrip is the cheaper trade.
Refs: ADR 0013; reviewer draft HIGH-3 leg 4.
```

```
[Nit] backend/src/Makables.Infra.Database/Orders/DisputeRepository.cs:27 — B2
What: GetOpenByOrderIdAsync is tracked for its three read-only Silent-Success callers.
Cost: one change-tracker snapshot of a single <=2 KB row per idempotent re-open; sub-ms.
Fix: none recommended — the resolve handler needs the tracked variant and a split
     method buys nothing measurable; deviation documented in IDisputeRepository.cs:10-14.
Refs: charter B2; ADR 0025 §Performance expectations item 2.
```

## Self-check

- No hot path from the charter table touched; no ADR 0023 budget engaged.
- M-1 cites B7 + the ticket's own risk section; it does not contradict the
  accepted A.5 provider-first decision — it narrows the retry class around it.
- All measurements above are query-census / cost-model from the diff; nothing
  was profiled, nothing fabricated.
