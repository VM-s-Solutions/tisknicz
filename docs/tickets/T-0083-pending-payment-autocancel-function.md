---
id: T-0083
title: CancelExpiredPendingPaymentOrders Function (timer daily 02:00 UTC)
status: ready
size: S
owner: dotnet-backend
created: 2026-06-09
updated: 2026-06-09
depends_on: [T-0066, T-0067]
blocks: []
user_stories: [US-customer-0010]
adrs: [0017, 0019, 0020, 0023]
phase: 4
manual_steps: []
security_touching: false
layers: [domain, appservices, infra-database, infra-functions]
---

# T-0083 — CancelExpiredPendingPaymentOrders Function (timer daily 02:00 UTC)

## Context

T-0083 closes the **order-lifecycle cleanup gap** at the top of the state graph. T-0066 creates an Order in `OrderState.PendingPayment` and hands the customer off to Comgate; T-0067 transitions PendingPayment → Paid on a verified webhook. The gap: customers who **never complete payment** — drop off the Comgate flow, abandon the browser, or simply ignore the email retry — leave their Order stuck in `PendingPayment` indefinitely. US-customer-0010 AC-3 names a 24h Comgate retry window after which the payment intent is dead; without a cleanup sweep, the Order row stays in a non-terminal state, occupies stock reservations, and clouds the customer's dashboard.

T-0083 is a **mirror of T-0077's AutoDeliverOrders Function pattern** applied to the payment-expiry seam. Both tickets share the exact same shape: a daily timer-triggered Function reads a projection-only stream of Order ids from a new unscoped read-only repository method, dispatches one MediatR Command per row, logs a structured end-of-sweep summary, and survives partial failure via per-row fail-continue. The Function itself is a thin scheduler-wrapper per ADR 0020 — the actual state transition + outbox emit lives in a new one-file feature `CancelExpiredOrder.cs` so the writer is independently testable and reusable.

