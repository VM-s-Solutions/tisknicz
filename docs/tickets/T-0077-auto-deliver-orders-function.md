---
id: T-0077
title: AutoDeliverOrders Function (timer daily 08:00 UTC)
status: ready
size: S
owner: dotnet-backend
created: 2026-06-08
updated: 2026-06-08
depends_on: [T-0076]
blocks: []
user_stories: [US-customer-0013]
adrs: [0014, 0017, 0020]
phase: 4
manual_steps: []
security_touching: false
layers: [appservices, infra-database, infra-functions]
---

# T-0077 — AutoDeliverOrders Function (timer daily 08:00 UTC)

## Context

T-0077 is the **automatic-source half** of the delivery confirmation seam. T-0076 ships the canonical `MarkOrderDelivered.Command(OrderId, Source)` writer (Shipped → Delivered transition + `DeliveredAt` stamp + `delivery_source` stamp + atomic `order.delivered.customerEmail` outbox event under one UoW). T-0077 wires a **daily timer-triggered Function** that finds every Order where the T-0072/T-0073-stamped `AutoDeliverAt` has elapsed (default 7-day window from ADR 0017) and dispatches `MarkOrderDelivered.Command(orderId, OrderDeliverySource.Auto)` for each — one per maker, fail-continue, no human in the loop.

This is the second of three callers feeding the same `MarkOrderDelivered` writer. T-0076 owns the customer-driven path (synchronous `POST /api/v1/customer/orders/{orderId}/deliver` endpoint + `Source = Customer`); T-0078 owns the Packeta-status-poll path (timer every 6h + `Source = Carrier`). The shared writer guarantees uniform state-graph rules, uniform outbox emission, and a single audit column (`delivery_source`) that distinguishes who confirmed each delivery. The bundle ships under one PR (T-0076 + T-0077 + T-0078).

The Function is a **thin MediatR-dispatch wrapper** mirroring the T-0029 `ProcessOutboxFunction` timer pattern verbatim: read a projected list of due Order ids via a new unscoped read-only repository method, loop dispatch, log a structured summary at end of sweep. There is no business logic in the Function itself — the writer (T-0076) owns the state transition, the silent-Success contract handles already-Delivered race-aborts, and the per-Order failure-isolation pattern (fail-continue + per-row Warning log) keeps one bad Order from stalling the whole nightly run.

The sweep is **stateless**. There is no claim table, no "in-flight" marker, and no batch row. If the Function crashes mid-sweep, the next day's tick re-queries the same predicate: Orders that succeeded have transitioned to `Delivered` and no longer match `State == Shipped`; Orders that failed are still `Shipped + AutoDeliverAt-expired` and get retried on the next sweep automatically. This mirrors T-0029's outbox-dispatch resilience and removes the operational burden of cleaning up half-completed batch rows.

T-0077 is purely additive: zero new `BusinessErrorMessage` codes (reuses Order* from T-0060/T-0066), zero new i18n keys (Function output is admin/log-only — the customer surface flows through the `order.delivered.customerEmail` outbox event added by T-0076), zero new outbox event types, zero new controllers, and zero schema changes (T-0076 already added the `delivery_source` column). The only new surface is the Function file + one read-only repository method + the corresponding tests.

## Locked design decisions

Captured per `docs/process/deliberation.md`. T-0077 had **zero user-input dimensions** at `/feature` step 3 — all design choices flowed from precedents already locked at T-0029 (ProcessOutboxFunction timer pattern + structured sweep summary log + fail-continue per-row resilience) and T-0076 (MarkOrderDelivered writer + `OrderDeliverySource.Auto` enum value + silent-Success on re-call + atomic outbox emission).

### A. User-locked at /feature step 3 (non-negotiable)

No user-input dimensions surfaced for T-0077 — all design choices flowed from precedents already locked at T-0029 (ProcessOutboxFunction timer pattern) and T-0076 (MarkOrderDelivered command + Source = Auto).

### B. ADR-locked (no relitigation)

- **ADR 0014 (UoW pipeline + one-file feature shape).** Reuses T-0076's `MarkOrderDelivered.cs` writer verbatim — no new feature file. Function dispatches the existing Command; the `UnitOfWorkPipelineBehavior` commits the Order mutation + outbox row in a single transaction per dispatch.
- **ADR 0017 (shipping / auto-deliver window).** The 7-day `AutoDeliverAt` is stamped at `Order.Ship(...)` (T-0072 Zásilkovna) and `Order.HandOver(...)` (T-0073 PersonalPickup). T-0077 only READS the column. No new fields, no per-shipping-method branching.
- **ADR 0020 (background jobs + Functions discipline).** Timer-triggered Function is a thin MediatR-dispatch wrapper. No business logic in the Function. Schedule lives in app configuration (`AutoDeliverOrders:Schedule`) so ops can tune without a code change. Functions discover via reflection; no DI-registration changes.
- **Per-event-type idempotency + fail-continue per row (per T-0029 precedent).** One Order's failure does NOT block the rest of the batch. Mirror `OutboxDispatcher` mixed-batch handling: each iteration catches `MarkOrderDelivered.Command` failure, logs Warning with OrderId + Error.Code, continues to the next Order id.
- **Stateless re-fetch on partial-run failure (per T-0029 precedent).** No claim table, no in-flight markers. Re-query next sweep; rows that transitioned drop out of the predicate naturally.
- **`Order.State == Shipped AND AutoDeliverAt < now` predicate (per T-0076 lifecycle + ADR 0017).** Disputed orders are not in `Shipped` state, so they're naturally excluded. T-0106's future `Disputed` state introduction does not change this predicate.

