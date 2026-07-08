---
id: T-0145
title: Dispute window + maker-response timer (state-machine half of the complaint flow)
status: ready
size: M
owner: dotnet-backend
created: 2026-07-07
updated: 2026-07-07
depends_on: [T-0079, T-0106]
blocks: [T-0146]
user_stories: [US-customer-0022, US-maker-0019]
adrs: [0013, 0014, 0017, 0020]
phase: 7
manual_steps: [ef-migration]
security_touching: false
layers: [dotnet-db, dotnet-backend, frontend, l10n]
---

# T-0145 — Dispute window + maker-response timer

## Context

Per [dopady-rozhodnuti-na-platformu.md §2.5](../meetings/dopady-rozhodnuti-na-platformu.md#25-reklamační-proces-q6q9--l) (dopady §1 Q6–Q9), the business has decided the complaint flow: the customer's first contact is the maker (via the existing order message thread), the maker has 7 days to respond before the case auto-escalates to admin, and the customer can only use the platform's dispute button within 14 days of delivery (admin remains unlimited — statutory rights continue outside the button).

The underlying `Dispute`/`OrderMessage` machinery already exists and shipped in T-0106 (see [docs/architecture/roles/dispute.md](../architecture/roles/dispute.md)): a `Dispute` aggregate with categories, a resolution triple, a parenthesis-state detour on `Order`, and four opener commands (`OpenCustomerDispute`, `OpenMakerDispute`, `OpenDispute` [admin], `DisputeShipment` [carrier]). What's missing is exactly the two time-based rules this ticket adds: (1) the customer-facing 14-day-from-delivery open window, and (2) the 7-day maker-response auto-escalation timer. This is the "state-machine half" of the reklamace work package — the reverse-shipping-label half (§2.5's return-cost/Zásilkovna row) is split out to **T-0146**, which depends on this ticket's window/timer existing first.

Satisfies US-customer-0022 (customer opens within 14 days, escalates from the message thread) and US-maker-0019 (maker has 7 days to respond before auto-escalation).

## Scope

