# Gate 8 (Performance) — order-cleanup bundle (T-0079 + T-0083)

- **Branch:** `feat/order-cleanup-bundle` (5 commits, `18f8401..ea3271f`)
- **Reviewer:** Performance Optimizer (Gate 8, ADR 0023 NFRs)
- **Date:** 2026-06-10

## Verdict: GATE8_FOLD_RECOMMENDED

No BLOCKER, no HIGH. Two MEDIUM folds, both cheap and in-PR-foldable. Four LOW
informational notes. All 12 mandatory checks pass at the budget level; the two
MEDIUMs are query-shape and doc-accuracy folds, not budget breaches.

---

## Check-by-check findings

### Check 1 — OrderMessageQueries: AsNoTracking + IgnoreAutoIncludes — PASS

`backend/src/Makables.Infra.Database/OrderMessages/OrderMessageQueries.cs:52-53`
and `:106-107` — both read methods chain `.AsNoTracking().IgnoreAutoIncludes()`
before the predicate. `OrderMessage` declares no navigations, so
`IgnoreAutoIncludes` is belt-and-braces; harmless.

### Check 2 — Repository reads + MarkAsRead shape — PASS (with M-2 caveat)

`backend/src/Makables.Infra.Database/OrderMessages/OrderMessageRepository.cs`
has no materializing read methods (write-side only: `AddAsync` + two
MarkAsRead). Both MarkAsRead methods use **`ExecuteUpdateAsync`** (`:48-56`,
`:71-79`) — the preferred single-SQL-UPDATE form, O(1) roundtrips independent
of thread length. No fetch+update loop anywhere. See **[MEDIUM M-2]** for the
transaction-boundary caveat.

### Check 3 — MarkAsRead WHERE predicate scope — PASS

`OrderMessageRepository.cs:49-53` (customer) and `:72-76` (maker): the UPDATE
WHERE is `order_id = @p AND author_role = <counterparty> AND
read_by_counterparty_at IS NULL AND EXISTS(... orders WHERE id = @orderId AND
customer_user_id/maker_id = @owner)`, plus the global `is_active` soft-delete
filter (query filters apply to ExecuteUpdate in EF Core 10). Ownership scope
is **inside** the UPDATE, not orderId alone — a cross-tenant probe touches
0 rows.

### Check 4 — PostMessage debounce: no extra roundtrip — PASS

`PostCustomerOrderMessage.cs:119` / `PostMakerOrderMessage.cs:109` — the
debounce predicate `order.ShouldEmitNotificationFor(...)` reads
`Maker/CustomerPendingNotificationEmailAt` as in-memory scalars on the Order
already loaded at `:95` / `:88` (`Order.cs:838-848`). Zero additional DB
roundtrips for the debounce decision. The emit path adds 2-3 reads
(maker/user/language) but is rate-bounded to once per 5-min window per
direction per order — acceptable cost model.

### Check 5 — PostMessage + outbox in single UoW commit — PASS

`PostCustomerOrderMessage.cs:82-162` — no `SaveChangesAsync` anywhere in the
handler. `OutboxWriter.Enqueue` (`Outbox/OutboxWriter.cs:27`) does
`db.Set<OutboxEvent>().Add(evt)` on the **same scoped DbContext**, so message
insert + counter increment + pointer update + outbox row all flush in the
single `UnitOfWorkPipelineBehavior.SaveChangesAsync` commit
(`Behaviors/UnitOfWorkPipelineBehavior.cs:63`). Same for the maker direction
and `CancelExpiredOrder.cs:132-139`.

### Check 6 — GetOrderMessages AuthorName N+1 — PASS (single SQL), with M-1 shape fold

`OrderMessageQueries.cs:70-86` / `:123-140` — the AuthorName resolution is
embedded in the `.Select(...)` projection, so EF translates it to **one SQL
statement per page** (correlated scalar subqueries in the SELECT list). No
client-side per-row roundtrip; no N+1 in the roundtrip sense. See [MEDIUM M-1]
for the SQL-plan-level improvement.

