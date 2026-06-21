---
ticket: T-0135
author: QA
created: 2026-06-21
adrs: [0023]
kind: manual-checklist
gate: pre-launch (NOT a merge gate)
---

# Terminal bug bash — final smoke checklist (T-0135)

The MVP-closing manual pass. Every automated gate (1745 backend unit + the
backend integration suite + the vitest a11y/SEO/link-hygiene suite +
`check-consistency` T1–T9) runs green on every PR; this checklist is the
**human end-to-end smoke** against a deployed, seeded staging environment —
the things that only a real browser against a real backend + real provider
sandboxes can confirm. Per the user-locked Phase-6 decision the **RUN is a
`manual_step`** executed once before launch (and on any major release); it is
**NOT a merge gate**. A finding here becomes a follow-up ticket — it does not
block the T-0135 PR.

The code-side half of T-0135 (the static bug-sweep + the confirmed fixes +
the link-hygiene regression test) ships in the PR; this checklist is the
gated staging RUN that complements it.

## Preconditions

- A deployed **staging** build (Web.{Customer,Maker,Admin,Public} + Functions
  + Next.js frontend) against a seeded Postgres + Azurite/blob.
- Provider **sandboxes** wired: Comgate test merchant, Packeta test API,
  ARES (live read-only is fine), SendGrid sandbox / a real inbox you control,
  Mapbox token. NEVER point staging at production provider credentials.
- At least one seeded **customer**, one **maker** (verified, with IČO + bank
  account), one **admin**, and a small catalog (≥1 category, ≥2 products).
- A throwaway email inbox for the email-flow rows.
- Browser DevTools open (Network + Console) — a console error or a 4xx/5xx on
  a happy path is a finding.

## How to record

For each row: **PASS** / **FAIL** / **N/A (not deployed)**. A FAIL gets a
one-line repro + a screenshot/HAR and a follow-up ticket id. File follow-ups
in `docs/questions/open.md` or as new `T-####` rows, not inline here.

---

## A. Public / acquisition surface (Web.Public + frontend, anonymous)

1. [ ] Landing `/` renders; every CTA navigates to a real route (no 404).
   **Specifically re-verify the maker CTA** — `/register?role=maker` (NOT
   `/auth/register...`; the `(auth)` group adds no segment — the T-0135
   link-hygiene fix + regression test guard this, confirm in the browser).
2. [ ] `/katalog` lists makers; pagination + filters work; empty-filter state
   renders (no crash, a friendly empty message).
3. [ ] `/katalog/{slug}` maker profile renders; products link through.
4. [ ] `/produkt/{productId}` product detail renders; gallery works; the
   order CTA is present and routes to checkout.
5. [ ] Static pages `/jak-to-funguje`, `/pro-makery` render real content.
6. [ ] `/vop` + `/gdpr` render the PLACEHOLDER banner (legal text is the
   Q-0030 launch-blocker — confirm the placeholder is visible, not invented
   legal copy).
7. [ ] `/sitemap.xml` + `/robots.txt` resolve; sitemap lists the static
   routes + maker slugs (set `NEXT_PUBLIC_SITE_URL` first — SEO launch item).
8. [ ] A deliberately-bad URL hits the `not-found` page (no raw stack trace).

## B. Auth (Web.* shared AuthController + frontend (auth) pages)

9. [ ] `/login` → forgot-password (`/reset`), magic-link (`/magic`), and
   register (`/register`) links all resolve (route-group-link fix).
10. [ ] Register a new customer → email-confirm flow → confirm → log in.
11. [ ] Login with wrong credentials shows a translated error (not a raw
    code / English fallback).
12. [ ] Password-reset request → email → reset → login with the new password.
13. [ ] Magic-link request → email → consume → logged in.
14. [ ] **Rate limit (T-0136):** hammer `/login` >10×/min from one client →
    a 429 appears (the auth bucket). Then confirm `refresh`/`logout` do NOT
    429 under normal multi-tab use (they're `[DisableRateLimiting]`-excluded).
15. [ ] Logout clears the session; protected pages redirect to `/login`.
16. [ ] A customer JWT cannot reach the maker or admin API (audience
    isolation) — try a maker-host call with a customer session → 401.

## C. Customer order happy path (the money path — Web.Customer + Comgate)

17. [ ] Browse → product → **place an order**; the total + VAT + shipping
    breakdown matches the displayed numbers (`1 234 Kč`, space thousands).
18. [ ] Pick a Zásilkovna pickup point (the Packeta widget loads + returns a
    point); the order captures it.
19. [ ] Pay via the **Comgate sandbox** → redirect back → the confirmation
    page polls and flips to **Paid** (server-verified — not the redirect
    params). Confirm the order state actually moved server-side.
20. [ ] The customer sees the order in their dashboard with the correct
    state + the invoice download (PDF streams; recipient PII present).
21. [ ] **Pay-then-cancel / failure path:** start a payment, abandon it →
    the order stays `PendingPayment`; the 24h auto-expire (T-0083) is a timer
    (verify the row is selected by the expiry query, or force-run the
    Function and confirm it cancels).
22. [ ] Order messages: post a message (≤2000 chars) to the maker; it
    appears; the 5-min email debounce holds (no email storm on rapid posts).

## D. Maker fulfilment (Web.Maker)

23. [ ] Maker sees the new paid order; accepts it; ships it (captures a
    Packeta label — the label PDF stores + downloads).
24. [ ] Maker does NOT see the customer's email (GDPR data-minimisation —
    only ContactName/Phone on the maker order detail).
