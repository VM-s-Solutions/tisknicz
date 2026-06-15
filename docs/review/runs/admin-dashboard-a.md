# Final review — T-0118a (admin shell + /admin/login + 3 read views), PR 1 of L-split

**Branch:** `chore/admin-dashboard-grooming` (3 commits: 3c66c9e grooming-split, f8ef737 shell+login, 85725f5 reads).
**Verdict:** **REQUEST CHANGES** — 1 BLOCKER (L4: live dead forward-compat row links). Everything else passes.

---

## L1–L4 dispositions

### L1 — Invoice download: **PASS (correctly NOT wired).**
`faktury/invoice-download.tsx:24-36` ships a disabled `<span aria-disabled="true" title=…unavailable>` — no event handler, no fetch, no path. `admin-client.ts:269-288` keeps `downloadAdminInvoice` **commented-out** with the future blob shape; the live module exports only the 3 reads. `invoice-row.tsx:14` correctly renders the row as NOT a `<Link>`. No `csv` reference anywhere in the route. The payout `csv(id)` is never borrowed. Exactly Option E.

### L2 — Admin login isolation: **PASS.**
`admin-login-form.tsx` is a distinct component posting `login('admin', …)` (line 44). Open-redirect guard `/^\/(?![/\\])/` (line 33) is byte-identical to the customer precedent (`login-form.tsx:29`). Error map (lines 85-97) covers `auth.invalidCredentials / auth.locked / auth.forbidden / auth.oauthNotAllowedForAdmin` + generic — all keyed (`cs-CZ.ts:1040-1046`). Zero register/magic/OAuth `<Link>` (grep: only doc-comment + error-code mentions). No `api-client/` edit.

### L3 — Auth gate, no flash, no loop: **PASS.**
`middleware.ts:28` retargets **only** `audience === 'admin'` → `/admin/login`; customer/maker keep `/login` (regression-safe). `(admin)/dashboard/admin/layout.tsx:25-30` is `async`, reads `accessCookieName('admin')` via `next/headers`, and calls server-side `redirect()` before rendering children — no client flash. `/admin/login` lives under `(admin)/admin/login/` while the gate is in the nested `dashboard/admin/layout.tsx`; the group root `(admin)/layout.tsx` is a pass-through — login is OUTSIDE the gated subtree, no loop. Defense-in-depth (middleware + server gate) both present.

### L4 — Forward-compat links: **BLOCKER.**
**Nav** is clean: `shell-nav.tsx` LIVE_NAV are real `<Link>`s, PENDING_NAV render as `aria-disabled` `PendingNavEntry` non-links with a "Připravujeme" badge (Option H). Active uses `aria-current` (line 142).
**BUT the row detail links are live dead links.** `order-row.tsx:48-50` and `audit-row.tsx:43-45` wrap each row in `<Link href="/dashboard/admin/orders/{orderId}">` / `…/audit/{id}` — routes that **do not exist in slice a** (confirmed: no `[orderId]`/`[id]` subroute in the dirs; build route table shows neither, unlike `/dashboard/maker/objednavky/[orderId]`). These resolve to the not-found page in a's own flows. The code comment ("may 404 until b ships… the test plan covers the pending state") concedes this. AC-3 ("**no live nav link to a not-yet-built b/c route**"), AC-5 ("each row links forward… **not a live dead link**"), AC-8, and Option H all forbid it. Quoting the ticket §C: "a dead link that 404s is not acceptable." Deferring a live 404 to a manual test note is not the disabled/pending treatment required.

---

## BLOCKERs (must fix before approval)

1. **`order-row.tsx:48` + `audit-row.tsx:43`** — the row `<Link>` targets `/dashboard/admin/orders/{orderId}` and `/dashboard/admin/audit/{id}` do not exist until PR 2. Per AC-3/AC-5/AC-8/Option H, render the row as a non-`<Link>` (mirror `invoice-row.tsx`, which already does this) **or** as a visibly-disabled/pending affordance — not a live anchor that 404s. The implementer picks one and stays consistent with the nav's pending treatment.

---

## Fold list (non-blocking nits, fix or log)