### C. PM-absorbed (no user input needed)

- **Timer schedule:** `0 0 8 * * *` (daily 08:00 UTC = 09:00 CET / 10:00 CEST). Per the INDEX line description. Morning aligns with business-hours start in CZ; the resulting customer notification lands in working hours. **Rejected:** earlier (00:00 UTC = 01:00 CET = unwanted overnight emails for customers); later (10:00 UTC = customer is already a few hours into their day). Schedule key: `AutoDeliverOrders:Schedule` for ops tunability.
- **Batch size cap:** **unlimited** at MVP. Per ADR 0023 perf budget, ~10-100 auto-deliverable orders per day fits trivially in one sweep (each dispatch is ~50ms of handler work + a single small outbox row insert). Post-MVP can add a `MaxBatchSize` knob if volume justifies. **Rejected:** arbitrary cap (50, 100) — premature; introduces "what happens to the rest?" semantics that need definition.
- **Idempotency recovery on partial-run failure:** **stateless re-fetch on next sweep.** Mirrors T-0029. Orders that succeed transition to `Delivered` and drop out of the predicate; orders that failed are still `Shipped + AutoDeliverAt-expired` and get retried tomorrow. **Rejected:** persistent batch table (extra schema, extra cleanup logic, no operational benefit at MVP volumes).
- **Batch failure handling:** **fail-continue** per row. Catch `BusinessResult.Failure` per Order; log Warning with OrderId + Error.Code; continue to next Order. One bad row does NOT stall the whole nightly run — critical for production resilience.
- **`Source = OrderDeliverySource.Auto`** injected by the Function (NOT by a Command parameter from the timer trigger payload). The timer trigger has no body; the Function is the only caller that produces `Source.Auto`.
- **Already-Delivered race handling:** if a customer (T-0076) or T-0078 carrier-status sync confirmed delivery between the Function's projection query and the dispatch, T-0076's silent-Success contract makes the dispatch a no-op (returns Success without re-emitting the outbox event). Log at **Information** level "skipped (already delivered)" — do NOT surface as a failure to the batch summary.
- **Logging:** structured **end-of-sweep summary** at Information level (`"AutoDeliverOrders completed: claimed N orders, dispatched M, failed K"`). Per-failure **Warning** with OrderId + Error.Code. No new metric emitter at MVP — ApplicationInsights already ingests structured log fields. **Rejected:** custom `IMetricsEmitter` interface (overengineered for one Function with one summary number).
- **Function shape:** thin MediatR-dispatch wrapper (~30 lines, mirror `ProcessOutboxFunction`). Reads `IOrderRepository.GetAutoDeliverableUnscopedReadOnlyAsync(asOf, ct) → IAsyncEnumerable<string>` and dispatches one Command per yielded OrderId.
- **Repository method shape:** `GetAutoDeliverableUnscopedReadOnlyAsync(DateTimeOffset asOf, CancellationToken ct) → IAsyncEnumerable<string>` — **projection-only** query that selects `Order.Id` and nothing else. The Function dispatches a Command, which loads the Order fresh via T-0076's tracked lookup; we save the Order-graph allocation on the projection side. Predicate: `o => o.State == OrderState.Shipped && o.AutoDeliverAt != null && o.AutoDeliverAt < asOf`. Uses the existing global soft-delete query filter (no `IgnoreQueryFilters` — soft-deleted orders should NOT auto-deliver).
- **AsNoTracking on the projection.** Pure read-only stream of strings; the Function never calls methods on the projected value. Mirrors the T-0074 / T-0075 read-only pattern + the recent Gate 8 fold applied proactively.
- **Dispute handling at MVP:** orders in `Disputed` state are not auto-deliverable; the predicate already filters by `State == Shipped`, which excludes Disputed (and Cancelled, Paid, Accepted, etc.). T-0106 future work does not change this.
- **Test environment timer trigger:** Functions-host test bootstrap uses Microsoft.Azure.Functions.Worker.Testing pattern (DI substitution); per-CI placeholder schedule (CRON only fires in production). Unit tests invoke the Function method directly with a fake `TimerInfo`.

## Scope

### Domain layer

**No changes.** T-0076 already added `OrderDeliverySource` enum + the `delivery_source` column. T-0077 only READS the existing `AutoDeliverAt` column (stamped by T-0072 / T-0073). No new entities, enums, payloads, or BusinessErrorMessage codes.

