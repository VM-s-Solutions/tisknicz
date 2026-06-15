# Preliminary review notes — T-0118a (admin shell + /admin/login + 3 read views)

**Mode:** PARALLEL preliminary (written while implementer codes). PR not yet open — these are
landmines to verify at final review, grounded in the ticket, patterns.md §B, the T-0087a/T-0116
precedents, and the *actual current* state of the repo.

**Slice scope:** read-only + auth gate. PR 1 of 2 (a alone; b+c bundled in PR 2). T-0118a is the
verification harness the write slices assert against — it must land complete and correct.

---

## Grounding confirmed (current repo state, on branch)

- `admin-api.v1.ts` exposes `adminOrders / adminInvoices / auditLog` (the three reads, signatures
  match the ticket §Context verbatim) + `csv(id): Promise<void>` (payout bank file, slice c).
  **There is NO admin invoice-PDF-download method on the contract.** Verified by enumerating the
  client surface (lines 21/33/45 reads; 95 `csv`; no invoice-pdf method anywhere).
- `login(host, input)` helper (`lib/api-client-helpers/auth.ts:54`) ALREADY takes `host` as its
  first arg → the admin form calls `login('admin', …)` with **no helper change** (and no
  `api-client/` edit — pre-commit hook clean).
- The existing `LoginForm` (`(auth)/login/login-form.tsx`) is **hardcoded to `login('customer', …)`**
  (line 40), maps only `auth.invalidCredentials / auth.locked / auth.emailNotConfirmed`, and ships
  **register + magic-link + forgot-password** links. It is NOT host-parameterized today. The admin
  form is a **separate** `admin-login-form.tsx` (ticket §C "thin variant"), not a `host=` reuse.
- `middleware.ts:24` redirects **every** audience (incl. admin) to `/login`. The slice retargets
  **only** the `audience === 'admin'` branch to `/admin/login`.
- `accessCookieName` + `Audience` exported from `lib/auth` (index re-exports session) — the server
  gate reads `accessCookieName('admin')`.
- `formatCzk(amountMinor, currency)` lives at `lib/money/formatter.ts:34` (ticket cites it correctly;
  it asserts CZK — see §B.10, non-CZK guarded at the card boundary).
- `triggerBlobDownload` precedent is local-per-route (defined inline in
  `vyplaty/[batchId]/fee-invoice-download.tsx:20` and `objednavky/[orderId]/order-actions.tsx`) —
  the admin invoice island, IF wired, mirrors that shape (`apiFetch parse:'blob'` + programmatic
  anchor), never the generated `Promise<void>` file method.
- i18n catalog currently has only `error.forbidden` — NOT `auth.forbidden` /
  `auth.oauthNotAllowedForAdmin`. Those are NEW `login.*` UI keys this slice adds.

---

## HIGH-RISK landmines (verify first at final review)

### L1 — Invoice-download endpoint is ABSENT → button MUST ship disabled, not wired (AC-7, Option E)
**Confirmed gap.** The contract has no admin invoice-PDF method. Per Option E + AC-7's "absent"
branch + the §Technical-notes verification step, the implementer MUST:
- render "Stáhnout fakturu" **disabled-with-tooltip** (keyed tooltip), and
- **log a thin backend follow-up ticket** for the admin invoice-streaming endpoint, and
- NOT invent a path, NOT wire the generated `csv(id)` (that's the payout bank file — would leak
  cross-maker PII AND return the wrong document), NOT call a `Promise<void>` generated file method.
**REJECT** if the button is wired to any guessed/borrowed path, or `downloadAdminInvoice` calls
anything that resolves. The `admin-client.ts` `downloadAdminInvoice` helper should either be absent
or guarded behind the confirmed-absent branch. Grep the route+helper for `csv` — must be zero
(slice-a has no CSV business, same discipline as T-0116 AC-10).

### L2 — Admin login form must NOT be a customer-login reuse; no register/OAuth/magic affordance (AC-2, A.1)
The shipped `LoginForm` posts `login('customer', …)` and renders register/magic/forgot links. The
admin form is a **distinct** component posting `login('admin', …)` (per-host audience, ADR 0013) with
**zero** register / magic-link / OAuth affordance (US-admin-0001: admins are provisioned). Verify:
- `admin-login-form.tsx` calls `login('admin', …)` — grep for the literal `'admin'`.
- No `<Link>` to `/auth/register`, `/auth/magic`, `/auth/reset` (or any OAuth button).
- Error mapping covers `auth.invalidCredentials / auth.locked / auth.forbidden /
  auth.oauthNotAllowedForAdmin` → keyed `login.*` messages (the existing `mapLoginError` covers only
  the first two + `emailNotConfirmed`; the admin map MUST add `forbidden` + `oauthNotAllowedForAdmin`).
  A non-admin's correct creds → `auth.forbidden` keyed message, no session (AC-2). **These are
  UI-only keys — T8 will NOT catch a missing one** (T8 only pairs `BusinessErrorMessage` codes to
  `cs-CZ`); the reviewer must eyeball that every mapped branch has a real key.
