---
id: T-0172
title: "Checkout + pre-payment flow: in-viewport errors, profile prefill, working downloads, honest confirmation"
status: done
size: M
owner:
created: 2026-08-21
updated: 2026-08-21
depends_on: []
blocks: []
user_stories: [US-customer-0010, US-customer-0011, US-customer-0013]
adrs: [0022, 0024]
phase: 8
manual_steps: []
security_touching: false
layers: [frontend, l10n]
---

# T-0172 — Checkout + pre-payment flow usability

## Context
Audit findings [CUST-H1, CUST-H3, CUST-H4, CUST-M2, CUST-M4, CUST-M5, CUST-L1, CUST-L3](../review/ux-functional-audit-2026-08-21.md).
The highest-value flow punishes users: failed submits render feedback off-viewport (the documented
"toast off-screen = bug" class), returning customers retype name/phone the profile already holds,
attachments on the pre-payment page are plain `<a>` tags resolving against the frontend origin
(404 in every environment, and the navigation destroys in-memory retry state), the confirmation
page shows a stale "detail připravujeme" banner and a wrong FailureView for already-cancelled
orders, and a refresh silently discards the whole form.

## Scope
- **Failed submit:** scroll to + focus the first errored field; error summary adjacent to the
  submit button; per-field errors clear on change (CUST-H3, L1).
- **Prefill:** pass `fullName`/`phone` from the already-fetched profile as editable defaults (CUST-H4).
- **Dirty guard:** `beforeunload` while the checkout form is dirty (CUST-M4).
- **Attachments:** replace the raw anchor with `FileDownloadButton` (blob download via
  `downloadOrderFile`), preserving the failed-upload retry state (CUST-H1); strip the stale
  `?attachmentsFailed=N` param after mount (CUST-L3); explain the 24 h auto-cancel on the
  PendingPayment surface (interim copy for the missing cancel action — see Q-0041 / T-0181 row).
- **Confirmation:** evaluate terminal order states before the `?status=` short-circuit; replace the
  `detailComing` copy with a link to the real detail; poller performs a final poll at the cap
  before switching views (CUST-M2, M5).
- cs-CZ keys for new copy; Chrome + WebKit at 375/768/1280 with a real order round trip.

## Alternatives Considered
- **sessionStorage draft persistence** — deferred: `beforeunload` covers the audited loss cheaply;
  drafts add cross-tab semantics that deserve their own slice if data shows abandonment.
- **Native browser validation instead of scroll/focus** — rejected: mirror-validation copy is
  Czech + backend-aligned; `noValidate` stays, focus management is added.

## Out of scope
- Customer cancel of an unpaid order (state-machine change — blocked on Q-0041, see T-0181 row).
- Payment provider changes; Packeta widget behavior.

## Acceptance criteria
- **AC-1** Given a checkout submit failing validation at 375 px, when the error renders, then the
  first errored field is focused and visible in-viewport (vitest + manual at 375/768/1280).
- **AC-2** Given a logged-in customer with a filled profile, when checkout opens, then name and
  phone are prefilled and editable.
- **AC-3** Given an uploaded attachment on `/objednavka/[id]`, when its name is clicked, then the
  file downloads (real file, manual proof) and failed-upload retry state survives.
- **AC-4** Given an order the webhook already cancelled, when the confirmation page loads with a
  failure `?status=`, then the cancelled state view renders — no promise of a pay button.
- **AC-5** Given a dirty checkout form, when the tab is closed or refreshed, then the browser warns.
- **AC-6** Given a payment still pending at the poll cap, when the budget expires, then one final
  poll runs before the "still verifying" frame.

## Technical notes
`order-form-client.tsx:69-288,500-507`, `objednavka/page.tsx:121-129`,
`attachment-manager-client.tsx:146-151`, `potvrzeni/page.tsx:111-136`,
`payment-poll-client.tsx:65-81`. `FileDownloadButton` precedent: `order-actions-client.tsx:14-18`.

## Files touched (expected)
- `frontend/src/app/(customer)/objednavka/**`
- `frontend/src/lib/i18n/cs-CZ.ts`
- tests under `frontend/src/app/(customer)/**/__tests__/`

## Test plan reference
`docs/test-plans/T-0172.md`

## Status log
- 2026-08-21 `draft → ready` (Phase 8 UX sweep plan; DoR checklist passed)
- 2026-08-21 `ready → in_progress`, branch `feat/T-0172-checkout-flow-usability` (main loop;
  stacked on the T-0170 branch — merges sequentially)
- 2026-08-21 `in_progress → in_review` — tsc clean, vitest 193/193 (+3 new);
  [review run](../review/runs/T-0172.md) (self, flagged); manual-browser items listed in the
  [test plan](../test-plans/T-0172.md) rows 5–9
- 2026-08-22 `in_review → done` — merged via PR #141 (merge 387c3af; CI green)
