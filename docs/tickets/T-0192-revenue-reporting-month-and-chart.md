---
id: T-0192
title: Monthly earnings report + revenue-over-time chart in the admin console
status: in_review
size: M
owner: claude
created: 2026-08-23
updated: 2026-08-23
depends_on: [T-0186]
blocks: []
user_stories: []
adrs: []
phase: 8
manual_steps: []
security_touching: false
layers: [backend, frontend]
---

# T-0192 — Monthly earnings + revenue chart

## Context

Operator: *"Uprav v admin panelu prehledy zisku, at je to pro dany mesic, pridej
graph.js, kde uvidime, jak se vyviji trby v case, jako bychom koukali na vyvoj
ceny na burze vc. filtrovani od 1 dne po cely rok."*

[T-0186](./T-0186-admin-shell-and-earnings.md) shipped the earnings panel over a
ROLLING day/week/month window. Rolling was chosen to avoid needing a civil
timezone, and it produced a number that reconciles against nothing: "the last 30
days" matches no invoice run and no VAT period, and it changes on every refresh.
Two surfaces replace it:

- the panel answers for a **calendar month**, the unit the business already
  accounts in, with a previous/next navigator;
- a **chart** answers the trend question a rolling window was standing in for,
  spanning 24 hours to 12 months.

The timezone a rolling window dodged is now read from
`CountryConfiguration.TimeZoneId` — never a hardcoded `Europe/Prague`.

## Acceptance criteria

- **AC-1** — The earnings panel reports one calendar month and can page between
  months, defaulting to the month in progress.
  *Proof:* browser-driven, Chromium + WebKit — opens on `srpen 2026`; the
  previous-month link is `?range=Quarter&metric=fee&month=2026-07`; clicking it
  shows `červenec 2026` with its own total (5 466 Kč vs August's 432 Kč).
- **AC-2** — A month is the OPERATOR'S month, not UTC's.
  *Proof:* `AdminPlatformRevenueIntegrationTests` — an order paid
  `2026-04-30T22:30Z` is reported under **May**, and May's window is
  `[2026-04-30T22:00Z, 2026-05-31T22:00Z)`. Unit: DST, month lengths and the
  local new year in `RevenueReportingCalendarTests`.
- **AC-3** — The operator cannot page into a month that has not started.
  *Proof:* `IsCurrentMonth` on the response; the next control renders
  `aria-disabled` on the current month and returns once off it (browser-checked
  in both engines).
- **AC-4** — A chart shows revenue over time, filterable from 1 day to 1 year.
  *Proof:* six ranges verified in-browser with their exact bucket counts —
  1 den → 24, 7 dní → 7, 30 dní → 30, 3 měsíce → 90, 6 měsíců → 26, 1 rok → 12,
  each with a painted canvas and the chosen range marked `aria-current`.
- **AC-5** — Series buckets are LOCAL days and sum to the single-number read.
  *Proof:* integration — an order paid 30 min after Prague midnight (a
  *previous* UTC calendar day) lands in the local day's bucket; three orders on
  three local days sum to `3 × fee` across three distinct buckets.
- **AC-6** — Empty periods are plotted as zero, never skipped.
  *Proof:* integration — one order in a 7-day range yields 7 points, 6 of them
  zero, ascending and unique.
- **AC-7** — Recognition matches the month aggregate state-for-state, including
  the soft-delete exclusion the raw SQL has to spell out itself.
  *Proof:* integration — `PendingPayment` / `Cancelled` / `Refunded` /
  soft-deleted all excluded from the fee; a partial refund rides the refund line
  without reducing commission.
- **AC-8** — Both money reads stay admin-only and cross-tenant-safe.
  *Proof:* integration — customer and maker JWTs and anonymous requests all 401
  against both endpoints.
- **AC-9** — Hand-typed URL params degrade instead of 400ing.
  *Proof:* `?month=2026-13&range=Nope&metric=bogus` renders `srpen 2026` with
  the chart intact, in both engines.
- **AC-10** — The chart is theme-aware, responsive and reachable without a
  pointer.
  *Proof:* repaints with `#0d6b62` (light) and `#2dd4bf` (dark); painted with no
  horizontal overflow at 375 / 768 / 1280; every plotted value is also published
  in an `sr-only` table; `jest-axe` clean.

## Notes

- **Why raw SQL.** Bucketing needs `date_trunc(field, timestamptz, zone)` — the
  three-argument form added in PostgreSQL 16 — so a "day" is a day in the
  operator's civil timezone. Npgsql EF Core 10 has no translation for it
  (`EF.Functions.DateTrunc` does not exist in the package), so the query is a
  parameterised `FromSqlInterpolated` over a keyless projection type, following
  the house precedent (`MakerRepository`, `ProductRepository`,
  `NumberingSequenceAllocator`). Every hole is a real query parameter.
- **The keyless type sits outside the global soft-delete filter**, so the SQL
  carries `is_active` itself. That duplication is pinned by an integration test.
- **Bounded reads.** Every range returns 12–92 points regardless of table size;
  the scan is served by the existing `ix_orders_paid_at` partial index
  (`paid_at IS NOT NULL AND is_active`), so no migration was needed.
- **One series, one axis.** Turnover is ~7× commission; plotting them together
  would need a second y-scale, whose alignment is arbitrary and invents
  correlation. Switching measure re-renders one line instead.
- Chart.js is loaded through `next/dynamic` and registers only the line
  controller, so it never enters the admin bundle for operators who only read
  the KPI tiles.

## Contract change

`GET /api/v1/platform-revenue` — `?window=` replaced by `?year=&month=`;
response gains `year`, `month`, `isCurrentMonth`.
`GET /api/v1/platform-revenue/series?range=` — new.
NSwag admin client regenerated; `npm run check:api` green.