- **14-day open window** (customer-facing only): `OpenCustomerDispute.Command` gains a guard — when the order is `Delivered` and `now() > Order.DeliveredAt + 14 days`, reject with a new time-window error instead of creating the `Dispute`. The other three openers (`OpenMakerDispute`, `OpenDispute` [admin], `DisputeShipment` [carrier]) are **unchanged** — this window is specific to the customer's platform button (dispute.md already documents the admin channel as unlimited; the maker-opener and carrier-sourced paths have no analogous business rule to gate).
- **UX ordering**: "Reklamovat" on the order page first routes into the existing order-scoped message thread (T-0079) with a pre-filled `DisputeCategory` selector; only an explicit "Eskalovat na Makables" action inside the thread calls `OpenCustomerDispute.Command`. (This is primarily a frontend routing change — the backend command itself is unchanged in shape, only newly gated by the window.)
- **7-day maker-response timer**: a new daily-sweep Function (mirrors `AutoDeliverOrdersFunction`, T-0077) that selects open (`ResolvedAt IS NULL`), customer-sourced (`Source = Customer`) disputes where `CreatedAt + 7 days < now()` AND no `OrderMessage` from the maker exists on that order with `CreatedAt > Dispute.CreatedAt`. Matches: enqueue a `dispute.autoEscalated.adminEmail` outbox event. The dispute itself is untouched (still `Disputed`, still awaiting admin's `ResolveDispute.Command` — this sweep only raises urgency, it never auto-resolves).
- New `EmailTemplateType.DisputeAutoEscalatedAdmin` + seed migration (cs-CZ + en-US), consistent with the existing admin-notification email pattern.
- New `BusinessErrorMessage` code (e.g. `order.dispute.windowExpired`) + Czech i18n.
- Frontend: the "Reklamovat" entry point on the order detail page routes to the message thread (not directly to a dispute form); the thread gains a category selector + an "Eskalovat na Makables" action visible only while the 14-day window (when applicable) hasn't elapsed.

## Alternatives Considered

- **Option A — Enforce the 14-day window inside the shared `Dispute.Open` factory, so all four opener commands are gated.** *Rejected* — dispute.md is explicit that the admin channel is unlimited ("admin bez limitu — zákonná práva běží dál, jen mimo platformní tlačítko"), and the carrier-sourced `DisputeShipment` command fires automatically off Packeta signals with no customer-initiated timing concept at all. Gating only `OpenCustomerDispute` keeps the other three openers' existing, already-shipped behavior untouched and matches the business rule precisely — it's a rule about *the customer's button*, not about the `Dispute` aggregate as a whole.
- **Option B — Auto-resolve the dispute (e.g. auto-refund the customer) if the maker doesn't respond within 7 days, instead of just escalating.** *Rejected* — dopady §2.5/Q7 only specifies a response-time SLA that triggers escalation to admin, not an automatic money-moving outcome. Every resolution outcome in the existing `Dispute.Resolve` model requires an admin decision (dispute.md's outcome-dispatch table: `Refunded` nests `RefundOrder.Command`, `Cancelled` nests `Order.Cancel` — both admin-triggered). Auto-refunding on a timer would bypass that safeguard and risk incorrect refunds when a maker's delay is legitimate (e.g. genuinely investigating a claim, briefly unavailable).
- **Option C — Compute the 7-day timer as "since dispute opened" using the dispute's own `CreatedAt`, vs. an alternative anchor like "since the customer's last message".** *Locked as CreatedAt* — dopady §2.5 states the rule as "maker reaguje do 7 dní" from when the complaint was raised, and `Dispute.CreatedAt` is the unambiguous, already-existing timestamp for that. Anchoring to "last customer message" would let a chatty customer repeatedly reset the maker's clock, which isn't the intent.

## Out of scope

