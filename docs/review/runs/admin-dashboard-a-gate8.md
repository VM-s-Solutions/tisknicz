# Gate 8 (Performance) — T-0118a admin dashboard read shell (frontend)

- **Branch:** `chore/admin-dashboard-grooming` (3 commits: shell+auth-gate, overview KPIs + 3 read views + i18n)
- **Scope:** frontend-only, 28 files / +2520 LOC. 3 admin list pages (orders / faktury / audit), 1 overview, login, shell-nav, `admin-client.ts` read helpers, i18n.
- **Reviewer:** Performance Optimizer (Gate 8)
- **Date:** 2026-06-15

## Verdict: **GATE8_PASS (with 1 FOLD)**

The three list pages are textbook: one paged SSR GET per render, URL-state filters via native `<form method="get">`, `<Link>`-based pagination, `force-dynamic`, per-route `loading.tsx` streaming, zero client refetch. No `useEffect` data fetch, no client data lib, no N+1, no raw `<img>`, no `.find/.filter` over server lists. Not on the public hot-path table (admin = 99.0% availability, business-hours, no customer TTFB budget in ADR 0023 §1). No BLOCKER, no High.

---

## Check-by-check

### 1. Three list pages — SSR single request, URL-state pagination, filter push — PASS
`orders/page.tsx:81`, `faktury/page.tsx:80`, `audit/page.tsx:77` each fire exactly **one** `getAdmin*()` per render. Filters are server-rendered native GET forms (`order-filters.tsx:47`) — no client store, deep-linkable, back/forward round-trips. Pagination is `<Link href>` preserving filter params, `page=1` dropped (`pagination.tsx:31`, patterns.md B.8). Page is reset on filter submit (no `page` field in the form). No client data library imported anywhere in `(admin)`. **No N+1.**

### 2. Overview KPIs — count probes — **SERIAL WATERFALL (FOLD)**
See finding below. **4 sequential `pageSize:1` probes**, not parallel.

### 3. Route bundle sizes — PASS (tracked baseline)
6 client islands total: `shell-nav.tsx`, `admin-login-form.tsx`, `invoice-download.tsx`, + 3 `error.tsx` boundaries. All justified (interactivity / error reset / forward-compat disabled island). Each imports `t()`, so each inlines the ~10 kB-gzip `cs-CZ.ts` dictionary — the **Q-0014** known duplication, now widened by the +162-line i18n block this PR adds. This is a pre-existing, tracked baseline issue (Q-0014 open; Q-0015 — no absolute First Load JS budget in ADR 0023), **not** a T-0118a regression. No new runtime dependency (`admin-client.ts` is hand-written over `apiFetch`; no NSwag class pulled into client). Marginal route cost over the maker-dashboard baseline is in-family (same island shape as maker `objednavky`).

### 4. Blob download (invoice PDF) — N/A (correctly deferred)
`invoice-download.tsx:24` ships a **disabled `<span>`** with tooltip — no fetch, no guessed path. The endpoint is absent from the admin contract; the blob helper (`apiFetch` `parse:'blob'` + `timeoutMs:120_000`) is documented as a commented stub in `admin-client.ts:278-283` for the follow-up. Nothing to measure. Correct call — no faked download wired.

### 5. force-dynamic + loading.tsx — PASS (1 note)
`dynamic = 'force-dynamic'` on all 4 admin pages (overview + 3 lists) — correct, admin data is always live, no static caching of operator PII. `loading.tsx` present on all **3 list** routes (streaming skeletons mirroring layout). **Overview `/dashboard/admin` has no `loading.tsx`** — see Medium note; it matters precisely because of the serial probes in §2.

### 6. Images / heavy client components — PASS
No `next/image`, no raw `<img>`, no charting/PDF/markdown module-scope imports in any client island. Nothing heavy.

---

## Findings

```
[Medium] frontend/src/app/(admin)/dashboard/admin/page.tsx:52-55 — overview KPI probes are serial
What: AdminOverviewPage awaits 4 countOrdersInState() probes back-to-back (Paid → Accepted → Shipped → Disputed); each is a separate pageSize:1 GET to /admin-orders for its totalCount.
Cost: cost model — 4 round-trips in series. At a same-DC backend RTT of ~60-90 ms each, that is ~240-360 ms of overview TTFB spent purely waiting, scaling linearly if the ops tiles (currently "—") later get live count reads (8 tiles -> ~480-720 ms serial). Promise.all collapses it to ~1 RTT (~60-90 ms). The probes are independent reads — no ordering dependency.
Fix: wrap the four probes in a single Promise.all([...]) so they fire in parallel (the redirect-on-Unauthorized branch still works — first rejected/redirecting probe wins; or hoist the auth redirect ahead of the gather).
Refs: ADR 0023 §1 (admin not budgeted, hence Medium not High); patterns.md A.8 (paged read contract — probes are correctly pageSize:1, not over-fetch); T-0118a US-admin-0002.
```

```
[Nit] frontend/src/app/(admin)/dashboard/admin/page.tsx (route) — no loading.tsx on the overview route
What: The overview route streams nothing while its (serial) probes resolve; the 3 list routes each have a loading.tsx but the overview does not.
Cost: cost model — with §2 unfixed, the user stares at a blank shell for the full ~240-360 ms serial window before any tile paints. A loading.tsx skeleton lets the shell+header stream immediately.
Fix: add dashboard/admin/loading.tsx with a 4-tile skeleton (mirrors the existing list loading.tsx skeletons). Lower priority if §2 is folded (parallel probes shrink the wait to ~1 RTT).
Refs: ADR 0023 §1 (streaming/observability); T-0118a AC-4 (graceful render).
```

---

## Handed to reviewer
Both findings are **FOLD** candidates, not gates — admin overview is off the public hot-path table and has no ADR 0023 TTFB budget. Recommend folding the Promise.all parallelisation into this PR (one-line change, removes ~240-360 ms serial wait and future-proofs the ops tiles); the overview loading.tsx can ride along or backlog. No BLOCKER, no High. Bundle/i18n duplication stays with Q-0014/Q-0015 (architect/standalone-ticket territory) — not a T-0118a finding.