### AppServices layer

**No changes to `Features/`.** T-0076's `MarkOrderDelivered.cs` writer is reused verbatim. T-0077 dispatches the same Command with `OrderDeliverySource.Auto`; the handler, validator, response shape, and outbox emission are all owned by T-0076.

Optional config (only if not already shipped by T-0076):

- **`Core.AppServices/Common/AutoDeliverOrdersOptions.cs`** (NEW if needed): sealed class with `Schedule { get; init; } = "0 0 8 * * *"` — bound from `AutoDeliverOrders:Schedule` in configuration. Validator enforces non-empty + Azure NCRONTAB-compatible string. Wired via `services.AddOptions<AutoDeliverOrdersOptions>().Bind(...).ValidateOnStart()` per the T-0029 `OutboxQueueOptions` precedent. If T-0076 did NOT introduce this options class, T-0077 adds it.

### Infrastructure / Database layer

- **`Core.Domain/Orders/IOrderRepository.cs`** — add ONE new method:

  ```csharp
  /// <summary>
  /// Projection-only stream of <see cref="Order.Id"/> values for orders
  /// that have crossed their auto-delivery window. Unscoped + read-only
  /// (<c>AsNoTracking</c>): the Function context has no user identity
  /// and only needs the id to dispatch <c>MarkOrderDelivered.Command</c>
  /// per row. Predicate: <c>State == Shipped AND AutoDeliverAt != null
  /// AND AutoDeliverAt &lt; asOf</c>. Soft-deleted rows excluded via the
  /// global query filter (auto-deliver MUST NOT resurrect deactivated
  /// orders). Stream is materialised one row at a time — keeps memory
  /// flat under any batch size.
  /// </summary>
  IAsyncEnumerable<string> GetAutoDeliverableUnscopedReadOnlyAsync(
      DateTimeOffset asOf,
      CancellationToken cancellationToken);
  ```

- **`Infra.Database/Orders/OrderRepository.cs`** — EF impl:

  ```csharp
  public IAsyncEnumerable<string> GetAutoDeliverableUnscopedReadOnlyAsync(
      DateTimeOffset asOf,
      CancellationToken cancellationToken)
      => _dbContext.Set<Order>()
          .AsNoTracking()
          .Where(o => o.State == OrderState.Shipped
                   && o.AutoDeliverAt != null
                   && o.AutoDeliverAt < asOf)
          .OrderBy(o => o.AutoDeliverAt) // stable order — oldest expirations first
          .Select(o => o.Id)
          .AsAsyncEnumerable();
  ```

  Index reuse: the predicate is served by an index on `(state, auto_deliver_at)` if present; if not, the planner falls back to a `state` filter then sequential scan against `auto_deliver_at`. At MVP volumes (~10-100 Shipped orders at any moment) this is acceptable; a follow-up migration can add the composite index if perf monitoring shows it's needed. **Out of scope for T-0077** — index tuning is a Phase-5 concern.

### Infrastructure / Functions layer

- **`Infra.Functions/Delivery/AutoDeliverOrdersFunction.cs`** — NEW timer-triggered Function. Mirror `ProcessOutboxFunction` shape verbatim:

  ```csharp
  public sealed class AutoDeliverOrdersFunction(
      IOrderRepository orderRepository,
      ISender mediator,
      IClock clock,
      ILogger<AutoDeliverOrdersFunction> logger)
  {
      [Function(nameof(AutoDeliverOrdersFunction))]
      public async Task RunTimerAsync(
          [TimerTrigger("%AutoDeliverOrders:Schedule%")] TimerInfo timer,
          CancellationToken cancellationToken)
      {
          var asOf = clock.UtcNow;
          var claimed = 0;
          var dispatched = 0;
          var failed = 0;

          await foreach (var orderId in orderRepository
              .GetAutoDeliverableUnscopedReadOnlyAsync(asOf, cancellationToken)
              .WithCancellation(cancellationToken))
          {
              claimed++;
              try
              {
                  var result = await mediator.Send(
                      new MarkOrderDelivered.Command(orderId, OrderDeliverySource.Auto),
                      cancellationToken);
                  if (result.IsSuccess)
                  {
                      dispatched++;
                  }
                  else
                  {
                      failed++;
                      logger.LogWarning(
                          "AutoDeliverOrders: MarkOrderDelivered failed for order {OrderId}: {Code}",
                          orderId, result.Error!.Code);
                  }
              }
              catch (Exception ex) when (ex is not OperationCanceledException)
              {
                  failed++;
                  logger.LogWarning(ex,
                      "AutoDeliverOrders: unexpected exception for order {OrderId}",
                      orderId);
              }
          }

          logger.LogInformation(
              "AutoDeliverOrders completed: claimed {Claimed} orders, dispatched {Dispatched}, failed {Failed}",
              claimed, dispatched, failed);
      }
  }
  ```

  Notes on shape:
  - **Per-row try/catch with `when (ex is not OperationCanceledException)`** — host shutdown cancellation propagates correctly; all other exceptions are logged + counted as failed + iteration continues. Mirror T-0029's outbox per-row resilience.
  - **No throw on summary failure** — the Function returns normally even if `failed > 0`. Per-row failures are already logged + counted; throwing would replay the whole sweep next minute, which solves nothing (the same Orders would fail again).
  - **`IClock` injection** — production clock at runtime; tests inject a fake clock for deterministic predicate timing.
  - **No `[FixedDelayRetry]`** — timer triggers do not retry per-invocation; the next scheduled tick is the retry. Mirrors T-0029's `ProcessOutboxTimer`.

