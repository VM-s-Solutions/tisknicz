---
id: T-0107
title: ChangeOrderStateManually command (admin escape hatch, strict allow-list, required reason)
status: ready
size: M
owner: dotnet-backend
created: 2026-06-12
updated: 2026-06-12
depends_on: [T-0060, T-0083, T-0105, T-0106]
blocks: [T-0118]
user_stories: [US-admin-0010]
adrs: [0014]
phase: 5
manual_steps: []
security_touching: true
layers: [domain, appservices, web-admin]
---

# T-0107 — ChangeOrderStateManually command (admin escape hatch, strict allow-list, required reason)

## Context

T-0107 is the **last ticket in the order-exceptions bundle** (T-0105 RefundOrder → T-0106 OpenDispute/ResolveDispute → T-0107 manual state change; sequential, one PR — ResolveDispute dispatches RefundOrder, so refund ships first). It gives the admin a power tool for fixing stuck orders: lost Comgate webhook, maker mis-click on Accept, carrier-blind delivery, manual pending-payment expiry. It satisfies **US-admin-0010** AC-1 (valid target from the state machine + mandatory reason + audit entry) and AC-2 (transitions with a sanctioned command are rejected with a hint naming that command). AC-3 (timeline "manual admin action" tag in customer/maker views) is frontend, deferred to T-0118.

The tool is deliberately NOT a free-form state setter. Per user-locked Q4 (2026-06-12 deliberation) it enforces a **strict allow-list**: only transitions with a defensible manual-recovery story are permitted, and every transition that has a sanctioned command (RefundOrder T-0105, OpenDispute/ResolveDispute T-0106, MarkPayoutBatchCompleted T-0103) is blocked with an error code that names that command. The allow-list predicate is pure logic and is THE TDD red-first surface of this ticket (T-0067+ hard rule) — table-driven tests over the full `OrderState × OrderState` matrix commit before any implementation.

`Web.Admin` currently has no controllers; T-0105 (first in the bundle) creates `Web.Admin/Controllers/OrdersController.cs`, the admin NSwag client (`admin-api.v1.ts`), and `IAdminAuditLogWriter` snapshot support for `TargetEntity = "order"`. T-0107 extends all three. Audit before/after JSONB comes free from the existing `AdminAuditPipelineBehavior` via `IAdminAuditableCommand` (ADR 0014); `Reason` maps to the audit `Notes`. No customer/maker emails are sent by manual transitions at MVP (PM default — the admin coordinates communication manually via the order-messages thread when needed).

## Locked design decisions

### A. User-locked (2026-06-12 batch, non-negotiable)

1. **Q4 — strict allow-list.** Never manually → `Paid` without `PaymentProviderRef` present on the row; never → `Refunded` (sanctioned: `RefundOrder` T-0105); never FROM `Refunded` (terminal); never → `Disputed` (sanctioned: `OpenDispute` T-0106). Mandatory non-empty reason. Every blocked transition returns a code naming the sanctioned command where one exists. **Rejected:** free-form setter with confirmation dialog (bypasses every invariant; one fat-finger destroys the money trail).
2. **Q4 corollary — explicit transition table** (the contract; the policy implements exactly this):

   | From | Permitted manual targets | Routing |
   |---|---|---|
   | PendingPayment | Paid (only if `PaymentProviderRef` non-null — lost-webhook recovery); Cancelled (manual expiry) | `MarkAsPaid(clock, existing ref)`; `Cancel(clock, Admin)` |
   | Paid | Accepted (redo after un-accept / maker unreachable) | `Accept(clock)` |
   | Accepted | Paid (undo maker mis-click) | `RevertAcceptance(clock)` — NEW domain method |
   | Shipped | Delivered (carrier-blind delivery) | `MarkAsDelivered(clock, AdminManual)` |
   | Delivered | — (`Completed` reserved for T-0103 payout pipeline) | blocked: `useMarkPayoutBatchCompleted` |
   | Completed | — (refund path = T-0105 with Q5 acknowledgement) | blocked |
   | Cancelled | — (terminal; resurrect = new order) | blocked |
   | Refunded | — (terminal, never out) | blocked: `notAllowed` |
   | Disputed | — (resolution = T-0106) | blocked: `useResolveDispute` |

   All pairs not listed as permitted are blocked. `Paid → Cancelled` and `Accepted → Cancelled` are blocked with `useRefundOrder` — money is captured; cancelling without refunding strands customer funds.