The auto-cancel transition emits a new outbox event `order.cancelled.customerEmail` consistent with the established outbox-via-email pattern from T-0067 (mark paid), T-0071 (accept), and T-0076 (delivered). The customer gets a "your order expired, payment not received" notification; the maker gets **nothing** because the maker never knew about the order at `PendingPayment` state (T-0071's maker-accept email fires on transition into `Accepted`, not before). This keeps the maker inbox quiet — no "an order you never saw was just cancelled" noise.

The TTL window is **24 hours** from `Order.CreatedAt`, matching US-customer-0010 AC-3's 24h Comgate retry window and the INDEX row description. No per-country variation at MVP; if/when a 2nd country materially differs, a `CountryConfiguration.PendingPaymentTtlHours` column can be added without code changes to the Function. The schedule is **daily at 02:00 UTC** — off-peak, and deliberately not aligned with T-0077's morning AutoDeliver (08:00 UTC) so the two cleanup jobs spread load across the day.

T-0083 is purely additive at the contract surface: zero new public HTTP endpoints, zero new controllers, zero NSwag regen. The new outbox event type + cs-CZ email template + i18n keys ship inside this ticket so the customer-facing copy lands atomically with the writer. The state machine gets one new transition (`PendingPayment → Cancelled` via `Order.Cancel(OrderCancellationSource.AutoExpiry)`) plus a small auxiliary `OrderCancellationSource` enum on the domain side; both surface a future T-0107 admin-manual-cancel path and a potential T-0105/T-0106 customer-side cancel without re-litigating the writer.

## Locked design decisions

Captured per `docs/process/deliberation.md`. The user locked 4 dimensions at `/feature` step 3 (TTL = 24h matching AC-3; outbox-emit customer-only on auto-cancel; Function dispatches MediatR Command; schedule daily 02:00 UTC). The remaining decisions follow from T-0077 (AutoDeliverOrders Function shape), T-0029 (ProcessOutboxFunction timer + fail-continue), and T-0067/T-0071/T-0076 (outbox-via-email pattern) precedents.

### A. User-locked at /feature step 3 (non-negotiable)

1. **TTL window = 24 hours from `Order.CreatedAt`.** Matches US-customer-0010 AC-3's 24h Comgate retry window and the INDEX row description. Sweep predicate: `State == PendingPayment AND CreatedAt < asOf - 24h`. **No per-country variation at MVP** — `CountryConfiguration.PendingPaymentTtlHours` can be added later when a 2nd country materially differs. **Rejected:** shorter window (e.g., 6h or 12h — strands customers who pay end-of-day on a delayed bank transfer); longer window (e.g., 72h — extends stock-reservation overhang for no UX gain when Comgate intent is dead after 24h anyway).
2. **Emit `order.cancelled.customerEmail` outbox event on auto-cancel.** Customer gets "your order expired, payment not received" notification. Consistent with T-0067 (mark paid), T-0071 (accept), T-0076 (delivered) outbox-via-email pattern. **NO maker email** — the maker never knew about the order at `PendingPayment` state (T-0071's maker-accept email fires on `PaymentReceived → Accepted`, not at order creation). **Rejected:** silent cancel with no customer notification (US-customer-0010 expects closure feedback); maker email too (creates "an order you never saw was just cancelled" noise + reveals customer flake-out behaviour the maker doesn't need to see).
3. **Function dispatches a MediatR Command (`CancelExpiredOrder.Command(orderId)`); the Command owns the state transition + outbox emit.** Function is a **thin scheduler-wrapper only** per ADR 0020 — no business logic in the Function. Mirrors T-0077 verbatim. **Rejected:** Function calls the domain method directly (skips Validator + UoW pipeline + outbox-on-commit contract); Function emits the outbox event itself (couples the timer trigger to the outbox shape; breaks separation of concerns).
4. **Schedule = daily at 02:00 UTC** (`0 0 2 * * *`). Off-peak; deliberately not aligned with T-0077's morning AutoDeliver (08:00 UTC) so the two cleanup jobs spread load across the day. Schedule key: `CancelExpiredPendingPaymentOrders:Schedule` for ops tunability. **Rejected:** hourly (overkill at MVP volumes — ~24h TTL doesn't care about hour-level granularity); same time as AutoDeliver (load spike + log-channel contention at 08:00 UTC); business-hours (intrusive customer email timing in late afternoon CET).

### B. ADR-locked (no relitigation)