- **`Infra.Functions/Program.cs`** — no change required. `Microsoft.Azure.Functions.Worker` reflects Functions automatically. DI for `IOrderRepository` + `ISender` + `IClock` is already wired from T-0029 / T-0042 / T-0076.

### Database layer

No EF migrations. No schema changes. T-0076 already added `delivery_source`. The `auto_deliver_at` + `state` columns exist since T-0072 / T-0073. No new index in this ticket (deferred per perf-tuning note above).

### Web host

**No controller.** T-0077 is a Function-only ticket. T-0076 owns the customer-host endpoint; T-0078 is also Function-only.

### Config / DI

- **`local.settings.json`** — add `"AutoDeliverOrders:Schedule": "0 0 8 * * *"` for local-dev parity. Production schedule lives in App Configuration / Key Vault per host environment policy.
- **`host.json`** — no changes (timer triggers honour the standard host settings; no new queue config).
- **No new DI registrations** beyond optional `AddOptions<AutoDeliverOrdersOptions>` binding (only if the options class is added — see AppServices Scope).

### i18n

**No new i18n keys.** The Function never surfaces to user UI. Customer-facing copy flows through the `order.delivered.customerEmail` outbox event added by T-0076 (template + i18n keys ship there).

### NSwag regen

**Not required.** T-0077 introduces no public contract changes. No new controllers, no new endpoints, no new DTOs exposed via OpenAPI. The Function is internal background plumbing.

### Tests

#### AutoDeliverOrdersFunctionTests (NEW, ~4 tests)

`backend/src/Makables.Tests/Functions/Delivery/AutoDeliverOrdersFunctionTests.cs` — NSubstitute mocks (`IOrderRepository`, `ISender`, `IClock`, `ILogger<AutoDeliverOrdersFunction>`).

1. **Happy_path_3_orders_dispatches_3_commands_with_Source_Auto** — `GetAutoDeliverableUnscopedReadOnlyAsync` yields `["order-1", "order-2", "order-3"]`; `ISender.Send` returns `BusinessResult.Success` for each. Assert: `ISender.Send` was called 3 times with `(MarkOrderDelivered.Command, ct)` where each `Command.OrderId` matches the yielded id AND each `Command.Source == OrderDeliverySource.Auto`. Assert: final Information log fires with `Claimed=3, Dispatched=3, Failed=0`.
2. **Fail_continue_on_per_order_failure_does_not_stall_batch** — `GetAutoDeliverableUnscopedReadOnlyAsync` yields `["order-1", "order-2", "order-3"]`; `ISender.Send` returns `Success` for `order-1` and `order-3` but `BusinessResult.Failure(Error.Conflict("state", OrderInvalidTransition))` for `order-2`. Assert: `ISender.Send` was called 3 times (NOT short-circuited at `order-2`); Warning log emitted with `OrderId=order-2, Code=order.invalidTransition`; final summary log `Claimed=3, Dispatched=2, Failed=1`. Per-row exceptions also propagate to the catch branch (separate sub-case: throw `InvalidOperationException` on `order-2` and assert the catch path executes with `Warning + ex`, batch continues, summary reflects 1 failure).
3. **Empty_batch_logs_Information_summary_with_zero_counts** — `GetAutoDeliverableUnscopedReadOnlyAsync` yields nothing (empty async sequence). Assert: `ISender.Send` was NOT called (`Received(0)`); final Information log fires with `Claimed=0, Dispatched=0, Failed=0`; no Warning or Error logs.
4. **Already_delivered_race_is_silent_success_no_op** — `GetAutoDeliverableUnscopedReadOnlyAsync` yields `["order-1"]`; `ISender.Send` returns `BusinessResult.Success` (T-0076's silent-Success contract on already-Delivered re-call). Assert: dispatch count is 1, failed count is 0 (the writer's no-op surfaces as Success to the Function). Document in test comment: "T-0076 silent-Success contract — Function sees Success even though no state mutation occurred."

#### AutoDeliverOrdersIntegrationTests (NEW, ~1 end-to-end test)

`backend/src/Makables.IntegrationTests/Delivery/AutoDeliverOrdersIntegrationTests.cs` — Testcontainers Postgres + real `IOrderRepository` + real `MarkOrderDelivered.Handler` + faked `IOutbox` (or assert against the outbox table directly).