### Check 7 — Index (order_id, created_at DESC) — PASS

`Configurations/OrderMessageConfiguration.cs:62-64` —
`ix_order_messages_order_created` on `(order_id, created_at DESC)` via
`.IsDescending(false, true)`. Emitted in
`Migrations/20260609174208_OrderCleanupBundle.cs:116-120` with
`descending: new[] { false, true }`. Backs the thread-read ORDER BY without a
sort step. (LOW L-3: the `ThenByDescending(m.Id)` tiebreak column is not in
the index — Postgres incremental sort handles ties; negligible at <=50
rows/page.)

### Check 8 — Index for MarkAsRead predicate — PASS

`OrderMessageConfiguration.cs:70-72` —
`ix_order_messages_order_author_unread` on `(order_id, author_role)` partial
`WHERE read_by_counterparty_at IS NULL AND is_active`
(migration `:108-112`). Exact match for the UPDATE predicate including the
soft-delete filter the global query filter injects. Partial form keeps the
index near-empty on healthy (read) threads. Correct choice.

### Check 9 — PageSize cap 1..50 — PASS

`GetCustomerOrderMessages.cs:46-49` and `GetMakerOrderMessages.cs:38-41` —
`InclusiveBetween(1, MaxPageSize)` with `MaxPageSize = 50`. Page itself is
capped at `int.MaxValue / MaxPageSize` (`:41-44`, `:33-36`), which also
prevents `Skip` integer overflow. Pagination per patterns §A.8 contract.

### Check 10 — T-0083 sweep predicate + ix_orders_state adequacy — PASS (LOW)

`Orders/OrderRepository.cs:168-187` — predicate is
`State == PendingPayment && CreatedAt < cutoff` with `cutoff = asOf.AddHours(-24)`
hoisted to a local (`:179`) so the expression tree carries a constant, not a
per-row computation. `.AsNoTracking()`, projection to `Id` only (the Order
AutoInclude on Attachments does not apply to scalar projections).
`ix_orders_state` (partial `is_active`, `OrderConfiguration.cs:181-183`)
serves the scan: PendingPayment live cardinality is structurally bounded
(<= ~24-48h of unpaid orders; the daily sweep itself keeps the tail short),
so index-scan + filter + small sort is adequate. **No new index needed
in-bundle.** If unpaid-order volume ever grows x100, a partial composite
`(state, created_at)` is the out-of-bundle escape hatch — not now.

### Check 11 — Function MARS workaround — PASS

`Functions/Payments/CancelExpiredPendingPaymentOrdersFunction.cs:66-68` —
`.ToListAsync(cancellationToken)` materializes the id stream **before** the
per-row `mediator.Send` loop (`:70-103`), with the Q-0008 rationale comment
inline (Npgsql no-MARS; the downstream handler reuses the scoped DbContext).
Mirrors the AutoDeliverOrdersFunction precedent exactly. At 10-200 rows/run
daily, eager-list cost is sub-millisecond / sub-KB.

### Check 12 — OrderQueries UnreadMessageCount projections — PASS

`Orders/OrderQueries.cs` diff — customer list projects
`o.CustomerUnreadMessageCount` (`:~96`), maker list projects
`o.MakerUnreadMessageCount` (`:~158`, replacing the T-0081 `(int?)null`
placeholder). Both are scalar columns on the **same Order row** already being
read — zero additional roundtrips, zero subqueries. This is the payoff of the
denormalized-counter design (locked decision A.3).

---

## Findings

### [MEDIUM] OrderMessageQueries.cs:70-86, 123-140 — M-1 (B1-adjacent, SQL shape)

