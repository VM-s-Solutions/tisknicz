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

## Reverse shipment (return-to-maker, T-0146)

Once a dispute is confirmed to warrant a physical return, admin generates a
reverse Zásilkovna shipment (customer's address as sender, maker's
registered address as recipient — mirrors the forward T-0072/T-0074/T-0075
label-cache shape verbatim, pointed at a dispute-scoped blob path):

- `ReturnCarrierRef` / `ReturnTrackingUrl` — nullable, set once by
  `Dispute.SetReturnShipment` (same set-once + same-value-Silent-Success /
  different-value-loud-conflict contract as `PayoutBatch.AttachCsvBlobPath`).
  Their presence gates the customer-facing "Stáhnout vratkový štítek" link.
- `ReturnReceivedAt` / `ReturnReceivedBy` — nullable, set once by
  `Dispute.MarkReturnReceived` (requires a return shipment to exist first;
  a second call is a loud conflict, mirroring `Resolve`'s re-resolve
  posture). No automated carrier-status sync for the reverse leg — the
  maker (`MarkDisputeReturnReceivedByMaker`, owner-scoped) or admin on
  their behalf (`MarkDisputeReturnReceivedByAdmin`, admin-audited) records
  the acknowledgment manually, ahead of the eventual `ResolveDispute.Command`.
- **Trigger is admin-gated** (`GenerateReturnLabel.Command`,
  `IAdminAuditableCommand`), mirroring `RefundOrder`'s posture — every
  other money/logistics-affecting dispute outcome in this model is
  admin-triggered, never automatic. Category-gated to `DamagedItem` /
  `NotAsDescribed` (`dispute.return.categoryNotEligible` otherwise).