1. **AutoDeliverOrdersFunction_e2e_3_shipped_expired_orders_transition_to_Delivered** — seed Postgres with 3 Orders in state `Shipped` with `AutoDeliverAt = clock.UtcNow.AddDays(-1)` (expired) AND 1 Order in state `Shipped` with `AutoDeliverAt = clock.UtcNow.AddDays(+1)` (NOT expired) AND 1 Order in state `Paid` (wrong state). Invoke `AutoDeliverOrdersFunction.RunTimerAsync` directly with a fake `TimerInfo`. Assert: the 3 expired Shipped orders are now in state `Delivered` with `DeliveredAt = clock.UtcNow` and `DeliverySource = OrderDeliverySource.Auto`. The non-expired Shipped order is unchanged (state still Shipped). The Paid order is unchanged. `outbox_events` table has exactly 3 rows of event type `order.delivered.customerEmail` with `aggregate_id` matching the 3 transitioned orders.

#### OrderRepositoryTests extension (NEW, ~2 tests)

`backend/src/Makables.Tests/Infra/Database/OrderRepositoryTests.cs` (or matching location) — Testcontainers Postgres tests for the new repository method.

1. **GetAutoDeliverableUnscopedReadOnlyAsync_returns_only_Shipped_orders_with_expired_AutoDeliverAt** — seed orders across the full state matrix (Pending, Paid, Accepted, Shipped-expired, Shipped-not-expired, Delivered, Cancelled) plus a soft-deleted Shipped-expired order. Call the method with `asOf = now`. Assert: stream yields exactly the ids of the Shipped-expired non-soft-deleted orders; ordered by `AutoDeliverAt` ascending (oldest first).
2. **GetAutoDeliverableUnscopedReadOnlyAsync_with_null_AutoDeliverAt_excludes_row** — seed a Shipped order with `AutoDeliverAt = null` (defensive — should not exist post-T-0072/T-0073, but the predicate handles it). Call the method. Assert: the null-AutoDeliverAt row is NOT yielded.

### Docs

- **`docs/architecture/roles/order.md`** — extend the Lifecycle table row for `Shipped → Delivered` to note three sources: customer (T-0076 `Source = Customer`), auto-deliver Function (T-0077 `Source = Auto`), and carrier-status sync (T-0078 `Source = Carrier`). All three call the same `MarkOrderDelivered.Command` writer.
- **`docs/tickets/INDEX.md`** — PM flips T-0077 row to `**done**` after PR merge.

## Alternatives Considered

- **Option A — Synchronous in-Handler scan inside `MarkOrderPaid` / `Order.HandOver`.** *Rejected per ADR 0020 background-jobs principle* — coupling auto-deliver semantics to unrelated writer paths creates "what does Paid do? oh it also auto-delivers stale Shipped orders" surprise behaviour. Background timer is the right separation.
- **Option B — Earlier schedule (00:00 UTC / midnight).** *Rejected per §C* — sends customer "your order was delivered" emails at 01:00 CET, which is intrusive and erodes notification trust. Morning landing aligns with working hours.
- **Option C — Later schedule (10:00 UTC).** *Rejected per §C* — customer is several hours into their workday before learning the order was auto-confirmed; loses the "good morning, here's what happened overnight" cadence.
- **Option D — Per-Order claim table (`auto_deliver_claim`) with in-flight markers.** *Rejected per §C + T-0029 precedent* — extra schema, extra cleanup logic, extra failure modes (orphaned claims after crash). Stateless re-fetch on next sweep is operationally simpler and correctness-equivalent because the predicate naturally excludes rows that have already transitioned.
- **Option E — Fail-fast on per-Order failure (throw out of the sweep loop).** *Rejected per §C + T-0029 precedent* — one bad row stalls every subsequent Order in the same batch. Worst case: all of tonight's expirations stalled because one order had a unique-constraint hiccup. Fail-continue keeps the rest of the batch processing.
- **Option F — Hard batch cap (`MaxBatchSize = 50`).** *Rejected per §C* — premature at MVP volumes (~10-100 expirations/day). If the cap is hit, the remaining Orders sit in the predicate for another 24h before retry, delaying maker payout. Unlimited at MVP; revisit if volume warrants.
- **Option G — Bypass the `MarkOrderDelivered` writer and mutate `Order.State` directly inside the Function.** *Rejected per ADR 0014 CQRS discipline* — every state transition goes through a Command + handler so validation pipeline + UoW pipeline + outbox emission stay consistent. Direct mutation would skip the customer email, the audit fields, and any future invariants added to the writer.
- **Option H — Emit a per-sweep "AutoDeliverOrdersCompleted" outbox event for metrics.** *Rejected per §C* — overengineered. ApplicationInsights structured-log ingestion serves the same purpose without the schema + dispatcher routing cost.
- **Option I — Load the full Order graph in the projection (not just the id).** *Rejected per §C + Gate 8 perf fold* — wastes memory + change-tracking allocation. The Function dispatches a Command that loads the Order fresh via T-0076's tracked lookup. Projection-only is the right cost/benefit.
- **Option J — Surface failed sweep summary as a thrown exception so the timer "fails."** *Rejected per §C + T-0029 precedent* — per-row failures are already logged; throwing replays the whole sweep next minute, which would re-fail the same Orders (same data, same broken row). The next scheduled tick is the retry.

