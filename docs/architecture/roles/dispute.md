---
role: Dispute
kind: aggregate-child
status: accepted
---

# Dispute

## Responsibility

Structured record of why an order was detoured into `Disputed` — category, the opener's own words, who opened it — and, once adjudicated, how admin resolved it (T-0106). The Order state flip is the escrow hold; the Dispute row is the triage evidence.

## Collaborators

- **Order** (parent aggregate; owns `State = Disputed`, `PreDisputeState`, and the `OpenDispute`/`ResolveDispute` parenthesis-state edges — see below)
- **OrderMessage** (the evidence channel — the T-0079 thread stays open in `Disputed`; no separate evidence-upload surface ships)
- **Outbox** (`order.disputed.adminEmail` enqueued on open; `order.disputeResolved.customerEmail` on resolve)
- **RefundOrder.Command / Order.Cancel** (sanctioned outcome commands dispatched by the resolve handler — never inlined)

## Knows

- `OrderId` (FK to `orders.id`, immutable) and `Source` (`DisputeSource : short` — `Customer | Maker | Carrier | Admin`; always server-stamped by the handler, never client-supplied)
- `Category` (`DisputeCategory : short` — `NotDelivered`, `DamagedItem`, `NotAsDescribed`, `CarrierReturned`, `CarrierFailed`, `Other`; the two carrier values are carrier-reserved — party Validators reject them with `order.dispute.categoryNotAllowed`; the admin open endpoint accepts all six)
- `Description` — opener's own words, trimmed at the `Dispute.Open` factory, max 2000 (`MaxDescriptionLength`)
- The resolution triple, null while OPEN: `ResolutionOutcome` (`DisputeResolutionOutcome : short` — `Refunded | Resumed | Cancelled`), `ResolutionNotes` (required on resolve, max 2000, **customer-visible** — rendered in the resolve email), `ResolvedAt` (null == OPEN)
- `Auditable` base: `CountryCode`, `IsActive`, `CreatedBy/On`, etc.

## Does NOT know