- **ADR 0017 (outbox eventing).** New outbox event type `order.cancelled.customerEmail` follows the established naming convention (`<entity>.<verb>.<channel>`). Emission is **atomic with the state transition** under the UoW pipeline behavior — the Order row update and the `outbox_events` insert commit in one transaction. Idempotent re-fire is guaranteed by the outbox dispatcher's `provider_ref` check (T-0029 precedent); a second sweep that re-runs the same Command before the dispatcher fires would skip via the new feature's Silent Success contract on already-Cancelled state, so the outbox row is written at most once per Order.
- **ADR 0019 (email-send service routing).** `EmailSendService` extends with a routing branch for `OrderCancelledCustomerEmail`. The handler loads the Order, inspects the `CancellationSource` (AutoExpiry vs Customer vs Admin), and selects the appropriate template + i18n copy. T-0083 ships **only the AutoExpiry copy + template**; Customer / Admin source copy lives in T-0105 / T-0107 respectively but the branch shape is established here so those tickets are pure extensions.
- **ADR 0020 (background jobs + Functions discipline).** Timer-triggered Function is a thin MediatR-dispatch wrapper. No business logic in the Function. Schedule lives in app configuration (`CancelExpiredPendingPaymentOrders:Schedule`) so ops can tune without a code change. **Q-0008 MARS workaround applied** (per the delivery-close precedent) — materialize the projection stream to `List<string>` BEFORE the per-row `mediator.Send` loop, so the outer reader is fully drained before any handler opens its own connection. Mirrors the T-0077 + T-0029 + Gate 8 fold.
- **ADR 0023 (NFRs: read-side AsNoTracking + projection-only).** New repository method is **projection-only** — selects `Order.Id` and nothing else. `AsNoTracking()` for the read-only stream. Soft-deleted rows excluded by the global Auditable query filter (no `IgnoreQueryFilters`). Index on `(state, created_at)` is verified during implementation; if missing, a follow-up migration adds it (deferred per T-0077's index-tuning precedent — at MVP volumes the planner handles the predicate adequately).

### C. PM-absorbed (no user input needed)

- **Function shape:** thin MediatR-dispatch wrapper (~30 lines, mirror T-0077's `AutoDeliverOrdersFunction` verbatim). Primary constructor DI: `IOrderRepository orderRepository, ISender mediator, IClock clock, ILogger<CancelExpiredPendingPaymentOrdersFunction> logger`. `RunAsync` method with `[TimerTrigger("%CancelExpiredPendingPaymentOrders:Schedule%")]`. Per-row try/catch with fail-continue; structured end-of-sweep summary log (`claimed`, `dispatched`, `failed`).
- **Q-0008 MARS workaround:** materialize the streaming projection to `List<string>` BEFORE the per-row `mediator.Send` loop. The async-enumerable iteration holds the outer reader open; opening a fresh DbContext per handler dispatch inside that loop trips MARS on Npgsql. Materialize-then-loop is the locked T-0077 precedent and applies verbatim here.
- **Repository method shape:** `GetExpiredPendingPaymentUnscopedReadOnlyAsync(DateTimeOffset asOf, CancellationToken ct) → IAsyncEnumerable<string>` — projection-only stream. Predicate: `o => o.State == OrderState.PendingPayment && o.CreatedAt < asOf.AddHours(-24)`. `.AsNoTracking()`. `ORDER BY CreatedAt ASC` (oldest expirations first — same stable-ordering rationale as T-0077's `OrderBy(AutoDeliverAt)`).
- **One-file feature shape:** `Core.AppServices/Features/Orders/CancelExpiredOrder.cs` with nested `Command(string OrderId)` + `Validator` (OrderId.NotEmpty) + `Handler` + `CancelExpiredOrderResponse`. Per ADR 0014 one-file-per-use-case convention.
- **Handler steps (read-write):**
  1. **Load Order** via `IOrderRepository.GetByIdUnscopedAsync(orderId)`. The Function context has no user identity; unscoped lookup is mandated. If null → return `BusinessResult.Failure(Error.NotFound("order", OrderNotFound))`.
  2. **State guard (Silent Success contract):** if `order.State != OrderState.PendingPayment` → return `BusinessResult.Success(new CancelExpiredOrderResponse(orderId, order.State))` without mutation. Covers the customer-pays-mid-flight race + the double-cancel race + the already-Cancelled re-fire scenario. Mirrors T-0076's silent-Success-on-already-Delivered contract.
  3. **Transition** via `order.Cancel(OrderCancellationSource.AutoExpiry)` (domain method — see below).
  4. **Emit outbox event** `OrderCancelledCustomerEmail` with payload `OrderCancelledCustomerEmailPayload(OrderId, OrderNumber, CustomerEmail, Reason: "AutoExpiry")`. UoW pipeline commits the state transition + outbox row in a single transaction.
  5. **Return** `BusinessResult.Success(new CancelExpiredOrderResponse(orderId, OrderState.Cancelled))`.
- **Domain method:** `Order.Cancel(OrderCancellationSource source)`. T-0083 verifies during implementation whether T-0067/T-0071/T-0076 already introduced this method on the Order aggregate. **If not, this ticket adds it** (call out in the deviation log on the PR); if yes, T-0083 extends the existing implementation with the `OrderState.PendingPayment → Cancelled` transition (Customer source is already covered if extant; AutoExpiry source is new). Domain invariant: from `PendingPayment` the only valid `Cancel` sources are `AutoExpiry`, `Customer`, and `Admin`; from any other state the method throws `InvalidOrderTransitionException` (existing per state-machine convention).
- **`OrderCancellationSource` enum:** new at `Core.Domain/Orders/OrderCancellationSource.cs`. Values: `Customer = 0`, `AutoExpiry = 1`, `Admin = 2`. Explicit numeric wire codes. Persisted on the Order entity as `CancellationSource` (nullable until the Order actually cancels) alongside an optional `CancelledAt` timestamp (set by `Order.Cancel`).
- **Outbox event type constant:** new `OutboxEventType.OrderCancelledCustomerEmail = "order.cancelled.customerEmail"`. Payload type `OrderCancelledCustomerEmailPayload(string OrderId, string OrderNumber, string CustomerEmail, string Reason)` at `Core.Domain/Outbox/Payloads/OrderCancelledCustomerEmailPayload.cs`. `Reason` carries the stringified `OrderCancellationSource` for the email-routing branch.
- **EmailSendService extension:** `IsOrderCancelled` predicate routes `OrderCancelledCustomerEmail` events; the handler loads the Order (or re-deserializes the payload), renders the email per AutoExpiry vs Customer source from the cs-CZ template. T-0083 ships AutoExpiry-source copy only; Customer / Admin variants are extension points for T-0105 / T-0107.
- **Email template + cs-CZ i18n keys** (NEW):
  - `email.orderCancelled.autoExpiry.subject` — "Vaše objednávka {orderNumber} byla zrušena (platba neproběhla)".
  - `email.orderCancelled.autoExpiry.body` — "Bohužel jsme neobdrželi platbu za objednávku {orderNumber} během 24 hodin od jejího vytvoření. Objednávka byla automaticky zrušena. Pokud máte zájem, můžete si ji znovu objednat na {checkoutLink}." (final copy lives in the template; the i18n keys are the contract).
- **Function host registration:** `Makables.Functions/Program.cs` adds the new Function via existing `AddMakables*` extensions. No new DI registrations beyond the optional `AddOptions<CancelExpiredPendingPaymentOrdersOptions>` binding for the schedule. Default `0 0 2 * * *` (daily 02:00 UTC).
- **Logging:** structured **end-of-sweep summary** at Information level (`"CancelExpiredPendingPaymentOrders completed: claimed N orders, dispatched M, failed K"`). Per-failure **Warning** with OrderId + Error.Code. Per-row exceptions caught with `when (ex is not OperationCanceledException)` — host shutdown cancellation propagates; all other exceptions are logged + counted as failed + iteration continues.
- **No new BusinessErrorMessage codes.** The Handler's only domain-failure mode is `OrderNotFound` (existing constant from T-0060/T-0066). Validator failures (OrderId empty) surface via the existing FluentValidation envelope.

## Acceptance criteria

- **AC-1** Given an `Order` in state `PendingPayment` with `CreatedAt = clock.UtcNow - 25 hours`, when `CancelExpiredPendingPaymentOrdersFunction.RunAsync` runs, then it dispatches `CancelExpiredOrder.Command(orderId)` exactly once. After dispatch the Order is in state `Cancelled` with `CancellationSource = OrderCancellationSource.AutoExpiry` AND `CancelledAt = clock.UtcNow`.
- **AC-2** Given the sweep dispatches N successful `CancelExpiredOrder` commands, when the Function returns, then exactly N rows exist in `outbox_events` with `event_type = "order.cancelled.customerEmail"` and `aggregate_id` matching each transitioned `OrderId`. Each payload deserializes to `OrderCancelledCustomerEmailPayload` with `Reason = "AutoExpiry"` and all required fields populated.
- **AC-3** Given an `Order` in state `PendingPayment` with `CreatedAt = clock.UtcNow - 12 hours` (within TTL), when the Function runs, then the Order is NOT in the projection stream AND `ISender.Send` is NOT called for that OrderId. The Order remains in state `PendingPayment` after the sweep.
- **AC-4** Given Orders in states other than `PendingPayment` (e.g., `Paid`, `Accepted`, `Shipped`, `Delivered`, `Cancelled`) regardless of `CreatedAt`, when the Function runs, then those Orders are NOT in the projection stream AND `ISender.Send` is NOT called for them. The Silent Success contract additionally guarantees that if a Command is dispatched and the Order has already moved out of `PendingPayment` (customer-pays-mid-flight race), the handler returns Success without mutation.
- **AC-5** Given a soft-deleted Order (`Auditable.DeactivatedOn` set) in state `PendingPayment` with `CreatedAt` expired, when the Function runs, then the Order is NOT in the projection stream (global query filter excludes it) AND no auto-cancel dispatch fires.
- **AC-6** Given a batch of 3 Orders where the middle Order's `CancelExpiredOrder.Command` returns `BusinessResult.Failure`, when the Function runs, then it dispatches Commands for ALL 3 Orders (NOT short-circuited at the failure). Warning log fires for the failed Order with structured fields `OrderId` + `Code`. Summary Information log reports `Claimed=3, Dispatched=2, Failed=1`.
- **AC-7** Given an empty projection stream (no Orders match the predicate), when the Function runs, then `ISender.Send` is NOT called and the final Information summary log fires with `Claimed=0, Dispatched=0, Failed=0`. No Warning or Error logs are emitted.
- **AC-8** Given the `CancelExpiredOrder.Handler` is dispatched for an Order whose state has already transitioned to `Cancelled` between projection and dispatch (concurrent T-0105 customer-cancel or T-0107 admin-cancel), when the Command runs, then the Silent Success contract returns `Success` without re-emitting the outbox event AND without re-mutating the Order. The Function counts the dispatch as successful and the sweep summary reflects no failure.
- **AC-9** Given the timer trigger configuration, when the Functions host loads `host.json` + app settings, then the schedule resolves from `%CancelExpiredPendingPaymentOrders:Schedule%` (default `"0 0 2 * * *"` = daily 02:00 UTC). Build clean. The schedule is independently configurable from T-0077's `AutoDeliverOrders:Schedule`.
- **AC-10** Given `IOrderRepository.GetExpiredPendingPaymentUnscopedReadOnlyAsync(asOf, ct)`, when called against Postgres seed data, then it yields `Order.Id` values for rows matching `State == PendingPayment AND CreatedAt < asOf.AddHours(-24)`, ordered by `CreatedAt` ascending (oldest first), with `AsNoTracking()` applied. The stream materialises to `List<string>` before per-row dispatch (Q-0008 MARS workaround).

## Out of scope

- **Per-country TTL variation.** No `CountryConfiguration.PendingPaymentTtlHours` at MVP. Hard-coded 24h on the predicate (`asOf.AddHours(-24)`). Re-evaluate when a 2nd country materially differs.
- **Maker email on auto-cancel.** Per A.2 — maker never knew about the order at `PendingPayment` state; no email surface.
- **Admin manual-cancel endpoint and copy variant.** T-0107 owns the admin-source path (`OrderCancellationSource.Admin`) — controller, authorization, copy template extension, and integration with this Function's writer. T-0083 introduces the enum value as an extension point only.
- **Customer manual-cancel endpoint.** T-0105 / T-0106 own the customer-source path (`OrderCancellationSource.Customer`) and any refund-on-cancel flow.
- **Refund flow on auto-cancel.** Out of scope — at `PendingPayment` no payment has cleared, so there's nothing to refund. The customer-pays-mid-flight race (payment clears AFTER the auto-cancel fires) is handled by the Comgate webhook handler (T-0067), which on receiving a paid signal for a now-Cancelled Order must dispute or refund per T-0105/T-0106 follow-on tickets.
- **Stock-reservation release semantics.** The Order's link to product inventory is not in T-0083's scope; the stock-reservation seam (if/when introduced) will hook into the writer via a domain event rather than ship inside this Function.
- **`(state, created_at)` composite index migration.** Verify-only during implementation; if missing, follow-up migration adds it. T-0083 ships the predicate; index tuning is a Phase-5 perf concern per T-0077 precedent.
- **NSwag regen.** No public contract changes. Function is internal background plumbing.
- **Frontend cancel-state UI / customer dashboard "Cancelled" tab.** Frontend concern handled in the customer-orders bundle (T-0080 list / T-0087 page); the `Cancelled` state value already serializes through `OrderState` enum on the wire.

## Risk / mitigation

- **Risk: double-cancel race** — Function reads OrderId at T=0 and dispatches Command at T=10ms; in between, T-0105 customer-cancel transitions the Order to `Cancelled`. **Mitigation:** Silent Success contract — handler checks `order.State != OrderState.PendingPayment` and returns Success no-op without re-mutating state or re-emitting the outbox event (AC-8). Mirrors T-0076's already-Delivered silent-Success pattern.
- **Risk: customer-pays-mid-flight race** — Comgate webhook lands AFTER the auto-cancel fires. The Order transitions PendingPayment → Cancelled, then a `payment.paid` webhook arrives for the same Order. **Mitigation:** Comgate webhook handler (T-0067) on receiving a paid signal for a now-Cancelled Order must dispute or refund per T-0105/T-0106 follow-on tickets. T-0083's Function does NOT need to defend against this — it cancels orders that are still `PendingPayment` at sweep time; what happens after is the webhook's problem. Document the expected handoff in the writer's XML doc.
- **Risk: outbox dispatcher double-fires the email** — if the Function crashes after committing the Order transition but before the outbox row is processed, the next sweep won't re-emit (Order is already `Cancelled`, Silent Success kicks in). If the outbox dispatcher itself retries, `provider_ref` idempotency (T-0029) prevents duplicate sends. **Mitigation:** existing — no new code required in T-0083.
- **Risk: Function holds DbConnection across per-row Command dispatches (MARS violation)** — async-enumerable iteration would keep the outer reader open. **Mitigation:** Q-0008 MARS workaround per ADR 0020 — materialize projection to `List<string>` before the loop. Locked in §C.

## Touched layers

- **backend:**
  - new Function `Makables.Functions/Payments/CancelExpiredPendingPaymentOrdersFunction.cs`,
  - new one-file feature `Core.AppServices/Features/Orders/CancelExpiredOrder.cs`,
  - new repository method on `IOrderRepository` + `OrderRepository`,
  - new enum `OrderCancellationSource`,
  - new domain method `Order.Cancel(OrderCancellationSource)` (or extension of existing if T-0067/T-0071/T-0076 introduced it — verify during implementation),
  - new outbox event type constant + payload record,
  - extension of `EmailSendService` routing with `IsOrderCancelled` branch,
  - new cs-CZ email template + i18n keys.
- **contract:** NONE — no new HTTP endpoint, no NSwag regen.
- **docs:** this ticket; PM flips INDEX.md row to `done` post-merge.

## Security-touching

**NO.** No auth surface change. No new public endpoint. Function admin auth flows through the existing Functions host key (Azure Functions platform auth). The Function context has no user identity by design — the unscoped repository read is the correct read path for a system-initiated sweep, and the writer's outbox emit is the only external side effect.

## Test plan stub

Full test inventory in `docs/test-plans/T-0083.md`. Targets:
- **4 unit tests** for `CancelExpiredOrder.Handler`:
  1. Happy path (`PendingPayment → Cancelled`, outbox event emitted, response carries new state).
  2. Silent Success on wrong-state (`Paid` Order — no mutation, Success returned).
  3. Silent Success on already-`Cancelled` (no re-emit, no re-mutation).
  4. Outbox event payload contract (`Reason = "AutoExpiry"`, all fields populated).
- **2 Function tests** for `CancelExpiredPendingPaymentOrdersFunction.RunAsync`:
  1. Zero rows → empty-batch summary log, `ISender.Send` not called.
  2. N rows with mid-row failure → fail-continue, all N dispatched, summary reports `Failed=1`.
- **1 integration test** end-to-end:
  - Seed Postgres with expired + non-expired + wrong-state Orders; invoke `RunAsync`; assert only expired-PendingPayment rows transition; assert `outbox_events` table has exactly N rows of `order.cancelled.customerEmail`.

### TDD red→green surface

The **pure-logic surface** committed test-first (red) is:
1. `OrderCancellationSource` enum exists with `Customer = 0, AutoExpiry = 1, Admin = 2`.
2. `Order.Cancel(OrderCancellationSource.AutoExpiry)` transitions `PendingPayment → Cancelled`, stamps `CancellationSource` + `CancelledAt`, and throws `InvalidOrderTransitionException` from any non-`PendingPayment` state when called with `AutoExpiry`.
3. `IsOrderCancelled` predicate on `EmailSendService` returns `true` for `OrderCancelledCustomerEmail` event type and `false` for all others.

These three are commit-1 (red). Implementation lands in commits 2–4.

## Alternatives Considered

- **Option A — Per-country TTL via `CountryConfiguration.PendingPaymentTtlHours`.** *Rejected per A.1* — at MVP single-country (CZ), the configuration row adds schema + a lookup + a code branch with zero behavioural value. The 24h window matches Comgate's intent-expiry; introducing variation before a 2nd country has different needs is premature.
- **Option B — Shorter TTL (e.g., 6h or 12h).** *Rejected per A.1* — strands customers who initiate payment late-day and complete via delayed bank transfer the next morning. 24h matches the AC-3 retry-window contract and the Comgate intent-lifetime.
- **Option C — Longer TTL (e.g., 72h).** *Rejected per A.1* — extends the period where the Order row sits in `PendingPayment`, occupies stock reservations (when introduced), and clouds the customer's dashboard. Comgate's intent is dead at 24h; nothing operationally meaningful happens between 24h and 72h.
- **Option D — Silent auto-cancel (no customer email).** *Rejected per A.2* — US-customer-0010 expects closure feedback. Without the email, the customer's dashboard would silently flip the order to `Cancelled` and they'd wonder if it had been processed or just disappeared. The email is the trust-preserving close.
- **Option E — Maker email on auto-cancel too.** *Rejected per A.2* — the maker has no relationship to the order at `PendingPayment` state. Sending them a "this order you never saw was cancelled" email surfaces customer flake-out behaviour, adds inbox noise, and hints at funnel metrics the platform doesn't owe makers.
- **Option F — Function calls the domain method directly (bypass MediatR).** *Rejected per A.3 + ADR 0014/0020* — skips Validator pipeline + UoW pipeline + the atomic outbox-on-commit contract. The writer would need to manually wrap a transaction + manually emit the outbox event + manually `SaveChangesAsync`, all of which the pipeline behaviors already do correctly. Thin scheduler-wrapper + MediatR-dispatch is the locked T-0077 pattern.
- **Option G — Function emits the outbox event itself.** *Rejected per A.3 + ADR 0017* — couples the timer trigger to the outbox shape; if the outbox payload schema changes, both the Function AND the writer would need updates. The writer owns the outbox emit; the Function only dispatches the Command.
- **Option H — Hourly schedule (`0 0 * * * *`).** *Rejected per A.4* — overkill at MVP volumes. The 24h TTL doesn't care about hour-level granularity; a customer whose order expired at 14:00 UTC and gets cancelled at 02:00 UTC the next day is 12h later, which is well within the "next business day" closure expectation. Hourly burns 24× the Function invocations for zero customer-visible win.
- **Option I — Same time as AutoDeliver (08:00 UTC).** *Rejected per A.4* — load spike + log-channel contention at 08:00 UTC. Spreading the two sweeps (02:00 / 08:00) keeps each invocation's resource footprint minimal and makes Application Insights traces easier to read.
- **Option J — Business-hours schedule (e.g., 14:00 UTC = 15:00 CET).** *Rejected per A.4* — intrusive customer email timing in late afternoon CET; the "order expired" email lands when the customer is mid-workday and least likely to re-engage. 02:00 UTC = 03:00 CET means the email is waiting in the inbox at morning open, which is the right cadence for re-engagement.
- **Option K — Persistent claim table for in-flight sweeps.** *Rejected per T-0077 precedent* — stateless re-fetch on next sweep is operationally simpler. Orders that succeed transition out of the predicate; orders that fail get retried tomorrow. Adding a claim table creates new failure modes (orphaned claims after crash) for no operational gain at MVP volumes.

## Dependencies

- **depends_on T-0066** (CreateOrder — establishes `PendingPayment` initial state + `CreatedAt` stamp).
- **depends_on T-0067** (MarkOrderPaid — locks the upstream state machine; the writer interaction with `Order.Cancel` must respect the same state-machine contract).
- **adrs:** 0017 (outbox), 0019 (email-send routing), 0020 (background jobs Function discipline + Q-0008 MARS workaround), 0023 (read-side AsNoTracking + projection-only).
- **user_stories:** US-customer-0010 (Comgate retry / expired payment scenario — AC-3 24h window).

## Commits hint

1. `test(T-0083): pin OrderCancellationSource + Order.Cancel(AutoExpiry) + IsOrderCancelled (red)` — domain unit tests + email-routing predicate tests committed before implementation.
2. `feat(T-0083): OrderCancellationSource enum + Order.Cancel(AutoExpiry) domain method + outbox payload` — domain layer + outbox payload record + event type constant.
3. `feat(T-0083): CancelExpiredOrder feature + EmailSendService routing + cs-CZ template` — AppServices one-file feature + email template + i18n keys.
4. `feat(T-0083): IOrderRepository.GetExpiredPendingPaymentUnscopedReadOnlyAsync + Function + DI` — repository method + Function + host registration + schedule config binding.

## Status log

- 2026-06-09 `draft` by PM. Created as the order-lifecycle cleanup ticket. Reference precedents on master or in the bundle PR: T-0077 AutoDeliverOrders Function (timer + fail-continue + structured sweep summary + Q-0008 MARS workaround), T-0029 ProcessOutboxFunction (timer pattern), T-0067/T-0071/T-0076 outbox-via-email pattern. Slice scope: thin MediatR-dispatch Function + new one-file feature (`CancelExpiredOrder`) + new projection-only read-only repository method + `OrderCancellationSource` enum + `Order.Cancel(AutoExpiry)` domain method (verify pre-existence) + outbox event type + cs-CZ email template + i18n keys. Daily 02:00 UTC timer. Fail-continue per-row. Silent Success contract on wrong-state. Zero new HTTP endpoints, zero NSwag regen, zero new BusinessErrorMessage codes.
- 2026-06-09 `draft → ready` by PM. User answered 4 blocking AskUserQuestion items per `/feature` workflow step 3: **A.1** TTL = 24h matching US-customer-0010 AC-3 (rejected shorter/longer + per-country variation at MVP); **A.2** emit `order.cancelled.customerEmail` outbox event customer-only (rejected silent cancel + maker email); **A.3** Function dispatches MediatR Command, thin scheduler-wrapper per ADR 0020 (rejected direct domain call + Function-emits-outbox); **A.4** schedule daily 02:00 UTC, deliberately offset from T-0077's 08:00 UTC (rejected hourly + same-time + business-hours). 11 PM-absorbed decisions captured in `## Locked design decisions §C` (Function shape mirrors T-0077; Q-0008 MARS workaround; projection-only IAsyncEnumerable<string>; one-file feature shape; handler steps with Silent Success contract; `Order.Cancel` domain method verification; `OrderCancellationSource` enum at Customer/AutoExpiry/Admin; outbox event type constant + payload record; EmailSendService routing extension; email template + cs-CZ i18n keys; schedule key + default `0 0 2 * * *`; structured logging summary; zero new BusinessErrorMessage codes). 4 ADR-locked items extracted in §B (ADR 0017 outbox atomic emit + idempotent re-fire; ADR 0019 email-send routing branch per source; ADR 0020 thin Function + MARS workaround; ADR 0023 AsNoTracking + projection-only + index verification). Zero `manual_steps`. **Ready for dotnet-backend.** TDD red→green: commit 1 pins enum + domain transition + email predicate (red); commits 2–4 land domain + feature + repository/Function.
