---
id: T-0181
title: "Order escape hatches: customer cancels unpaid, maker refuses paid within 2 days"
status: ready
size: M
owner:
created: 2026-08-21
updated: 2026-08-22
depends_on: [T-0172, T-0174]
blocks: []
user_stories: [US-customer-0010, US-maker-0006]
adrs: [0004, 0014, 0016, 0017, 0019, 0020, 0022]
phase: 8
manual_steps: [nswag-regen, ef-migration]
security_touching: true
layers: [dotnet-db, dotnet-backend, frontend, l10n, secops]
---

# T-0181 — Order escape hatches

## Context
Audit findings [MAKER-H3, CUST-M3](../review/ux-functional-audit-2026-08-21.md). Two dead ends
existed by design: a maker who cannot fulfil a paid order could only accept or ignore it, and a
customer could not cancel an unpaid order — the only exit was the silent 24 h auto-expiry.
T-0174 / T-0172 shipped interim guidance copy for both.

**Q-0041 answered 2026-08-22.** Today's answer supplied the **time bound**; the user-confirmed
decision of **2026-06-03** (recorded in `docs/status/sprint-7.md`) had already supplied the
**roles**. Together they fully determine this ticket:

| Actor | From state | Window | Money |
|---|---|---|---|
| Customer | `PendingPayment` only | none | nothing moved — no refund path |
| Maker ("refuses") | `Paid` only | **2 days from `PaidAt`** | refund via the existing `RefundOrder` |
| Admin | any state | none | unchanged — T-0107 already ships it |

**Not granted:** customer cancellation of a *paid* order. Neither answer asks for it, and on
made-to-order goods it would return money after production may have started.

## Scope
- **Domain:** no state-machine change needed — `Order.Cancel` already accepts
  `PendingPayment | Paid | Accepted` and stamps `OrderCancellationSource`
  (`Order.cs:796-808`). Add `OrderCancellationSource.Maker` (the enum has Customer / AutoExpiry /
  Admin only) + migration.
- **Config:** `CountryConfiguration.MakerRefusalWindowHours` (default **48**) — the window is a
  policy, so it is a config row, never a hard-coded constant (ADR 0004; no country branching).
- **Backend — customer:** `CancelPendingOrder` command on the Customer host,
  `PendingPayment`-only, IDOR-scoped; idempotent Silent-Success on an already-cancelled order
  (T-0076 precedent); emits the existing `order.cancelled.customerEmail` outbox event.
- **Backend — maker:** `RefuseOrder` command on the Maker host, `Paid`-only, rejected past the
  window with a new `OrderRefusalWindowExpired` code pointing the maker at admin support. On the
  happy path it runs the existing `RefundOrder` path in the same flow and notifies the customer
  (new `EmailTemplateType.OrderRefusedCustomer` + cs-CZ/en-US seed).
- **Frontend:** replace T-0174's interim Paid-order hint with a real confirm-gated "Odmítnout"
  action showing the remaining window; replace T-0172's interim 24 h notice on
  `/objednavka/[id]` with a real "Zrušit objednávku" for PendingPayment.
- NSwag regen (customer + maker hosts); cs-CZ keys for every new code.

## Alternatives Considered
- **Hard-code 48 h** — *rejected*: a policy that will be tuned belongs in `CountryConfiguration`
  (ADR 0004), and the seed makes the default explicit.
- **Reuse `OrderCancellationSource.Admin` for maker refusals** — *rejected*: the dispute trail must
  distinguish "the platform intervened" from "the maker refused"; that distinction is the whole
  point of the enum.
- **Let the customer cancel a paid order too** — *rejected by the answer* (see Context).
- **Auto-refund without the existing RefundOrder path** — *rejected*: refunds are money-moving and
  already have a reviewed, idempotent implementation (T-0105); a second path would diverge.

## Out of scope
- The maker accept-by SLA timer and sanctions ladder — [T-0148](../tickets/INDEX.md) (blocked on
  its own §5.1 question).
- Post-payout refunds — Q-0018 / T-0142 territory; `RefundOrder` already guards on
  `PayoutTransferProviderRef IS NULL`.

## Acceptance criteria
- **AC-1** Given a `PendingPayment` order, when its customer cancels, then it becomes `Cancelled`
  with `CancellationSource = Customer`, and no refund is attempted (nothing was captured).
- **AC-2** Given a customer cancelling an order that is **not** theirs, then not-found (scoped
  repository — ADR 0013), never a 403 that confirms existence.
- **AC-3** Given a customer cancelling an order that is **not** `PendingPayment`, then
  `OrderInvalidTransition`; the order is unchanged.
- **AC-4** Given a `Paid` order **within** the window, when its maker refuses it, then it becomes
  `Cancelled` with `CancellationSource = Maker`, the refund is issued through `RefundOrder`, and
  the customer notification is enqueued (one outbox row).
- **AC-5** Given a `Paid` order **past** the window (`now - PaidAt > MakerRefusalWindowHours`), when
  the maker refuses, then `OrderRefusalWindowExpired` and the order is unchanged — asserted with a
  pinned `IClock`, not wall-clock.
- **AC-6** Given an already-cancelled order, when either action re-runs, then Silent-Success with
  no second refund and no second outbox row (re-delivery safety).
- **AC-7** The window is read from `CountryConfiguration`, not a constant (test changes the config
  row and observes the boundary move).
- **AC-8** Every new `BusinessErrorMessage` code has a cs-CZ key + a triggering test;
  `npm run check:api` passes after regen; `dotnet test` counts reported.

## Technical notes
`Order.cs:796-808` is the transition to reuse — do **not** add a new one. `RefundOrder` (T-0105) is
the money path; `MarkOrderDelivered` (T-0076) is the Silent-Success idempotency precedent.
`CancelExpiredOrder` (T-0083) shows the cancellation outbox shape to mirror.

## Files touched (expected)
- `backend/src/Makables.Core.Domain/Orders/OrderCancellationSource.cs`, `.../Configuration/CountryConfiguration.cs`
- `backend/src/Makables.Infra.Database/Migrations/**` (enum value + config column + template seed)
- `backend/src/Makables.Core.AppServices/Features/Orders/{CancelPendingOrder,RefuseOrder}.cs` (+ tests)
- `backend/src/Makables.Web.Customer/**`, `backend/src/Makables.Web.Maker/**`
- `frontend/src/app/(customer)/objednavka/[id]/**`, `frontend/src/app/(maker)/dashboard/maker/objednavky/[orderId]/**`
- `frontend/src/lib/i18n/cs-CZ.ts`, `frontend/src/lib/api-client/*` (regen)

## Test plan reference
`docs/test-plans/T-0181.md`

## Status log
- 2026-08-21 filed `draft` (Phase 8 UX sweep plan) — blocked on Q-0041
- 2026-08-22 `draft → ready` — Q-0041 answered; today's time bound composes with the 2026-06-03
  role decision, so roles + window + money path are all determined. No open question remains