- The order's state machine — `PreDisputeState` and the detour flip live on `Order` (patterns §A.22)
- How money moves — the `Refunded` outcome is a nested `RefundOrder.Command` dispatch (T-0105's tested Comgate path), never re-implemented here
- Email rendering / recipient resolution (outbox + `EmailSendService` routing; the admin recipient resolves at send time from `EmailOptions.AdminNotificationAddress` / `ADMIN_NOTIFICATION_EMAIL`)
- The carrier wire protocol — `DisputeReason` (Packeta terminal-state enum) stays on the `DisputeShipment.Command` boundary and maps into `DisputeCategory`

## Parenthesis-state mechanics (patterns §A.22)

`Disputed` is a detour state on `Order`, not a terminal one:

- **Disputable allow-list = `Paid | Accepted | Shipped | Delivered`** (T-0106 §C.1 — Paid/Accepted are the "maker silent / never ships" escalation lanes; `Completed` is OUT: payout settled, nothing to freeze; `PendingPayment`/`Cancelled`/`Refunded` have nothing in escrow).
- `Order.OpenDispute(clock)` stamps `PreDisputeState = State` before flipping; `Order.ResolveDispute(clock, restoreTo)` guards `State == Disputed`, restores `State = restoreTo`, clears the pointer; `DisputedAt` is KEPT as a historical marker. Invariant: `PreDisputeState` non-null ⇔ `State == Disputed`.
- **Sweep exclusion by definition:** the auto-deliver + carrier sweeps select `State == Shipped`, so a disputed order drops out without predicate changes (pinned by integration tests; no predicate edits shipped).
- `AutoDeliverAt` is NOT extended on resume — if the window elapsed during the dispute, the next sweep auto-delivers immediately after a `Resumed` resolution (T-0106 §C.10; revisit if ops data shows premature closes).

## Resolution outcomes + sanctioned-command dispatch

`ResolveDispute.Command` (admin host, `IAdminAuditableCommand`) restores `PreDisputeState` FIRST, marks the row resolved (outcome + notes + `ResolvedAt`), enqueues the customer email, THEN branches per outcome:

| Outcome | Effect after restore |
|---|---|
| `Resumed` | nothing further — the order proceeds as if undisputed |
| `Refunded` | nested `mediator.Send(RefundOrder.Command)` for the **full remaining** amount (`TotalAmountMinor − RefundedAmountMinor`); the dispute lane never issues partials. Refund failure (e.g. Comgate window expired) leaves the dispute OPEN — the transaction rolls back, no half-resolve |
| `Cancelled` | `order.Cancel(clock, OrderCancellationSource.Admin)` — only legal from a Paid/Accepted restore; a Shipped/Delivered restore surfaces `order.invalidTransition` and the dispute stays open (the error steers admin to `Refunded` for shipped goods) |

The outcome edges apply from the restored state, so `Order.Refund`'s and `Order.Cancel`'s allow-lists stay untouched — `Disputed` is never refunded or cancelled directly. T-0107's manual tool refuses `→ Disputed` / `Disputed →` hops with codes naming `OpenDispute`/`ResolveDispute` (see `manual-order-transition-policy.md`).

## Idempotency boundaries (asymmetric by design)

- **Re-OPEN is Silent-Success:** opening on an already-`Disputed` order returns 200 with the EXISTING open dispute's id — no second row, no second outbox emission. Backed by the partial unique index `ux_disputes_order_open UNIQUE (order_id) WHERE resolved_at IS NULL` (makes the concurrent-open race safe). The carrier sweep's re-fire on a Disputed order is the same Silent-Success.
- **Re-RESOLVE is loud:** resolve on a non-`Disputed` order — and a second `Dispute.Resolve` — returns `409 order.dispute.notOpen`. The asymmetry is intentional (T-0106 §C.4): re-open is idempotent-safe; a silently "succeeding" re-resolve with a different outcome would mask an admin race and risks double money-movement.

## Lifecycle

- **Created by:** factory `Dispute.Open(id, orderId, category, description, source, countryCode)` via four open commands: `OpenCustomerDispute` (customer host; scoped `GetByIdForCustomerAsync` load = IDOR shield, cross-tenant probe → `404 order.notFound`), `OpenMakerDispute` (maker-host mirror), `OpenDispute` (admin host; any category — admin may transcribe phone-reported carrier failures; admin-audited), `DisputeShipment` (carrier-sourced; the rewired T-0078 stub — Packeta `Returned`/`Failed` map to `CarrierReturned`/`CarrierFailed` with canned description incl. `ShippingCarrierRef`)
- **Modified by:** `Resolve(IClock, DisputeResolutionOutcome, string resolutionNotes)` only — sets the resolution triple; refuses double-resolve with `order.dispute.notOpen`
- **Persisted by:** `IDisputeRepository` — `AddAsync(dispute, ct)` + `GetOpenByOrderIdAsync(orderId, ct)` (tracked; `ResolvedAt == null` predicate; at most one row matches per the partial unique index)
- **Destroyed by:** never (soft delete via `Auditable`)

## Invariants

- At most one OPEN dispute per order (`ux_disputes_order_open`); resolved disputes accumulate as history
- `Source` is always server-stamped — the four open commands each hard-code their source; no client input reaches it
- The Order state flip + `Dispute` row + admin-email outbox row commit atomically (UoW pipeline; handlers never call `SaveChangesAsync()`)
- Resolution fields (`ResolutionOutcome`, `ResolutionNotes`, `ResolvedAt`) are set together, exactly once
- Admin open + resolve are admin-audited via `IAdminAuditableCommand`; party + carrier variants are not (not admin commands)

## Implementation pointer

- `backend/src/Makables.Core.Domain/Orders/Dispute.cs` (+ `DisputeCategory`, `DisputeSource`, `DisputeResolutionOutcome`, `IDisputeRepository` alongside)
- `backend/src/Makables.Core.Domain/Orders/Order.cs` — `OpenDispute`/`ResolveDispute` edges + `PreDisputeState`
- `backend/src/Makables.Core.AppServices/Features/Orders/` — `OpenCustomerDispute`, `OpenMakerDispute`, `OpenDispute`, `ResolveDispute`, `DisputeShipment`
- `backend/src/Makables.Infra.Database/Orders/DisputeRepository.cs` + `Configurations/DisputeConfiguration.cs`; migration `20260612121152_AddDisputeTableAndPreDisputeState.cs`

## Related

- Roles: `order` (parent; parenthesis state + refund surface), `order-message` (evidence channel), `outbox`, `manual-order-transition-policy` (sanctioned-command interlock)
- Patterns: §A.22 (state-machine detour with restore)
- ADRs: 0013 (scoped repositories), 0014 (UoW + admin audit), 0017/0020 (outbox), 0019 (email)
- Tickets: T-0105 (refund path), T-0106 (this surface), T-0107 (manual-change interlock), T-0118 (dispute UI)
- Stories: US-admin-0011
