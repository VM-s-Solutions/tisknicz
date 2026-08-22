---
id: T-0174
title: "Maker product + review forms: unlock reply form, upload timeout, in-viewport save feedback, multi-upload"
status: in_review
size: M
owner:
created: 2026-08-21
updated: 2026-08-21
depends_on: []
blocks: []
user_stories: [US-maker-0004, US-maker-0014]
adrs: [0022, 0024]
phase: 8
manual_steps: []
security_touching: false
layers: [frontend, l10n]
---

# T-0174 — Maker product + review form feedback

## Context
Audit findings [MAKER-H1, MAKER-H2, MAKER-M1, MAKER-M2, MAKER-M6 (frontend half), MAKER-M7, MAKER-L6](../review/ux-functional-audit-2026-08-21.md).
The review reply form permanently locks after a **successful** submit (success path never resets
`submitting`); product image uploads run on the documented 8 s apiFetch trap and misreport the
abort as "invalid file"; the product form's save confirmation renders off-viewport (the exact bug
class `SaveButton` was built for — and the profile page already uses it); unsaved edits are lost
silently; creating a product silently lands on the edit page with no hint it is already live; and
adding 8 photos takes 8 pick-and-wait cycles with the 10-image cap discoverable only via a 409.

## Scope
- **Reply form:** reset `submitting`/`inFlightRef` after refresh trigger (mirror the
  order-actions `useTransition` pattern); wire the unused edit/cancel toggle so the pre-filled
  form isn't permanently open under an existing reply (MAKER-H1, L6a).
- **Upload timeout:** `uploadProductImage` passes `timeoutMs: 120_000` (the `UPLOAD_TIMEOUT_MS`
  constant); abort maps to a truthful timeout message, not "invalid file" (MAKER-H2).
- **Product form:** switch to the shared `SaveButton` with dirty tracking (profile-client
  pattern); auto-clear the stale success flash on edit; scroll/focus to the first error on failed
  submit; `beforeunload` guard while dirty, create + edit (MAKER-M1, M2).
- **Create → edit handoff:** `?created=1` renders "Produkt je vytvořený a veřejný — teď přidej
  fotky" + a link to the public product page (MAKER-M7).
- **Image manager:** `multiple` file picker with a sequential upload queue + per-file result,
  visible "N/10" indicator; interim guidance in the Paid-order action bar for the missing decline
  path ("Nemůžeš objednávku vyrobit? Napiš zákazníkovi…" → message thread) rides here (MAKER-H3
  interim; full decline is Q-0041 / T-0181 row).
- **Product grid:** active/inactive filter so soft-deleted products stop cluttering; hide "Smazat"
  on already-inactive cards and state irreversibility in the confirm copy until reactivation
  exists (MAKER-L6b; reactivation itself is Q-0040 / T-0180 row).
- cs-CZ keys; upload verified with a real multi-MB file — uploaded **and rendered after**.

## Alternatives Considered
- **Parallel uploads** — rejected: sequential keeps per-file errors attributable and stays inside
  the backend's one-file endpoint without burst pressure.
- **Blocking in-app navigation via router events for the dirty guard** — Next App Router has no
  stable route-change interception; `beforeunload` + confirm on the explicit back-link covers the
  audited losses.

## Out of scope
- Set-primary/reorder of images and any new backend contract (draft T-0179).
- Decline/reactivation state-machine changes (T-0180/T-0181 rows, Q-0040/Q-0041).

## Acceptance criteria
- **AC-1** Given a successful review reply, when the list refreshes, then the form is re-enabled
  and shows the saved reply with an edit affordance (vitest + manual).
- **AC-2** Given a 15 MB-class photo on a slow uplink, when upload exceeds 8 s, then it still
  completes (manual proof: real file uploaded and visible afterwards; no 499 in the backend log).
- **AC-3** Given a failed product-form submit at 375 px, when errors render, then the first error
  is focused in-viewport; given a successful save, then confirmation appears at the button
  (SaveButton) and the stale flash clears on the next edit.
- **AC-4** Given a new product just created, when the edit page opens, then the created notice and
  the public-page link are visible.
- **AC-5** Given 3 files picked at once, when the queue runs, then each shows its own result and
  the counter reads correctly; the 10-cap is visible before hitting it.
- **AC-6** Given unsaved changes, when the maker closes the tab, then the browser warns.

## Technical notes
`reply-form.tsx:39-56` (contrast `order-actions.tsx:105-135`), `maker-products.ts:253-264`
(constant precedent `profile.ts:152,176`), `product-form.tsx:145-344`,
`image-manager.tsx:66,103-188`, `save-button.tsx:21-35`.

## Files touched (expected)
- `frontend/src/app/(maker)/dashboard/maker/{recenze,produkty}/**`
- `frontend/src/app/(maker)/dashboard/maker/objednavky/[orderId]/order-actions.tsx` (interim copy)
- `frontend/src/lib/api-client-helpers/maker-products.ts`
- `frontend/src/lib/i18n/cs-CZ.ts`

## Test plan reference
`docs/test-plans/T-0174.md`

## Status log
- 2026-08-21 `draft → ready` (Phase 8 UX sweep plan; DoR checklist passed)
- 2026-08-21 `ready → in_progress`, branch `feat/T-0174-maker-form-feedback` (main-loop
  implementation — parallel agents unavailable, account session limit)
- 2026-08-21 `in_progress → in_review` — tsc clean, vitest 185/185 (+12 new tests); self-review
  pass recorded in [review run](../review/runs/T-0174.md) (reviewer agent unavailable, flagged);
  real-browser maker-session pass pending per [test plan](../test-plans/T-0174.md) row 6.
  Follow-up noted: backend is-active filter param for the product list (display filter ships now)