## Out of scope

- **`MarkOrderDelivered.Command` writer + outbox event + state-graph transition** — T-0076. T-0077 only dispatches the existing Command.
- **Customer-confirm endpoint** (`POST /api/v1/customer/orders/{orderId}/deliver`) — T-0076.
- **Packeta-status-sync Function** (`Source = Carrier`, polls Packeta `GetStatusAsync` every 6h) — T-0078.
- **`OrderDeliverySource` enum + `delivery_source` column migration** — T-0076.
- **`order.delivered.customerEmail` outbox event + payload + template** — T-0076.
- **Disputed-order skip logic** — out of scope; the `State == Shipped` predicate already excludes Disputed. T-0106 future work changes the dispute writer, not this Function.
- **Per-maker auto-deliver opt-out** — out of scope at MVP. Platform-uniform 7-day window per ADR 0017.
- **Index tuning (`(state, auto_deliver_at)` composite)** — deferred. At MVP volumes the planner handles the predicate adequately; add if perf monitoring shows it's needed.
- **NSwag regen** — no public contract changes.
- **Frontend maker / customer surface for auto-deliver outcome** — out of scope. Customer email (from T-0076's outbox event) is the only customer-facing surface.
- **Admin "force re-run today's sweep" command** — Phase 5+. T-0029-style HTTP-trigger companion can be added then.
- **Admin dashboard metrics** — covered by ApplicationInsights structured-log ingestion at MVP; dedicated dashboard is Phase 5+.

## Acceptance criteria

- **AC-1** Given an `Order` in state `Shipped` with `AutoDeliverAt = clock.UtcNow - 1 day`, when `AutoDeliverOrdersFunction.RunTimerAsync` runs, then it dispatches `MarkOrderDelivered.Command(orderId, OrderDeliverySource.Auto)` exactly once. After the dispatch the Order is in state `Delivered` with `DeliveredAt = clock.UtcNow` AND `DeliverySource = OrderDeliverySource.Auto`.
- **AC-2** Given the sweep dispatches N successful `MarkOrderDelivered` commands, when the Function returns, then exactly N rows exist in `outbox_events` with `event_type = "order.delivered.customerEmail"` and `aggregate_id` matching each transitioned `OrderId`. Each payload deserializes to T-0076's `OrderDeliveredCustomerEmailPayload` with all fields populated.
- **AC-3** Given an `Order` in state `Shipped` with `AutoDeliverAt = clock.UtcNow + 1 day` (not yet expired), when the Function runs, then the Order is NOT in the projection stream AND `ISender.Send` is NOT called for that OrderId. The Order remains in state `Shipped` after the sweep.
- **AC-4** Given Orders in states other than `Shipped` (e.g., `Pending`, `Paid`, `Accepted`, `Delivered`, `Cancelled`) regardless of `AutoDeliverAt`, when the Function runs, then those Orders are NOT in the projection stream AND `ISender.Send` is NOT called for them.
- **AC-5** Given a soft-deleted Order (`Auditable.DeactivatedOn` set) in state `Shipped` with `AutoDeliverAt` expired, when the Function runs, then the Order is NOT in the projection stream (global query filter excludes it) AND no auto-deliver dispatch fires.
- **AC-6** Given a batch of 3 Orders where the middle Order's `MarkOrderDelivered.Command` returns `BusinessResult.Failure(Error.Conflict("state", OrderInvalidTransition))`, when the Function runs, then it dispatches Commands for ALL 3 Orders (NOT short-circuited at the failure). Warning log fires for the failed Order with structured fields `OrderId` + `Code`. Summary Information log reports `Claimed=3, Dispatched=2, Failed=1`.
- **AC-7** Given a batch of 3 Orders where the middle Order's `ISender.Send` throws an unexpected `InvalidOperationException`, when the Function runs, then it catches the exception (excluding `OperationCanceledException`), logs Warning with the exception + OrderId, continues to the next Order, dispatches the third Order's Command, and reports `Claimed=3, Dispatched=2, Failed=1`.
- **AC-8** Given an empty projection stream (no Orders match the predicate), when the Function runs, then `ISender.Send` is NOT called and the final Information summary log fires with `Claimed=0, Dispatched=0, Failed=0`. No Warning or Error logs are emitted.
- **AC-9** Given an Order already in state `Delivered` between the projection query and the Command dispatch (race with T-0076 customer-confirm or T-0078 carrier-sync), when the Command runs, then T-0076's silent-Success contract returns `Success` without re-emitting the outbox event. The Function counts the dispatch as successful and the sweep summary reflects no failure.
- **AC-10** Given `IOrderRepository.GetAutoDeliverableUnscopedReadOnlyAsync(asOf, ct)`, when called against Postgres seed data, then it yields `Order.Id` values for rows matching `State == Shipped AND AutoDeliverAt != null AND AutoDeliverAt < asOf`, in ascending `AutoDeliverAt` order, with `AsNoTracking` applied (no change-tracking overhead). The stream materialises one id at a time (`IAsyncEnumerable<string>` — not buffered).
- **AC-11** Given the Function's timer trigger configuration, when the host loads `host.json` + app settings, then the schedule resolves from `%AutoDeliverOrders:Schedule%` (default `"0 0 8 * * *"` = daily 08:00 UTC). Build clean. Unit tests: baseline (after T-0076 in the same bundle) + ~6 new (4 `AutoDeliverOrdersFunctionTests` + 2 `OrderRepositoryTests`). Integration tests: baseline + 1 new (`AutoDeliverOrdersIntegrationTests`).
- **AC-12** Consistency script exit 0 (no new T1–T7 violations vs the bundle's running baseline). Zero new `BusinessErrorMessage` codes (reuses Order* from T-0060/T-0066). Zero new i18n keys. Zero new outbox event types. Zero new controllers. Zero schema changes (T-0076 already added `delivery_source`).
- **AC-13** Handler-side discipline: `AutoDeliverOrdersFunction` does NOT call `SaveChangesAsync` (per ADR 0014 — T-0076's UoW pipeline commits per-dispatch). The Function does NOT contain Order state-transition logic (per ADR 0020 — thin wrapper). Verified by grep: zero `SaveChangesAsync` occurrences in `AutoDeliverOrdersFunction.cs`; zero `Order.MarkAsDelivered` / `OrderState.Delivered` references in the Function file.

## Technical notes

### Why projection-only repository method (not full Order graph)

T-0076's `MarkOrderDelivered.Handler` loads the Order fresh via its own tracked lookup (per the writer's UoW + change-tracking contract). If T-0077's projection returned the full Order graph, the Function would either (a) re-load the Order in the Handler (wasted work — the writer needs a tracked instance anyway) or (b) pass the untracked Order into the Command (bypasses the writer's tracking contract). Returning just the `OrderId` is the minimal contract that keeps the Function decoupled from the writer's internal lookup strategy. Mirrors the recent Gate 8 fold (read-only lookups use AsNoTracking; mutating handlers re-load through the tracked path).

### Why stateless re-fetch on partial-run failure (no claim table)

T-0029 established the pattern: the predicate IS the claim. Rows that succeed transition out of the predicate; rows that fail stay in the predicate for the next sweep. Adding a claim table would create new failure modes (orphaned claims after crash; claim row leaked into production data; cleanup logic that itself can fail). The auto-deliver predicate is naturally idempotent because the state transition is one-way (Shipped → Delivered, no reverse), so a row that succeeds will never re-match. T-0029 ProcessOutboxFunction follows this pattern verbatim.

### Why fail-continue (per-row try/catch) instead of fail-fast

One bad Order (e.g., a unique-constraint violation due to a concurrent write, or a Comgate webhook racing the auto-deliver dispatch) MUST NOT stall the rest of tonight's expirations. Fail-fast would mean: a single bad row blocks ~50 makers from getting paid tomorrow. Fail-continue is the production-resilience pattern; the failed row is logged (Warning + OrderId + Code) and gets retried on the next sweep automatically. The summary log makes per-night failure visibility easy to grep.

### Why `Source = OrderDeliverySource.Auto` is injected by the Function (not by the Command parameter from the trigger payload)

Timer triggers have no payload. The Function is the only caller that produces `Source = Auto` (T-0076 produces `Customer`; T-0078 produces `Carrier`). Hard-coding the `Source` in the Function call site keeps the writer's contract clean: every caller MUST declare the source, and the source's correctness is verifiable by reading the call site. Threading the source through a serialized timer-payload would introduce an unnecessary intermediate format.

### Why silent-Success on already-Delivered race (no failure surface)

Three writers feed `MarkOrderDelivered`. Between the projection query and the dispatch, another writer (a fast customer confirming on the app, or a Packeta status sync) may have transitioned the Order. T-0076's silent-Success contract makes this a Success no-op (no state change, no outbox event). The Function should NOT treat this as a failure — it's the expected behavior of a concurrent writer. Logging it as Information ("skipped: already delivered") is fine; surfacing it as Warning / Failed would create noise that distracts from real failures.

### Why no Schedule HTTP-trigger companion (unlike T-0029)

T-0029's HTTP trigger exists because the outbox is the system-of-record for cross-cutting events and admin "force retry" semantics matter operationally. Auto-deliver is a slow business-cycle process (24h); manual triggering during the wait window has no operational value. If a future ticket needs admin-force-run semantics (e.g., for debugging or backfill), the HTTP-trigger companion can be added then.

### Why the 7-day window lives on the Order row (not in app config)

The window is stamped at ship time (T-0072 / T-0073) per ADR 0017. This means: (a) historical orders preserve their original window if the platform-default ever changes; (b) per-shipping-method variations (already in T-0073's HandOver path) are honored automatically; (c) T-0077 reads a row column instead of branching on shipping method. Future per-maker variations would extend the writer, not the Function.

### Why no index migration in this ticket

The predicate is `(state, auto_deliver_at)`. At MVP volumes (~10-100 Shipped orders at any moment, sweep runs once per day), Postgres handles it adequately with the existing `state` index + a sequential pass on `auto_deliver_at`. Adding a composite index now is premature optimization; perf monitoring during MVP will surface whether it's needed. If yes, a follow-up migration adds it without code changes to T-0077.

## Files touched (expected)

### New

- `backend/src/Makables.Infra.Functions/Delivery/AutoDeliverOrdersFunction.cs`
- `backend/src/Makables.Tests/Functions/Delivery/AutoDeliverOrdersFunctionTests.cs`
- `backend/src/Makables.IntegrationTests/Delivery/AutoDeliverOrdersIntegrationTests.cs`

### Modified (domain)

- `backend/src/Makables.Core.Domain/Orders/IOrderRepository.cs` — add `GetAutoDeliverableUnscopedReadOnlyAsync(DateTimeOffset asOf, CancellationToken ct) → IAsyncEnumerable<string>` with XML doc per the Scope shape.

### Modified (infra)

- `backend/src/Makables.Infra.Database/Orders/OrderRepository.cs` — implement `GetAutoDeliverableUnscopedReadOnlyAsync` per the Scope shape (`AsNoTracking + Where(state == Shipped && AutoDeliverAt != null && AutoDeliverAt < asOf) + OrderBy(AutoDeliverAt) + Select(Id) + AsAsyncEnumerable`).

### Modified (tests)

- `backend/src/Makables.Tests/Infra/Database/OrderRepositoryTests.cs` (or matching location) — 2 new tests for the new repository method.

### Modified (config — only if not already shipped by T-0076)

- `backend/src/Makables.Core.AppServices/Common/AutoDeliverOrdersOptions.cs` — sealed class with `Schedule` property (default `"0 0 8 * * *"`) bound from `AutoDeliverOrders:Schedule`. Validator + `ValidateOnStart` per T-0029 `OutboxQueueOptions` precedent. Skip if T-0076 already shipped it.
- `backend/src/Makables.Infra.Functions/local.settings.json` — add `"AutoDeliverOrders:Schedule": "0 0 8 * * *"` (dev parity; production schedule via App Configuration).

### Modified (docs)

- `docs/architecture/roles/order.md` — extend Lifecycle row `Shipped → Delivered` to list three sources (customer T-0076, auto T-0077, carrier T-0078) calling the same `MarkOrderDelivered.Command` writer.
- `docs/tickets/INDEX.md` — PM flips T-0077 row to `done` after PR merge.

## Test plan reference

Inline above (see Scope > Tests). No separate `docs/test-plans/T-0077.md`.

## Status log

- 2026-06-08 `draft` by PM. Created from delivery-close bundle plan (T-0076 + T-0077 + T-0078). Reference precedents: T-0029 ProcessOutboxFunction (timer trigger + fail-continue + structured sweep summary log) merged; T-0076 (MarkOrderDelivered writer + OrderDeliverySource.Auto enum value + silent-Success on already-Delivered re-call) in the same bundle PR. Slice scope: thin MediatR-dispatch Function reading a new projection-only read-only repository method; daily 08:00 UTC timer; fail-continue per-row; stateless re-fetch on partial-run failure. Zero new BusinessErrorMessage codes, zero new i18n keys, zero new outbox event types, zero new controllers, zero schema changes.
- 2026-06-08 `draft → ready` by PM. Zero blocking AskUserQuestion items surfaced at `/feature` step 3 — all design choices flowed from T-0029 (Function shape, MediatR-dispatch wrapper, fail-continue per-row, stateless re-fetch on partial-run failure, structured summary log) and T-0076 (MarkOrderDelivered writer + OrderDeliverySource.Auto + silent-Success on already-Delivered re-call). 11 PM-absorbed decisions captured in `## Locked design decisions §C` (daily 08:00 UTC schedule + rationale vs earlier/later alternatives; unlimited batch size at MVP; stateless re-fetch; fail-continue with per-row Warning log + structured summary; Source.Auto injection at Function call site; silent-Success race handling; thin MediatR-dispatch shape; projection-only IAsyncEnumerable<string> repository method; AsNoTracking discipline; Disputed naturally excluded by predicate; test-environment timer placeholder). 6 ADR-locked items extracted in §B (ADR 0014 UoW + one-file feature reuse; ADR 0017 7-day window stamped at ship time; ADR 0020 thin Function wrapper + reflection discovery; T-0029 per-row resilience + stateless re-fetch precedents; predicate naturally excludes Disputed). Zero `manual_steps` (timer schedule needs no manual step beyond standard App-Configuration deployment). **Ready for dotnet-backend.** The implementer processes T-0076 → T-0077 → T-0078 sequentially in the same branch; all three ship in one PR.