- Open-redirect guard on `redirect` is the path-only regex precedent (`login-form.tsx:29`) — verify
  copied, not dropped.

### L3 — Auth-gate soundness: clean redirect, no content flash, no login loop (AC-1, AC-3)
- **Middleware:** ONLY the admin branch retargets `/login → /admin/login`; customer/maker branches
  untouched (regression risk — T-0087a/T-0116 makers still bounce to `/login`). Verify the diff
  touches one branch, with a path-only `redirect` param preserved.
- **Server gate in `(admin)/layout.tsx`:** reads `accessCookieName('admin')` via `next/headers`,
  `redirect('/admin/login?redirect=…')` when absent — server-side `redirect()`, so unauthenticated
  pages **never render children** (no content flash; the ticket's explicit concern). REJECT if the
  gate is a client-side effect or renders the shell before checking.
- **Login-loop guard:** `/admin/login` must sit OUTSIDE the gated subtree (the ticket allows
  `app/(admin)/admin/login/` or a sibling group). If it's inside the gate, an unauthenticated admin
  loops. Confirm the route placement + that the gate excludes it.
- **Defense-in-depth, not replacement:** both layers present (middleware is presence-only until
  T-0027; the server gate is the real check). Both AC-1 mechanisms must exist.

### L4 — Forward-compat links must be graceful, never live 404s (AC-3, Option H)
The b/c detail/diff links and the b/c nav sections (payouts/outbox/makers/country-config/users) point
at routes that **do not exist in slice a**. Verify:
- Order-row detail link `/dashboard/admin/orders/{orderId}` → slice b. Audit-row diff link
  `/dashboard/admin/audit/{id}` → slice c. These render **disabled/pending** OR are noted as
  "coming in PR 2" — NOT a live anchor that 404s in a's own flows.
- b/c **nav entries**: disabled/visibly-pending OR omitted until their slice ships — implementer
  picks ONE approach and is consistent. **No live nav link to a not-yet-built route.** Grep the
  shell for hrefs to `/dashboard/admin/{vyplaty,outbox,makers,...}` and confirm each is
  disabled/absent.
- Active section uses `aria-current` (AC-3).

---

## MEDIUM checks (Server-Component-first, URL-state, money, parity)

### M1 — Server-Component-first (AC-3)
- Shell + overview + all 3 list `page.tsx` are Server Components, `export const dynamic =
  'force-dynamic'`. The ONLY `'use client'` islands allowed: `admin-login-form.tsx`, the (disabled)
  invoice-download island, and the filter bars **must remain server `<form method="get">`** (NOT
  `useState` — Option G reject). Grep the route group for `'use client'` — anything beyond the login
  form + download island is a finding.
- **Zero `useEffect` data fetching** anywhere in `(admin)/` (AC-3, §B.1).
- All data via `lib/api-client-helpers/admin-client.ts` → `apiFetch` (§B.4/§B.16); no raw `fetch`,
  no direct `api-client/` import in route code.
- SSR cookie forwarding is automatic via §B.14 (admin-audience cookie) — the helper passes no
  `makerId`/scoping id (backend `Unscoped()` owns IDOR).

### M2 — URL-state pagination + filters (AC-5, AC-6, §B.8)
- `page`/filters in the URL; `parsePositiveInt` clamp precedent; junk (`page=0`, `page=abc`, unknown
  `state`) clamps/drops without an error page (backend Validator authoritative). `page=1` dropped
  from canonical URLs. Verify the `Pagination` is a local copy per base path (Option F reject on a
  shared mega-component; per-list pagination/loading/error mirrors `objednavky`/`vyplaty`).
- Date-range params on orders: the generated `adminOrders` signature is
  `(page,pageSize,state,country,makerId,customerEmail)` — **it does NOT take dateFrom/dateTo**.
  `adminInvoices` and `auditLog` DO. So the orders date-range filter has **no contract param** —
  the implementer must omit it cleanly and log the gap (ticket §C anticipates exactly this:
  "date-range params are passed iff the generated signature accepts them … omits cleanly otherwise
  and logs the gap"). REJECT if the orders list invents a date param or silently renders a dead
  date filter.

### M3 — Money + dates (AC-5, AC-7)
- Every money figure via `formatCzk(totalAmountMinor|totalMinor, currency)` — no client arithmetic.
- Czech short dates via `formatDate`; audit uses date+time (`formatDateTime`).
- `state` badge reuses existing `order.state.*` keys via the `Badge` primitive.
- Admin orders list renders `customerEmail` — **intentional and correct** (admin DTO carries it; the
  maker GDPR lock keeps email out of the *maker* view only — ticket §Technical-notes). NOT a leak.

### M4 — Overview KPIs from existing reads only; missing count → "—", no backend (AC-4, A.2)
- Counts from existing T-0111/0109/0106/0102 reads. Any count an existing read doesn't expose
  renders **"—"** (graceful, no throw) and still deep-links to the underlying list; a missing-count
  is logged as a follow-up. **NO count-aggregation backend added in this frontend slice** (Option C
  reject). Stalled-outbox red banner surfaces ONLY if the source read exposes the signal; otherwise
  the banner condition is logged. Verify no over-fetch of full lists to compute counts client-side
  (Option B reject).

### M5 — read-only scope gate (AC-12)
- Grep the whole `(admin)/` route group + `admin-client.ts` for mutation calls:
  `refund / state / resolve / complete / retry / erase / dispute / acknowledge / payoutBatches /
  countryConfigurations` — **must be zero**. `admin-client.ts` exports only the 4 reads (+ the
  conditional/disabled invoice blob).
- **No type-to-confirm affordance anywhere** (reserved for slice c GDPR-delete, A.3).

---

## T8 GATE (codified — mechanical, but verify the run is clean)

Per recurring-findings #2, T8 i18n-parity is now `hard:true` in `check-consistency.mjs` (`ruleT8`,
`BEM_PATH`↔`CS_CZ_PATH`). It pairs every `BusinessErrorMessage` code to a `cs-CZ.ts` key (or the
`T8_NO_KEY_REQUIRED` allowlist). **A missing key fails the BUILD, not your review.**

- **Verify the implementer ran `node scripts/check-consistency.mjs` and it exited 0** (AC-12 names
  it). This slice adds **no** `BusinessErrorMessage` codes (frontend-only, read-only) → T8 should be
  trivially green; the risk is only if the implementer touched a backend error file (they shouldn't).
- **CAVEAT the implementer/PM should know:** T8 covers **backend-error-code ↔ cs-CZ** parity. It does
  **NOT** cover the slice's NEW UI-only keys (`dashboard.admin.nav.*`, `login.*`,
  `overview.*`, `orders/invoices/audit.*` headers/empty/error). A missing *UI* key renders the raw
  key string at runtime and **passes** T8/build. So the L2 login error-map keys and every
  `dashboard.admin.*` string still need a **human eyeball** — T8 is necessary, not sufficient here.
- Zero hardcoded Czech outside `cs-CZ.ts`; vykání tone (admin = operator, V form — differs from the
  maker tykání). No `console.*`, no `any`, no unsafe `!`.

---

## AC traceability map (for final review)

| AC | What to verify | Primary risk |
|----|----------------|--------------|
| AC-1 | Unauth `/dashboard/admin/*` → `/admin/login` (middleware admin branch + server gate), path-only redirect | L3 |
| AC-2 | `login('admin',…)`; non-admin → keyed `auth.forbidden`; no register/magic/OAuth | L2 |
| AC-3 | Real shell, `aria-current`, no live dead b/c link; all SC + force-dynamic; no useEffect fetch | L4, M1 |
| AC-4 | KPI tiles (counts/—/deep-links), stalled-outbox banner iff signal exposed, no backend added | M4 |
| AC-5 | orders list: cols incl. `customerEmail`, `formatCzk`, state badge; server `<form>` filters; fwd detail link | M3, L4 |
| AC-6 | `?state=&country=&page=2` round-trips; junk clamps; `page=1` dropped | M2 |
| AC-7 | invoices list cols + **download present→blob / absent→disabled** (ABSENT today) | **L1** |
| AC-8 | audit list cols (date+time), filters URL-state, fwd diff link | L4, M2 |
| AC-9 | per-list keyed empty states | i18n eyeball |
| AC-10 | per-list error Alert + retry + `loading.tsx` | mirror objednavky |
| AC-11 | responsive 375/768/1280, cards<md/table≥md | manual QA |
| AC-12 | every string keyed (vykání), read-only grep, no any/console, check-consistency exit 0, no regen | M5, T8 |

---

## Harvest watch (not yet a finding — flag if it recurs)

- **Generated `Promise<void>` file-download method discarding the PDF body** has now bitten three
  tickets' design (T-0087b Option F, T-0116 Option F, T-0118a Option E). It's already pattern-locked
  (the blob-helper discipline), so it's NOT a new recurring-findings row — but if a PR actually
  *ships* a generated file method (vs. the design rejecting it), that's a logged-pattern violation,
  not a fresh nit.
- No new harvest candidate from this slice's design. Recurring-findings #2 (T8) and #3 (T9) are both
  codified and irrelevant to a read-only frontend slice with no new error codes / no new index.

## Preliminary verdict

**Cannot pass yet — PR not open.** No blocker in the *design* (the ticket is unusually well-locked;
every landmine has a rebutted alternative). The four things that will decide approval, in order:
**L1** (invoice button MUST be disabled, not wired — the contract gap is real and confirmed),
**L2** (admin login is a distinct no-OAuth form posting `login('admin',…)` with full error-map),
**L3** (server gate redirects cleanly with no flash + no login loop; middleware retargets admin
branch only), **L4** (no live 404 forward links/nav). T8 should be trivially green (no new error
codes) but verify the run exits 0 and remember it does NOT cover the new UI keys — those need eyes.
