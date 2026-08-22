---
id: T-0186
title: "Admin shell redesign + platform earnings panel"
status: in_review
size: M
owner:
created: 2026-08-22
updated: 2026-08-22
depends_on: [T-0118a, T-0118c, T-0126]
blocks: []
user_stories: [US-admin-0002]
adrs: [0013, 0014, 0022, 0023]
phase: 8
manual_steps: [nswag-regen, ef-migration]
security_touching: true
layers: [dotnet-db, dotnet-backend, frontend, l10n, secops]
---

# T-0186 — Admin shell redesign + platform earnings panel

## Context
Reported directly by the user against the running admin console: *"navbar je vykryplenej"*.
Four concrete defects, all one root cause — a single header row was asked to hold a brand,
ten section links and an account block at once, with `justify-between` handing the nav as
much width as it wanted:

1. **"Makables Admin" wrapped** onto two lines once the nav squeezed it.
2. **The nav links wrapped into ragged rows** of uneven length.
3. **The operator's identity was cut off** — `max-w-48 truncate`, and it was not even the
   real sign-in but the hard-coded placeholder `"Administrátor"`.
4. **Logout was styled `dangerGhost`** — red-bordered, making the most routine and
   reversible control the loudest thing on the page.

Plus one addition: the overview reported order counts and ops counts but never answered
*"kolik jsme na prodejích udělali jako platforma"*, with a day / week / month filter.

## Decisions
- **Two header rows, not tuned spacing.** Row 1 = brand + identity + sign out; row 2 = the
  section rail. Splitting identity from navigation removes the width competition rather than
  balancing it.
- **Reuse the shared `DashboardNav`** the customer and maker dashboards already use, instead
  of the shell's private nav renderer + private mobile drawer. It scrolls horizontally rather
  than wrapping. Gained an optional `exact` flag (the overview href is a prefix of every
  other admin route) and an optional `ariaLabelKey`.
- **Rail spacing tightened** (`gap-2`→`gap-1`, `px-4`→`px-3`, icon gap `2`→`1.5`) because ten
  sections at the roomier spacing pushed "Audit log" past the 1280 content width. Measured:
  1183 px of 1216 available — 33 px slack, and it still scrolls if a section is added.
- **Real identity**, decoded (unverified, display-only) from the admin cookie the layout
  already reads — `getAdminDisplaySession()`, deliberately separate from `getDisplaySession()`
  which must never return an admin into the public account menu.
- **Revenue recognised at `PaidAt`**, not `CreatedAt` (an unpaid order earned nothing) and not
  `CompletedAt` (payout settles weeks later, so "today" would always read zero).
  `Paid | Accepted | Shipped | Delivered | Completed | Disputed` count; `PendingPayment`,
  `Cancelled` and `Refunded` do not.
- **Refunds are reported, never netted.** The refund column is a gross amount and does not
  decompose into a platform share and a maker share; netting it would understate commission
  by the maker's portion.
- **Rolling windows** (last 24 h / 7 d / 30 d), not calendar-aligned — a calendar month needs
  a civil timezone to know where the day starts, and this is a live operational readout. The
  invoice and payout surfaces stay the record for accounting periods.
- **Window lives in the URL** (`?earnings=`), so it survives a refresh and is shareable.

## Acceptance criteria
- **AC-1** Given the admin console at 375 / 768 / 1280, when the shell renders, then the brand
  occupies one line, all ten section links sit on one row, and the page never scrolls
  horizontally. *(Proof: Playwright sweep, Chromium + WebKit, all three widths.)*
- **AC-2** Given a signed-in admin, when the shell renders, then their actual e-mail is shown
  in full and un-clipped. *(Proof: `scrollWidth <= clientWidth` assertion + unit test.)*
- **AC-3** Given the sign-out control, then it does not carry the destructive weight, and a
  failed sign-out surfaces an error instead of failing silently.
- **AC-4** Given the overview, then it shows platform commission, gross volume, maker payout,
  paid-order count and refunds for the chosen window, with a day / week / month switch that
  survives a hard refresh.
- **AC-5** Given a customer or maker JWT, when `/api/v1/platform-revenue` is called, then it is
  rejected — the aggregate is cross-tenant money (ADR 0013).
- **AC-6** Given a window with no sales, then the panel shows zeros; given a failed read, then
  it says so rather than showing zeros as if they were real.

## Notes
- `AdminEnvelopePermitLimit` raised 30 → 180/min. The overview costs seven backend calls per
  render, so 30/min allowed roughly four page loads — switching the earnings window a few
  times started 429-ing, and an SSR 429 is invisible (the page still returns 200; the panels
  just report they could not load).
- New partial index `ix_orders_paid_at` (`paid_at IS NOT NULL AND is_active`) backs the window
  filter.