What: AuthorName is resolved via two correlated scalar subqueries inside the
per-row SELECT (Order.ContactName probe + Maker.CompanyName probe behind a
nested EXISTS), re-evaluated per message row.
Cost (model): the thread is single-order, so both names are constant across
the page, yet the plan executes up to 2 SubPlans x 50 rows = ~100 PK index
probes per page request (~1-5 ms + plan complexity) where 1 probe would do.
Not an ADR 0023 budget threat, but pure waste on the most-trafficked new read
path (every order-detail thread open).
Fix: hoist the two names — fetch ContactName + CompanyName once via a single
Order-join-Maker projection before the page query, then project them as
captured locals in the message SELECT (EF parameterizes them; the subqueries
vanish).
Refs: patterns §A.8; ADR 0023 §1; T-0079 AC-10/AC-12.

### [MEDIUM] OrderMessageRepository.cs:48-56 + MarkCustomerOrderMessagesAsRead.cs:16-23, 75-82 — M-2 (commit-boundary doc accuracy)

What: ExecuteUpdateAsync executes immediately in its own implicit transaction;
the Order counter-reset + pointer-clear (handler `:81-82`, maker twin `:67-68`)
commit later via the UoW pipeline SaveChangesAsync. Two transactions, not one
— but the handler XML doc claims the Order side effects happen "in the same
UoW".
Cost (model): zero perf cost — this IS the cheapest correct bulk shape (O(1)
roundtrips vs O(N) fetch+update). The gap is a crash-window between the two
commits leaving messages read but the counter > 0; self-healing because
ResetUnreadFor is unconditional on the next MarkAsRead. The risk is
doc-induced misunderstanding, not runtime cost.
Fix: fold a doc correction stating the two-commit shape + self-healing
property (or, if strict atomicity is wanted, wrap the handler in an explicit
transaction — that call belongs to the reviewer; the perf gate does not
require it).
Refs: CLAUDE.md §Backend rule 4; ADR 0014; T-0079 AC-6.

### [LOW] PostCustomerOrderMessage.cs:95 / PostMakerOrderMessage.cs:88 / MarkAsRead handlers — L-1

Tracked GetByIdForCustomer/MakerAsync loads AutoInclude the Attachments
collection (`OrderConfiguration.cs:204-207`) — a JOIN materializing <=10 child
rows these handlers never read. Bounded and pre-existing design (count-gate
integrity); not worth a new repository variant now. Revisit only if message
posting becomes hot enough to register.

### [LOW] OrderMessageQueries.cs:58, 112 — L-2

Standard two-roundtrip paged contract (COUNT + page). The ownership EXISTS
subquery repeats in both — uncorrelated (parameter-bound), executed once per
statement by Postgres. No action.

### [LOW] OrderMessageQueries.cs:67, 120 — L-3

ThenByDescending(m.Id) tiebreak is outside ix_order_messages_order_created;
incremental sort covers duplicate created_at ties at <=50 rows. No action.

### [LOW] CancelExpiredOrder.cs:80-135 — L-4

Per-row sweep cost ~4 roundtrips (order+attachments JOIN via unscoped tracked
load, user load, language resolve, commit). At 10-200 rows/run on a daily
02:00 UTC schedule this is well inside budget. The fail-continue loop in the
Function means one bad row costs one row, not the sweep. No action.

---

## Index migration assessment

Both new indexes (`ix_order_messages_order_created`,
`ix_order_messages_order_author_unread`) ship **in-bundle** in
`20260609174208_OrderCleanupBundle.cs` and match their query predicates
exactly. **No additional index migration required** — in-bundle or
out-of-bundle. The hypothetical `(state, created_at)` partial composite for
the T-0083 sweep is explicitly NOT needed at MVP cardinality; record it as a
scale-trigger note, not a migration.

## Summary

| Severity | Count | IDs |
|---|---|---|
| BLOCKER | 0 | — |
| HIGH | 0 | — |
| MEDIUM | 2 | M-1 (AuthorName subquery hoist), M-2 (MarkAsRead commit-boundary doc) |
| LOW | 4 | L-1..L-4 |

**Verdict: GATE8_FOLD_RECOMMENDED** — merge-safe; fold M-1 + M-2 in-PR (both
are < 15-line changes), LOWs to backlog.
