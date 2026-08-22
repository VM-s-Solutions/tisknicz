---
id: T-0176
title: "Admin action feedback: money-action confirmations, un-bricked rows, focus-trapped modals"
status: in_review
size: M
owner:
created: 2026-08-21
updated: 2026-08-21
depends_on: [T-0175]
blocks: []
user_stories: [US-admin-0009, US-admin-0012, US-admin-0013, US-admin-0018]
adrs: [0024]
phase: 8
manual_steps: []
security_touching: false
layers: [frontend, l10n]
---

# T-0176 — Admin action feedback + modal accessibility

## Context
Audit findings [ADM-H6, ADM-M5, ADM-M6, ADM-M10, ADM-L2, ADM-L3, ADM-L4, ADM-L6](../review/ux-functional-audit-2026-08-21.md).
The highest-stakes admin actions (refund, manual state change, complete payout batch) end with
**no** success confirmation — violating the project's in-viewport-confirmation rule exactly where
money moves; a category row bricks its own Edit button after a successful deactivate; outbox
success notices render inside the row the refresh unmounts; three modal shells claim a focus trap
none has; and several small feedback defects (shared busy spinner, boolean download errors,
never-disarming confirm buttons, non-prefilled fee override) compound it.

## Scope
- **Success confirmations:** refund, manual state change and complete-batch surface a page-level
  success Alert after the modal closes (pattern: maker-admin-actions) (ADM-M6).
- **Category row:** reset `busy` on the success path (ADM-H6).
- **Outbox:** hoist retry/ack results to a page-level notice that survives the row unmounting,
  including the retry count (ADM-M5).
- **Shared Dialog primitive** in `components/ui/`: initial focus, Tab containment, Escape,
  focus return — adopted by the three modal shells (ADM-M10).
- **Return-label card:** independent busy per button; after generation, show the label reference
  and keep the generate action guarded from an accidental second fire (ADM-L2).
- **Arm-confirm:** disarm on outside-click/Escape and a short timeout (both copies) (ADM-L3).
- **Fee override:** prefill the current override (ADM-L4).
- **Downloads:** invoice/payout-CSV errors go through `resolveErrorMessage` instead of a boolean
  (ADM-L6).
- cs-CZ keys; jest-axe on the new Dialog.

## Alternatives Considered
- **A toast system** — rejected: the codebase standardizes on in-flow `Alert`s; introducing a
  global toaster is a design-language change out of scope here.
- **Disabling the generate button permanently after success (return label)** — rejected: re-issue
  is legitimate; guard with confirm, not removal.

## Out of scope
- Reactivation actions (Q-0040 / T-0180 row). Backend idempotency (already shipped).

## Acceptance criteria
- **AC-1** Given a completed refund, when the modal closes, then a success confirmation is visible
  in-viewport naming the refunded amount (vitest + manual).
- **AC-2** Given a category deactivated successfully, then its row's Edit button remains usable.
- **AC-3** Given an outbox retry, when the row leaves the stalled set, then the page-level notice
  still reports the outcome.
- **AC-4** Given any admin modal open, then focus starts inside, Tab cycles within, Escape closes
  and returns focus to the trigger (jest-axe + keyboard test).
- **AC-5** Given a failed invoice download with an expired session vs a 500, then the two error
  messages differ accordingly.

## Technical notes
`category-row.tsx:57-72`, `outbox-row-actions.tsx:58-91`, `order-actions.tsx:88-142,195-341`,
`complete-batch-modal.tsx:72-102`, `return-label-form.tsx:51-112`,
`maker-fee-override-form.tsx:70`, `invoice-download.tsx:55-56`, `payout-csv-download.tsx:61-62`.

## Files touched (expected)
- `frontend/src/components/ui/dialog.tsx` (new shared primitive)
- `frontend/src/app/(admin)/dashboard/admin/**` (orders/[orderId], vyplaty, outbox, kategorie,
  makers/[id], faktury)
- `frontend/src/lib/i18n/cs-CZ.ts`

## Test plan reference
`docs/test-plans/T-0176.md`

## Status log
- 2026-08-21 `draft → ready` (Phase 8 UX sweep plan; DoR checklist passed)
- 2026-08-22 `ready → in_progress`, branch `feat/T-0176-admin-action-feedback`
- 2026-08-22 `in_progress → in_review` — tsc clean, vitest 215/215 (+9 new); shared focus-trapped
  `Dialog` adopted by all three modal shells; ADM-L2 (return-label card) deferred to the T-0179
  contract pass with rationale in the [test plan](../test-plans/T-0176.md); see
  [review run](../review/runs/T-0176.md)