25. [ ] Auto-deliver (T-0077): a Shipped order older than 7 days flips to
    Delivered (force-run the timer or verify the selection query).
26. [ ] Maker product CRUD: create/edit a product, upload an image
    (server-side type+size validation rejects a bad file), deactivate.

## E. Admin control plane (Web.Admin)

27. [ ] Admin lists all orders / invoices / payout batches (cross-tenant).
28. [ ] Admin opens an order detail (full un-redacted contact snapshot) →
    confirm an `admin_audit_log` row `order.detail.view` lands (T-0137).
29. [ ] Admin downloads an invoice PDF → `invoice.pdf.download` audit row;
    downloads a payout CSV → `payout.csv.download` audit row. A 404 (bad id)
    writes NO row.
30. [ ] Admin manual state change / refund (T-0105/T-0107) works and is
    audited; a refund moves money back (Comgate sandbox) + records the row.
31. [ ] **Run the weekly payout batch** (admin "run batch now" / HTTP
    trigger): a batch is created, per-maker Fee invoices issue, the bank CSV
    builds; a second run returns the same batch (idempotent, not a double-pay).
32. [ ] Outbox health: `GET /outbox-events/stalled/count` + the admin outbox
    view reflect reality (no rows stuck `Permanent` on the happy path).

## F. Background jobs / integrations (Functions)

33. [ ] Functions host **boots** (all `%...:Schedule%` keys + the outbox
    queue names resolve — a missing key fails indexing at boot).
34. [ ] The outbox drains end-to-end: placing an order produces the expected
    emails (order confirmation) + invoice generation, each exactly once
    (re-delivery is idempotent — no double-send).
35. [ ] SyncShipmentStatuses pulls Packeta status for an in-flight shipment.
36. [ ] Every cron/Function HTTP trigger (outbox process, payout run-batch)
    rejects an unauthenticated call (function key / CRON_SECRET).

## G. Cross-cutting

37. [ ] No `console.*` errors on any happy-path page (DevTools console clean).
38. [ ] No untranslated raw error CODE shown to a user on any error path
    (every `BusinessErrorMessage` has a cs-CZ key — T8 enforces this in CI;
    spot-check a few live error states).
39. [ ] Responsive smoke at 375 / 768 / 1280 on the landing + catalog +
    product + checkout pages (no broken layout, no horizontal scroll).
40. [ ] Dates render Czech short format (`9. 5. 2026`); currency `1 234 Kč`.

---

## Known carry-over follow-ups (from the T-0135 static sweep — NOT blocking this PR)

The static bug-sweep that accompanied this checklist confirmed and FIXED the
launch-blocking dead-CTA bug (five `/auth/*` route-group links → 404, now
fixed + guarded by `route-group-link-hygiene.test.ts`). It also surfaced two
lower-severity items left as **post-launch polish follow-ups** (not fixed in
the T-0135 PR to keep the bug-bash scoped to launch-blockers):

- **Hardcoded Czech on the landing `/` + `not-found` pages.** The homepage
  hero/steps/CTA copy and the 404 page render inline Czech rather than `home.*`
  / `error.*` i18n keys. It's correct Czech (cs-CZ-only launch), so it is NOT a
  user-facing defect today — but it bypasses the i18n layer. Follow-up: lift to
  keys when a second locale is on the roadmap.
- **No `error.tsx` on `/produkt/[productId]`.** The product route has
  `loading.tsx` + `not-found.tsx` but no error boundary; an unexpected throw in
  the Server Component would surface the framework error page. Follow-up: add a
  route-level `error.tsx` with a translated fallback.

Several "BLOCKER/HIGH" candidates raised by the automated sweep were
**adversarially verified and REFUTED** against the code (kept here so the next
bash doesn't re-litigate them):

- Comgate PAID-webhook on a Refunded order is **already handled** (logged
  Warning "manual refund required", idempotent 200 — no money/state corruption;
  `ComgateWebhookController.cs:192-198`).
- Missing `OutboxQueues:Generate{Invoice,Label}QueueName` in dev
  `local.settings.json` does **not** crash the host — the options properties
  carry defaults the binder leaves intact (`OutboxQueueOptions.cs:41,52`).
- The outbox "park-before-publish" ordering is **correct by design** (a
  consumer can't dequeue a message that Phase 3 hasn't published yet;
  `IOutboxDispatcher.cs:102-105`).
- The weekly-payout timer "no-retry" is a **deliberate** alert-and-manual-rerun
  design (failed run rolls back → unclaimed orders picked up next run / HTTP
  re-fire; `RunWeeklyPayoutBatchFunction.cs:29-36,56-58`).