- **Cost accounting (Q-0037):** the maker-borne return-shipping cost is
  recorded as a `PayoutDeduction` (negative line item) at label-creation
  time — cost basis is `CountryConfiguration.DefaultShippingPriceMinor`
  (Packeta doesn't itemize the reverse leg at MVP). `CreatePayoutBatch`
  claims every eligible maker's pending deductions into whichever batch
  next pays them, subtracting the sum from that batch's wire total. Never
  a customer-facing charge.

## Lifecycle

- **Created by:** factory `Dispute.Open(id, orderId, category, description, source, countryCode)` via four open commands: `OpenCustomerDispute` (customer host; scoped `GetByIdForCustomerAsync` load = IDOR shield, cross-tenant probe → `404 order.notFound`), `OpenMakerDispute` (maker-host mirror), `OpenDispute` (admin host; any category — admin may transcribe phone-reported carrier failures; admin-audited), `DisputeShipment` (carrier-sourced; the rewired T-0078 stub — Packeta `Returned`/`Failed` map to `CarrierReturned`/`CarrierFailed` with canned description incl. `ShippingCarrierRef`)
- **Modified by:** `Resolve(IClock, DisputeResolutionOutcome, string resolutionNotes)` (resolution triple; refuses double-resolve with `order.dispute.notOpen`), `TryMarkAutoEscalated(IClock)` (T-0145, stamps `AutoEscalatedAt` exactly once; never touches the resolution triple or `Order.State`), `SetReturnShipment(carrierRef, trackingUrl)` (T-0146, set-once), `MarkReturnReceived(IClock, receivedBy)` (T-0146, set-once, requires a return shipment first)
- **Persisted by:** `IDisputeRepository` — `AddAsync(dispute, ct)` + `GetOpenByOrderIdAsync(orderId, ct)` (tracked; `ResolvedAt == null` predicate; at most one row matches per the partial unique index) + `GetByIdUnscopedAsync(disputeId, ct)` (tracked; admin host + T-0145's `EscalateDispute.Handler` + T-0146's `GenerateReturnLabel`/admin `MarkReturnReceived`) + `GetAutoEscalationCandidateIdsUnscopedReadOnlyAsync(asOf, ct)` (id-only stream, T-0145) + T-0146's `GetByIdUnscopedReadOnlyAsync` (Function context) and `GetByIdForCustomerReadOnlyAsync`/`GetByIdForMakerAsync` (owner-scoped, IDOR shield)
- **Destroyed by:** never (soft delete via `Auditable`)

## T-0145 — 14-day open window + 7-day maker-response timer

Per dopady §2.5 Q6–Q9, this ticket adds exactly two time-based rules on top of the T-0106 machinery
above — it does not change the resolution model, the parenthesis-state mechanics, or any opener
except `OpenCustomerDispute`.

### 14-day customer open window

`OpenCustomerDispute.Command`'s handler rejects with `order.dispute.windowExpired` when
`Order.State == Delivered AND now() > Order.DeliveredAt + 14 days` (the constant is
`OpenCustomerDispute.OpenWindowDays`). Boundary is inclusive on the "still open" side —
`now() == DeliveredAt + 14 days` still succeeds.

- **Gates ONLY the customer's platform button.** `OpenMakerDispute`, `OpenDispute` (admin), and
  `DisputeShipment` (carrier) are unchanged — the admin channel stays unlimited (statutory rights run
  outside the button) and the carrier-sourced path has no customer-initiated timing concept at all
  (Alternatives Considered Option A, T-0145).
- **No anchor, no gate.** Paid / Accepted / Shipped orders have no `DeliveredAt` yet, so the guard
  never fires for them — the pre-delivery in-flight behaviour is unchanged.
- Re-opening an already-`Disputed` order is still the T-0106 §C.4 Silent-Success path and is
  evaluated BEFORE the window guard (a re-open is not "opening a new dispute").

### 7-day maker-response auto-escalation

A daily sweep (`DisputeAutoEscalationFunction`, mirrors T-0077's `AutoDeliverOrdersFunction` shape)
selects id-only candidates via `IDisputeRepository.GetAutoEscalationCandidateIdsUnscopedReadOnlyAsync`
— `ResolvedAt IS NULL AND Source == Customer AND AutoEscalatedAt IS NULL AND CreatedAt < asOf - 7 days`
— and dispatches `EscalateDispute.Command` per candidate. The command's handler re-checks every guard
against a freshly-loaded tracked `Dispute` (the id-only projection can be stale by dispatch time):

1. Dispute still exists, still open, not already escalated.
2. `IOrderMessageRepository.HasMakerReplySinceAsync(orderId, dispute.CreatedAt, ct)` — a targeted
   `EXISTS` against `OrderMessage` (`AuthorRole == Maker AND CreatedAt > dispute.CreatedAt`), not a
   full thread load. The anchor is `Dispute.CreatedAt`, never "last customer message" — an anchor tied
   to the customer's own messages would let a chatty customer repeatedly reset the maker's clock
   (Alternatives Considered Option C, locked).
3. If no maker reply, `Dispute.TryMarkAutoEscalated(clock)` stamps `AutoEscalatedAt` (the idempotency
   claim — a second dispatch against the same dispute returns `false` and no-ops) and the handler
   enqueues `dispute.autoEscalated.adminEmail` (`EmailTemplateType.DisputeAutoEscalatedAdmin`,
   recipient resolves at send time exactly like `order.disputed.adminEmail`).

**Notification only — never resolves, never sanctions.** The dispute stays `Disputed` / `ResolvedAt
== null` after escalation; only admin's own `ResolveDispute.Command` can close it. Auto-refunding or
auto-sanctioning the maker on a timer was explicitly rejected (Alternatives Considered Option B,
T-0145) — a maker's delay may be a legitimate ongoing investigation, and every money-moving /
sanctioning outcome in this system requires a human admin decision. Maker sanctions for missed SLAs
are T-0148's territory (blocked on its own open question), not this sweep's.

## Invariants

- At most one OPEN dispute per order (`ux_disputes_order_open`); resolved disputes accumulate as history
- `Source` is always server-stamped — the four open commands each hard-code their source; no client input reaches it
- The Order state flip + `Dispute` row + admin-email outbox row commit atomically (UoW pipeline; handlers never call `SaveChangesAsync()`)
- Resolution fields (`ResolutionOutcome`, `ResolutionNotes`, `ResolvedAt`) are set together, exactly once
- Admin open + resolve are admin-audited via `IAdminAuditableCommand`; party + carrier variants are not (not admin commands)
- Return-shipment fields (`ReturnCarrierRef`/`ReturnTrackingUrl`) and the ack fields (`ReturnReceivedAt`/`ReturnReceivedBy`) are each set together, exactly once (T-0146)

## Implementation pointer

- `backend/src/Makables.Core.Domain/Orders/Dispute.cs` (+ `DisputeCategory`, `DisputeSource`, `DisputeResolutionOutcome`, `IDisputeRepository` alongside)
- `backend/src/Makables.Core.Domain/Orders/Order.cs` — `OpenDispute`/`ResolveDispute` edges + `PreDisputeState`
- `backend/src/Makables.Core.AppServices/Features/Orders/` — `OpenCustomerDispute`, `OpenMakerDispute`, `OpenDispute`, `ResolveDispute`, `DisputeShipment`, `GenerateReturnLabel`, `MarkDisputeReturnReceivedByAdmin`, `MarkDisputeReturnReceivedByMaker` (T-0146)
- T-0145: `backend/src/Makables.Core.AppServices/Features/Orders/EscalateDispute.cs`; `backend/src/Makables.Functions/Disputes/DisputeAutoEscalationFunction.cs`; migrations `20260707102940_AddDisputeAutoEscalatedAt.cs` + `20260707103019_SeedDisputeAutoEscalatedAdminEmailTemplate.cs`
- `backend/src/Makables.Core.AppServices/Features/Shipping/FetchAndStoreReturnLabel.cs` (T-0146, mirrors `FetchAndStoreShippingLabel`)
- `backend/src/Makables.Core.Domain/Payouts/PayoutDeduction.cs` + `IPayoutDeductionRepository` (T-0146 Q-0037 accounting)
- `backend/src/Makables.Infra.Database/Orders/DisputeRepository.cs` + `Configurations/DisputeConfiguration.cs`; migrations `20260612121152_AddDisputeTableAndPreDisputeState.cs`, `20260707102940_AddDisputeAutoEscalatedAt.cs`, `20260707114455_AddDisputeReturnShipmentAndPayoutDeduction.cs`

## Related

- Roles: `order` (parent; parenthesis state + refund surface), `order-message` (evidence channel), `outbox`, `manual-order-transition-policy` (sanctioned-command interlock), `shipping-carrier` (reverse-leg capability)
- Patterns: §A.22 (state-machine detour with restore)
- ADRs: 0013 (scoped repositories), 0014 (UoW + admin audit), 0017/0020 (outbox), 0019 (email)
- Tickets: T-0105 (refund path), T-0106 (this surface), T-0107 (manual-change interlock), T-0118 (dispute UI), T-0145 (14-day window + 7-day timer), T-0146 (reverse-shipping-label + payout deduction, depends on T-0145)
- Stories: US-admin-0011, US-customer-0022, US-customer-0023, US-maker-0019