- The reverse-shipping-label / return-to-maker flow (§2.5's Zásilkovna return row) — that's **T-0146**, which explicitly depends on this ticket.
- Any automatic sanction against a maker who misses the 7-day window (three-tier warning/suspend/deactivate sanctions are **T-0148**, blocked on its own open question and explicitly out of scope here).
- Reminder nudges to the maker *before* day 7 (T-0148's broader SLA-timer-nudge pattern covers earlier reminders; this ticket ships only the day-7 auto-escalation email).
- Any change to `ResolveDispute.Command` or the resolution-outcome dispatch table — unchanged.
- A dedicated evidence-upload surface for the complaint — the existing order message thread remains the sole evidence channel (dispute.md).

## Acceptance criteria

- **AC-1** Given an order is `Delivered` and `now() <= DeliveredAt + 14 days`, when the customer calls `OpenCustomerDispute.Command`, then the dispute is created exactly as it is today (no behavior change within the window).
- **AC-2** Given an order is `Delivered` and `now() > DeliveredAt + 14 days`, when the customer calls `OpenCustomerDispute.Command`, then it's rejected with `order.dispute.windowExpired` and no `Dispute` row is created; the frontend hides or disables the "Eskalovat na Makables" action once the window has elapsed.
- **AC-3** Given an order in `Paid`, `Accepted`, or `Shipped` (no `DeliveredAt` yet), when the customer calls `OpenCustomerDispute.Command`, then no window check applies — the existing unlimited-while-in-flight behavior is unchanged (there's nothing to anchor a "from delivery" window to yet).
- **AC-4** Given an admin calls `OpenDispute.Command` (admin channel) or the carrier-sourced `DisputeShipment.Command` fires, then neither is affected by the 14-day window — both remain exactly as shipped in T-0106.
- **AC-5** Given a customer-opened (`Source = Customer`) `Dispute` is still open 7 days after `CreatedAt` with no maker `OrderMessage` posted on that order after the dispute opened, when the daily sweep Function runs, then a `dispute.autoEscalated.adminEmail` outbox event is enqueued exactly once; the dispute's `State` remains `Disputed` and `ResolvedAt` remains `null` (the sweep never resolves).
- **AC-6** Given the maker posts a reply on the order's message thread within 7 days of the dispute opening, when the sweep runs, then no escalation event fires for that dispute.
- **AC-7** Given a dispute is resolved (`ResolvedAt` set) before day 7, when the sweep runs, then it's excluded from the auto-escalation query (the sweep predicate is `ResolvedAt IS NULL`, the same idiom as the auto-deliver/auto-cancel sweeps).
- **AC-8** Given the sweep runs a second time against a dispute it already escalated, when it re-evaluates, then it does not enqueue a duplicate `dispute.autoEscalated.adminEmail` event (idempotency — e.g. a boolean flag or a check against outbox history, consistent with how `AutoDeliverOrdersFunction` avoids re-processing).
- **AC-9** Given the customer clicks "Reklamovat" on a `Delivered` order within the window, when the UI opens, then it lands in the existing order message thread (T-0079) with a `DisputeCategory` selector pre-filled — not directly in a standalone dispute form.

## Technical notes

- Precedent for the sweep Function: `AutoDeliverOrdersFunction` (T-0077) — thin MediatR-dispatch wrapper, `IAsyncEnumerable<string>` id-only projection, fail-continue per row.
- `Dispute` categories are already an enum (`DisputeCategory`) with two carrier-reserved values party Validators already reject — no change needed to the category set for this ticket.
- The "maker replied after dispute opened" check needs a query against `OrderMessage` scoped to the dispute's `OrderId` with `CreatedAt > Dispute.CreatedAt` and `SenderUserId` matching the maker's user — this may want a small addition to `IOrderMessageRepository` (a `HasMakerReplySinceAsync` or similar) rather than loading the whole thread.
- The escalation email recipient resolves the same way the existing `order.disputed.adminEmail` does (dispute.md: `EmailOptions.AdminNotificationAddress`).

## Files touched (expected)

- `backend/src/Makables.Core.AppServices/Features/Orders/OpenCustomerDispute.cs` — add the window guard.
- `backend/src/Makables.Core.Domain/Common/BusinessErrorMessage.cs` — new `order.dispute.windowExpired` code.
- `backend/src/Makables.Functions/DisputeAutoEscalationFunction.cs` (or similarly named) — new timer Function.
- `backend/src/Makables.Core.Domain/Orders/IOrderMessageRepository.cs` / `Infra.Database` impl — maker-reply-since check.
- `backend/src/Makables.Infra.Database/Migrations/` — seed migration for the new email template.
- `frontend/src/app/(customer)/objednavka/[id]/page.tsx` (or the order-detail component) — route "Reklamovat" into the thread; add the escalation action + window-aware visibility.
- `frontend/src/lib/i18n/cs-CZ.ts` — new `order.dispute.windowExpired` key.
- `docs/architecture/roles/dispute.md` — note the 14-day window + 7-day timer once shipped (currently documents pre-T-0145 behavior only).

## Test plan reference

`docs/test-plans/T-0145.md` (to be created by the implementer; cover the window boundary at exactly 14 days, the sweep's maker-reply-detection query, and idempotency of the escalation email).

## Status log

- 2026-07-07 `draft` by PM — added to the Phase 7 business-model-pivot manifest per dopady §6 work-package table, split (a) of the §2.5 reklamace package (T-0146 is split (b), depends on this ticket).
- 2026-07-07 `draft → ready` by BA. Wrote US-customer-0022 + US-maker-0019 with Given/When/Then AC + Alternatives Considered. Locked: window applies only to `OpenCustomerDispute` (admin/maker/carrier openers unaffected); timer anchors to `Dispute.CreatedAt` (not last-message); auto-escalation is notification-only, never auto-resolves and never sanctions the maker (T-0148's territory). No new open question raised.