- **`shell-nav.tsx:81-85`** — PENDING_NAV is rendered **only in the mobile menu**; the desktop `md:flex` nav (line 81) omits the pending sections entirely. Internally consistent with Option H ("omitted until their slice ships"), so not a blocker, but the desktop/mobile asymmetry is a UX nit — consider rendering pending entries in both or neither for parity.

---

## L1 / L4 backend follow-ups (log as Q-items if not already)

1. **Admin invoice-PDF-download endpoint** — absent from `admin-api.v1.ts` (only payout `csv(id)`). Needs `GET /api/v1/admin-invoices/{id}/pdf` on the admin host with `IInvoiceRepository.Unscoped()`. Blocks US-admin-0012 AC-2 enable. **Log a backend follow-up ticket.**
2. **Overview ops-count reads** — `Processing`-payout count + stalled-outbox count/signal not exposed by any existing read; tiles render "—" + `countFollowUp` info banner (correct, no fabricated data, no backend added). **Log a thin count-endpoint follow-up** (also the orders-list `dateFrom/dateTo` gap — `adminOrders` carries no date params; orders filter correctly omits date fields, no dead filter shipped).

---

## AC matrix

| AC | Verdict | Evidence |
|----|---------|----------|
| AC-1 | PASS | `middleware.ts:28` admin branch + `dashboard/admin/layout.tsx:25-30` server `redirect()`; path-only `redirect` param |
| AC-2 | PASS | `admin-login-form.tsx:44` `login('admin',…)`; guard:33; `forbidden` mapped:91; no register/magic/OAuth |
| AC-3 | **FAIL** | shell + `aria-current` OK; SC + force-dynamic OK; **but live dead row links (L4)** |
| AC-4 | PASS | `page.tsx` KPI tiles, `pageSize:1` probes, "—" + `countFollowUp` banner, no backend |
| AC-5 | **FAIL** | columns/`customerEmail`/`formatCzk`/badge/filters OK; **detail row `<Link>` is a live dead link (L4)** |
| AC-6 | PASS | `parsePositiveInt` clamp; unknown state→undefined; `page>1` only; filters round-trip |
| AC-7 | PASS | invoice list cols + **download disabled-with-tooltip (endpoint absent branch)** |
| AC-8 | **FAIL** | audit cols + date+time OK; **diff row `<Link>` is a live dead link (L4)** |
| AC-9 | PASS | per-list keyed empty states |
| AC-10 | PASS | `Alert variant="error"` + retry `<Link>` + `loading.tsx` per list |
| AC-11 | PASS (manual QA) | cards `<md` / grid `≥md` markup present; Vercel pass per test plan |
| AC-12 | PASS | read-only (no mutation call); 0 `any`/`console`/`TODO`; no `api-client/` edit; lint+build clean; check-consistency exit 0; no regen |

---

## Gates 1-7

- **Gate 1 (CLAUDE.md self-check):** PASS — SC-default, no `useEffect` fetch, all data via `apiFetch`+helper, no DB SDK, no business logic, i18n-keyed (vykání), no `any`/`console`/`!`.
- **Gate 2 (AC traceability):** FAIL — AC-3/5/8 blocked by L4.
- **Gate 3 (security):** PASS — read-only; per-host JWT audience + `Unscoped()` are backend locks; open-redirect guard present; no SecOps ping needed.
- **Gate 4 (extension-points / RDD):** PASS — no new aggregate/VO/repo; read-only frontend consumer; no role-file delta.
- **Gate 5 (TDD/pure-logic tests):** N/A — no new pure logic; frontend read slice, manual QA plan per `T-0118a.md`.
- **Gate 6 (contract parity):** PASS — no NSwag regen; `api-client/` untouched.
- **Gate 7 (hot-path/optimizer):** N/A — no hot path (KPI uses bounded `pageSize:1` probes, acceptable).

## Checks (run)
- `npx tsc --noEmit` → **0**.
- `npm run lint` → **clean**.
- `npm run build` → **clean** (all admin routes resolve; no `[orderId]`/`[id]` admin subroute exists — confirms L4).
- `node scripts/check-consistency.mjs` → **exit 0** ("clean (145 tracked)", T8/T9 green).
- Hygiene grep (`console.`/`any`/`TODO`) in `(admin)/` + `admin-client.ts` → **0**.
- Read-only grep (mutation verbs / POST-PUT-DELETE-PATCH) → **0 live calls** (doc-comment mentions only).