3. **Q5 (context only).** Refund on Completed orders (already paid out) is T-0105's warning + acknowledgement flow, not T-0107's. T-0107 never reaches Refunded at all.

### B. ADR-locked (no relitigation)

- **ADR 0014 (admin audit).** `Command : IAdminAuditableCommand` → `AdminAuditPipelineBehavior` captures before/after JSONB and appends `AdminAuditLogEntry` only on success. `ActionCode = "order.manualStateChange"`, `TargetEntity = "order"`, `TargetId = OrderId`, `Notes => Reason`. Failed/blocked commands write no audit row (behavior skips on `!IsSuccess`).
- **State-graph edges live on the entity; who-may-take-which-edge lives in the command layer** (Order.cs authorisation note). T-0107 routes to existing domain transition methods wherever an edge exists; the entity keeps its guards as defence-in-depth.
- **`BusinessResult<T>` + centralized `BusinessErrorMessage`** for every blocked transition. One-file feature shape; handlers never call `SaveChangesAsync()` (UoW pipeline).
- **`[Authorize]` admin-host gate + fail-closed session check** in the handler (VerifyMaker / T-0034 precedent — never attribute a privileged state change to "system").

### C. PM-absorbed (no user input needed)

- **Policy is a pure Domain class:** `ManualOrderTransitionPolicy.Evaluate(OrderState from, OrderState to, bool hasPaymentProviderRef)` returns a decision record (Allowed + route discriminator, or Blocked + error code). Deterministic precedence: (1) `to == from` → AllowedNoOp; (2) `from == Refunded` → `notAllowed`; (3) `from == Disputed` → `useResolveDispute`; (4) `to == Refunded` → `useRefundOrder`; (5) `to == Disputed` → `useOpenDispute`; (6) `(Delivered, Completed)` → `useMarkPayoutBatchCompleted`; (7) `(Paid|Accepted, Cancelled)` → `useRefundOrder`; (8) allow-list pair — if `to == Paid` and no provider ref → `paidRequiresProviderRef`, else Allowed; (9) everything else → `notAllowed`.
- **No generic state setter on `Order`.** Judge call per grooming brief: a generic `ChangeStateManually(target)` bypasses invariants. Four of the five allowed pairs route to existing domain methods; the single residual pair (Accepted → Paid) gets a dedicated semantic method `Order.RevertAcceptance(IClock)` (guard `State == Accepted`; sets `State = Paid`, clears `AcceptedAt` so a later re-accept stamps fresh). Zero residual generic surface.
- **`OrderDeliverySource.AdminManual = 3`** appended (the enum's own doc-comment anticipated exactly this value). Stamped on Shipped → Delivered manual transitions.
- **`Cancel` source:** existing `OrderCancellationSource.Admin = 2` (shipped by T-0083 for this ticket).
- **Same-state target = Silent Success** (T-0067/T-0076 idempotency precedent, matching T-0105 re-refund / T-0106 re-dispute defaults): no mutation, 200, audit row records identical before/after with the reason.
- **PendingPayment → Paid routing:** handler calls `order.MarkAsPaid(clock, order.PaymentProviderRef!)` — the matching-ref set-once guard passes; `PaidAt = clock.UtcNow` (we have no authoritative provider timestamp on the manual path).
- **No outbox events, no emails** by default. Q-0012 enrichment-at-enqueue question untouched.
- **New `BusinessErrorMessage` codes (6):** `OrderManualTransitionNotAllowed = "order.manualTransition.notAllowed"`, `...UseRefundOrder = "order.manualTransition.useRefundOrder"`, `...UseOpenDispute = "order.manualTransition.useOpenDispute"`, `...UseResolveDispute = "order.manualTransition.useResolveDispute"`, `...UseMarkPayoutBatchCompleted = "order.manualTransition.useMarkPayoutBatchCompleted"`, `...PaidRequiresProviderRef = "order.manualTransition.paidRequiresProviderRef"`. Parallel keys in `frontend/src/lib/i18n/cs-CZ.ts`.
- **Reason validation:** `NotEmpty` + `MinimumLength(10)` (forces a real sentence, not "fix") + `MaximumLength(2000)` (audit notes column width, VerifyMaker precedent).
- **Endpoint:** `POST /api/v1/admin/orders/{orderId}/state` body `{ targetState, reason }`. Globally-unique response name `ChangeOrderStateManuallyResponse(OrderState State)` (returns the post-transition state). NSwag regen: admin host client only.
- **No migration.** `AdminManual` enum value is data-only (SMALLINT column unchanged); no new columns.

## Scope

### Domain layer

- **`Core.Domain/Orders/ManualOrderTransitionPolicy.cs`** — NEW static class + nested sealed decision record. Pure logic, no dependencies. Routes: `NoOp`, `MarkAsPaid`, `Cancel`, `Accept`, `RevertAcceptance`, `MarkAsDelivered`. **TDD red-first: table-driven tests over the full matrix commit before this file exists.**
- **`Core.Domain/Orders/Order.cs`** — NEW method `RevertAcceptance(IClock)` (guard `State == Accepted` else `InvalidTransition()`; sets `State = Paid`, `AcceptedAt = null`). No other entity changes.
- **`Core.Domain/Orders/OrderDeliverySource.cs`** — append `AdminManual = 3`.
- **`Core.Domain/Common/BusinessErrorMessage.cs`** — the 6 new constants (§C).

### AppServices layer

- **`Core.AppServices/Features/Orders/ChangeOrderStateManually.cs`** — NEW one-file feature:
  - `Command(string OrderId, OrderState TargetState, string Reason) : ICommand<ChangeOrderStateManuallyResponse>, IAdminAuditableCommand` with `ActionCode = "order.manualStateChange"`, `TargetEntity = "order"`, `TargetId => OrderId`, `Notes => Reason`.
  - `Validator`: `OrderId` NotEmpty/Max 40; `TargetState` `IsInEnum()`; `Reason` NotEmpty + MinLength 10 + MaxLength 2000.
  - `Handler(IOrderRepository orders, IUserSessionProvider session, IClock clock)`:
    1. Fail-closed session check (`Error.Unauthorized()` when no user id — VerifyMaker precedent).
    2. Load order; null → `Error.NotFound` `OrderNotFound`.
    3. `TargetState == order.State` → Silent Success (no mutation).
    4. `ManualOrderTransitionPolicy.Evaluate(order.State, TargetState, order.PaymentProviderRef is not null)`; Blocked → `Error.Conflict("state", <policy code>)`.
    5. Allowed → dispatch the routed domain method (table §A.2). Domain-guard failure propagates as Conflict (defence-in-depth; unreachable when policy and entity agree).
    6. Return `ChangeOrderStateManuallyResponse(order.State)`. No `SaveChangesAsync()`; no outbox.

### Web.Admin host

- **`Web.Admin/Controllers/OrdersController.cs`** — extend the controller created by T-0105: `[HttpPost("{orderId}/state")]`, `[Authorize]` (admin audience per ADR 0013), `[ProducesResponseType(typeof(ChangeOrderStateManuallyResponse), 200)]`, one-liner `Mediator.Send`.

### Frontend

- **`frontend/src/lib/i18n/cs-CZ.ts`** — 6 new error keys mirroring §C codes (Czech copy names the sanctioned action, e.g. "Použijte refundaci objednávky").
- **`frontend/src/lib/api-client/admin-api.v1.ts`** — NSwag regen (admin host), committed in the same PR. No manual edits (pre-commit hook).

### Tests (~10 unit + 2 integration)

`ManualOrderTransitionPolicyTests` (red-first, table-driven; `backend/src/Makables.Tests/Domain/Orders/`):
1. **Allowed_pairs_route_correctly** — theory over the 5 allowed pairs (with `hasPaymentProviderRef = true`); asserts route discriminator per §A.2.
2. **Target_Refunded_blocked_useRefundOrder_from_every_state** (except from Refunded/Disputed — precedence).
3. **Target_Disputed_blocked_useOpenDispute_from_every_state** (same precedence carve-out).
4. **From_Refunded_blocked_notAllowed_for_every_target.**
5. **From_Disputed_blocked_useResolveDispute_for_every_target.**
6. **Paid_target_without_providerRef_blocked_paidRequiresProviderRef** — both `(PendingPayment, Paid)` and `(Accepted, Paid)` with `hasPaymentProviderRef = false`.
7. **Delivered_to_Completed_blocked_useMarkPayoutBatchCompleted.**
8. **Paid_or_Accepted_to_Cancelled_blocked_useRefundOrder.**
9. **Full_matrix_exhaustive** — iterate every `OrderState × OrderState` pair; assert a decision exists and ONLY the 5 documented pairs (plus same-state NoOp) are Allowed. A future `OrderState` value fails this test until classified.

`OrderRevertAcceptanceTests` + `ChangeOrderStateManuallyHandlerTests`:
10. **RevertAcceptance_from_Accepted_clears_AcceptedAt_and_sets_Paid**; from any other state → `OrderInvalidTransition`. Handler: happy path PendingPayment → Cancelled stamps `CancellationSource.Admin`; same-state → Silent Success without repository mutation; missing session → Unauthorized; Validator rejects 9-char reason.

`ChangeOrderStateManuallyIntegrationTests` (Testcontainers + WebApplicationFactory):
1. **POST_shipped_to_delivered_succeeds_and_audits** — seed Shipped order; POST `{ targetState: "Delivered", reason: "Zákazník potvrdil převzetí telefonicky." }` as admin. Assert 200; DB `State == Delivered`, `DeliverySource == AdminManual`, `DeliveredAt` set; `admin_audit_log` row with `action_code = "order.manualStateChange"`, before/after JSONB showing the state diff, `notes` == reason.
2. **POST_paid_to_refunded_blocked_409_names_RefundOrder** — seed Paid order; POST target Refunded. Assert 409 `order.manualTransition.useRefundOrder`; DB state unchanged; no audit row.

### Docs

- **`docs/architecture/roles/order.md`** — add the manual-transition table (§A.2) and the `ManualOrderTransitionPolicy` seam; note `RevertAcceptance` + `AdminManual` delivery source.
- **`docs/tickets/INDEX.md`** — PM flips T-0107 to done post-merge.

## Alternatives Considered

- **Option A — Generic `Order.ChangeStateManually(target)` setter.** *Rejected per Q4 + §C* — bypasses set-once provider-ref invariants, timestamp stamping, and source attribution. Routing to existing domain methods keeps every transition's side effects (timestamps, sources, guards) in one place; the residual pair gets a semantic method instead.
- **Option B — Permissive tool (any state-machine-legal edge) with a confirmation dialog.** *Rejected per Q4* — `→ Refunded` without the Comgate refund call (T-0105) silently desynchronizes money state; `→ Disputed` without a `Dispute` row (T-0106) breaks resolution. The allow-list makes the sanctioned-command boundary machine-enforced, not dialog-enforced.
- **Option C — Single generic `order.manualTransition.notAllowed` code for all blocked pairs.** *Rejected per Q4* — US-admin-0010 AC-2 explicitly requires the hint naming the proper command. Six codes cost six i18n keys and save the admin a support ticket.
- **Option D — Allow `Delivered → Completed` manually.** *Rejected* — `Completed` means "maker payout settled" (T-0103 `MarkPayoutBatchCompleted`); a manual flip would fake a payout that never happened and corrupt payout reporting. Blocked code names the command.
- **Option E — Reason optional, audit row sufficient.** *Rejected per Q4* — mandatory ≥10-char reason is the cheap forensic record; before/after JSONB shows WHAT changed, the reason records WHY.
- **Option F — Emit customer/maker notification emails on manual transitions.** *Rejected (PM default)* — manual fixes are exception handling; an auto-email on an admin un-accept would confuse the maker mid-phone-call. The admin communicates via the order-messages thread when needed. Revisit post-MVP.

## Out of scope

- **Refunds (full or partial), `refunded_amount_minor`, Q5 Completed-refund acknowledgement** — T-0105 `RefundOrder`.
- **Dispute open/resolve, `Dispute` entity, `PreDisputeState`** — T-0106.
- **`Delivered → Completed`** — T-0103 payout pipeline.
- **Bulk state changes** — per US-admin-0010 out-of-scope.
- **Timeline "manual admin action" tag in customer/maker UI (US AC-3) + admin UI** — T-0118.
- **Customer/maker notification emails on manual transitions** — PM default, none at MVP.
- **Maker-share recovery / negative-balance ledger** — forward note pinned for T-0102 grooming.

## Acceptance criteria

- **AC-1** Given a Shipped order, when admin POSTs `/api/v1/admin/orders/{id}/state` with `targetState: Delivered` and a ≥10-char reason, then 200; DB shows `State = Delivered`, `DeliveredAt` stamped, `DeliverySource = AdminManual`.
- **AC-2** Given a PendingPayment order, when admin targets `Cancelled`, then 200; `State = Cancelled`, `CancelledAt` stamped, `CancellationSource = Admin`.
- **AC-3** Given a Paid order, targeting `Accepted` succeeds (`AcceptedAt` stamped); given the resulting Accepted order, targeting `Paid` succeeds and `AcceptedAt` is cleared (undo mis-click, both directions).
- **AC-4** Given a PendingPayment order with `PaymentProviderRef` non-null, targeting `Paid` succeeds with `PaidAt = now`; given one with `PaymentProviderRef` null, the same request returns 409 `order.manualTransition.paidRequiresProviderRef` and no mutation.
- **AC-5** Targeting `Refunded` from any state returns 409 `order.manualTransition.useRefundOrder`; targeting `Disputed` returns 409 `order.manualTransition.useOpenDispute`. `Paid/Accepted → Cancelled` returns 409 `useRefundOrder`. `Delivered → Completed` returns 409 `useMarkPayoutBatchCompleted`.
- **AC-6** Any request on an order in `Refunded` returns 409 `order.manualTransition.notAllowed`; in `Disputed` returns 409 `order.manualTransition.useResolveDispute`. Targeting the order's current state returns 200 Silent Success with no mutation.
- **AC-7** Missing/empty/9-char `Reason` → 400 validation error on `Reason`. Anonymous or non-admin-audience JWT → 401 (host gate); missing session user inside the handler → fail-closed Unauthorized.
- **AC-8** Every successful command writes one `admin_audit_log` row: `action_code = "order.manualStateChange"`, before/after JSONB differing on `State` (+ stamped fields), `notes` = reason, `admin_user_id` = caller. Blocked (409) and validation-failed (400) requests write no audit row.
- **AC-9** Build clean; ~10 new unit tests (policy table-driven red-first commit precedes implementation — verifiable in commit history) + 2 integration tests green; 6 new `BusinessErrorMessage` codes with parallel `cs-CZ` i18n keys; NSwag admin client regenerated in the same PR with no manual edits; `node scripts/check-consistency.mjs` exit 0.

## Technical notes

### Why the policy lives in Core.Domain (not the handler)

The allow-list is a business rule about the order state machine, not orchestration. Putting it in `Core.Domain` next to `Order` keeps the entire transition surface (entity edges + manual-tool policy) reviewable in one folder, keeps the predicate dependency-free for table-driven unit tests (no mocks, no MediatR), and lets a future T-0118 admin UI ask "which targets are valid from state X?" through the same class instead of duplicating the table in TypeScript (the UI still only renders what the backend permits — frontend holds no business logic; the dropdown population endpoint, if wanted, is a T-0118 concern).

### Why routing to existing domain methods beats a generic setter

Each existing transition method carries side effects the tool must not skip: `Cancel` stamps `CancelledAt` + `CancellationSource`; `MarkAsDelivered` stamps `DeliveredAt` + `DeliverySource`; `MarkAsPaid` enforces the set-once provider-ref invariant. A generic `State = target` setter would need to re-implement all of that or silently drop it — both wrong. The one edge with no existing method (Accepted → Paid) gets `RevertAcceptance`, which also encodes the non-obvious cleanup (clear `AcceptedAt`) that a generic setter would miss.

### Why `→ Paid` requires an existing `PaymentProviderRef`

The only legitimate manual `→ Paid` scenario is a lost/failed Comgate webhook where the payment session was already reserved (T-0065 stamped the ref) and the admin verified capture in the Comgate portal. Without a ref there is no payment to point at — marking Paid would fabricate revenue with no provider trail, and T-0105's refund path (which needs the ref for the Comgate refund call) would be permanently broken for that order.

## Risk

- **Security (HIGH — admin power tool).** Misrouted to a non-admin host this is order-state privilege escalation. Mitigations: admin-audience JWT (ADR 0013), `[Authorize]`, fail-closed session check, strict allow-list, mandatory audit. Security review required on the PR.
- **Policy/entity drift.** If a future ticket widens an entity transition guard, the policy could allow a pair the entity refuses (safe: Conflict) or block a newly-sanctioned pair (annoying: stale hint). Test 9's exhaustive matrix + the roles/order.md table keep the surfaces reconciled.
- **Bundle coupling.** T-0107 assumes T-0105 shipped the admin OrdersController, admin NSwag client, and `"order"` snapshot support in `IAdminAuditLogWriter`. Sequential implementation in one branch makes this safe; do not reorder.

## Test plan reference

Inline above (Scope > Tests). No separate `docs/test-plans/T-0107.md`.

## Files touched (expected)

**New:** `backend/src/Makables.Core.Domain/Orders/ManualOrderTransitionPolicy.cs`; `backend/src/Makables.Core.AppServices/Features/Orders/ChangeOrderStateManually.cs`; `backend/src/Makables.Tests/Domain/Orders/ManualOrderTransitionPolicyTests.cs`; `backend/src/Makables.Tests/AppServices/Features/Orders/ChangeOrderStateManuallyHandlerTests.cs`; `backend/src/Makables.IntegrationTests/Orders/ChangeOrderStateManuallyIntegrationTests.cs`.
**Modified:** `Core.Domain/Orders/Order.cs` (`RevertAcceptance`); `Core.Domain/Orders/OrderDeliverySource.cs` (`AdminManual = 3`); `Core.Domain/Common/BusinessErrorMessage.cs` (6 codes); `Web.Admin/Controllers/OrdersController.cs` (extend); `frontend/src/lib/i18n/cs-CZ.ts`; `frontend/src/lib/api-client/admin-api.v1.ts` (regen); `docs/architecture/roles/order.md`.

## Commits hint

1. `test(T-0107): pin ManualOrderTransitionPolicy matrix (red)` — table-driven policy tests + RevertAcceptance entity tests.
2. `feat(T-0107): policy + RevertAcceptance + AdminManual source + error codes (green)`.
3. `feat(T-0107): ChangeOrderStateManually feature + admin endpoint + i18n + NSwag regen`.
4. `test(T-0107): handler + integration coverage`.

## Status log

- 2026-06-12 `draft` by PM. Created as the third ticket in the order-exceptions bundle (T-0105 refund → T-0106 dispute → T-0107 manual change; ResolveDispute dispatches RefundOrder so refund ships first). Precedents: VerifyMaker (IAdminAuditableCommand shape + fail-closed session), T-0083 (`OrderCancellationSource.Admin`), T-0076 (silent-success idempotency), T-0067 (red-first pure-logic rule).
- 2026-06-12 `draft → ready` by PM. User locked Q4 (strict allow-list + named sanctioned-command codes + mandatory reason) and Q5 (Completed-refund acknowledgement — T-0105 scope) in the 2026-06-12 batched deliberation. PM absorbed: pure Domain policy class with deterministic precedence; no generic setter (dedicated `RevertAcceptance`); `AdminManual = 3` delivery source; same-state Silent Success; 6 error codes + i18n; no emails/outbox; no migration; NSwag admin host regen. No manual_steps. **Ready for dotnet-backend** (implement after T-0105 and T-0106 in the same branch/PR).

## Definition of Ready

- [x] User story linked (US-admin-0010) with AC mapped (AC-1/AC-2 here; AC-3 → T-0118).
- [x] All blocking questions answered (Q4/Q5 locked 2026-06-12; Q-0016 ruled, docs-only, architect owns).
- [x] Allow-list captured as an explicit transition table (§A.2) — no interpretation room.
- [x] Error codes, i18n keys, audit shape, endpoint route, and test counts enumerated.
- [x] Dependencies on master or earlier in the bundle PR (T-0060, T-0083 on master; T-0105/T-0106 precede in-branch).
