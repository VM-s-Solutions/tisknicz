# Gate 8 (Performance) — Bundle A: public-polish-bundle

**Branch:** `feat/public-polish-bundle` · 3 commits (016d047 groom, f2ece1d T-0130, 834de75 T-0131)
**Tickets:** T-0130 (static content) + T-0131 (SEO)
**Reviewer:** Performance Optimizer · **Date:** 2026-06-21
**Verdict: GATE8_PASS**

---

## Scope

Public SSR / static surface only. No backend `.cs` in the diff — no new query, no
new index need, no handler signature touched. Backend checks (B1–B8) N/A. This is
a frontend-render + sitemap-generation-cost review (F1–F7 + crawl-time cost model).

Files judged: 4 static pages (`jak-to-funguje`, `pro-makery`, `vop`, `gdpr`),
`page.tsx` (landing metadata), `sitemap.ts`, `robots.ts`, `site-url.ts`,
`katalog/page.tsx` + `katalog/[slug]/page.tsx` + `produkt/[productId]/page.tsx`
(metadata additions), `layout.tsx` (`metadataBase`), `cs-CZ.ts` (prose keys).

---

## Findings

### 1. Sitemap generation cost — the headline item — ACCEPTABLE (model below)

`frontend/src/app/sitemap.ts` enumerates maker slugs by a **capped paged walk** of
the anonymous `getPagedMakers` read, NOT a single unbounded fetch.

**Cost model (no live measurement — pre-launch, empty catalog):**
- `MAX_MAKER_PAGES = 20` × `CATALOG_MAX_PAGE_SIZE = 48` → **ceiling 960 maker URLs**,
  capped at **20 backend reads** per generation. At MVP scale (ADR 0023 §2: ≤ a few
  hundred makers) the real walk is **1–6 paged reads** then stops on `hasNext === false`.
- `export const revalidate = 3600` → the walk runs **at most once/hour**, not per
  crawl hit. Next serves the cached `/sitemap.xml` to every crawler in between.
  Catalog browse RPS budget is 50 (ADR 0023 §2, "mostly bots"); the sitemap does
  **not** add a read per bot hit — it adds ≤20 reads once an hour. **Negligible.**
- Transient-failure path returns partial+static (line 62-65) and **never throws** —
  a backend blip can't 500 the sitemap (AC-3). Good.
- Each backend read is `GetPagedMakers` (T-0043 hot path, already indexed +
  `AsNoTracking`); the sitemap rides the existing query, adds no new query shape.

**[Nit] sitemap.ts — cost-ceiling drift watch (not gating):** the 20-page cap (960
URLs) is comfortable now, but if the catalog crosses ~960 publicly-listable makers
the walk silently truncates the sitemap (drops makers past page 20 from the index)
rather than paging the full set. Fine at MVP; flag a follow-up to lift the cap or
split sitemaps before the catalog approaches that size. Cost model, not measured.

**Product-id enumeration deferred (static note):** correctly NOT attempted — there
is no bulk product-id feed, so listing products would mean an N+1 walk of every
maker's products at generation time. Deferring to a backend bulk-id feed is the
right call (sitemap.ts lines 16-22). Noted, no action this bundle.

### 2. OG/canonical metadata — NO double-fetch — PASS

`katalog/[slug]` and `produkt/[productId]` call `getMakerBySlug` / `getProductById`
in **both** `generateMetadata` and the page body. This is **not** a double backend
call: both go through one `apiFetch('public', <identical URL>, {method:'GET'})`, and
Next App Router **request-memoizes** identical `fetch()` calls within a single render
pass — independent of the Data Cache / `force-dynamic`. One slug render = **one**
`GetMakerBySlug` (T-0044) backend read, one `getProductById` read. Verified
`apiFetch` issues a plain `fetch` with stable options and no per-call cache-buster
(`frontend/src/lib/runtime/api-fetch.ts:150`). Metadata reuses the page's data for free.

`katalog/page.tsx` `generateMetadata` is **synchronous + data-free** (i18n + canonical
only) — zero added fetch. Canonical pins the unfiltered `/katalog`, consolidating
filtered views (duplicate-content hygiene). Good.

### 3. Landing `generateMetadata` — PASS

`page.tsx:8-19` — new, synchronous, i18n + `canonicalUrl('/')` only. Trivial, no fetch.
`layout.tsx` adds `metadataBase` (one `new URL(SITE_URL)` at module scope) — negligible.

### 4. The 4 static pages — Server Components, zero client JS — PASS (F1)

`jak-to-funguje`, `pro-makery`, `vop`, `gdpr`: no `'use client'`, no `useEffect`,
no event handlers, no state, no fetch. Pure RSC prose + `Link` + `Icon`/`Card`/`Alert`
primitives. They emit **zero JS to the client** beyond the shared framework runtime —
build output should mark them `○ (Static)`. `vop`/`gdpr` are placeholder shells
(Q-0030, blocking pre-launch legal text — render path is correct now). No raw `<img>`,
no `next/image` needed (no photos on these pages). F3 N/A.

### 5. Static pages vs ADR 0023 §1 budgets — sanity PASS

Catalog TTFB p95 budget is 400 ms (ADR 0023 §1). These 4 marketing pages are static
prose with no SSR data dependency — TTFB is bounded by static-asset serve time, well
under any budget. No surface here is missing from ADR 0023, so no open question needed.

### 6. Route bundle sizes / i18n chunk (Q-0014) — marginal — PASS (F6)

`cs-CZ.ts` grew ~+112 net lines (~720 added incl. moved) of marketing-prose keys
(`static.*`). Per the **pre-existing** Q-0014 (i18n dictionary inlined into client
chunks), prose keys consumed only by these RSC pages are **not** shipped to the client
(server-rendered `t()` calls). Growth in any client chunk is bounded to keys a client
component actually references — none of these `static.*` keys are. Marginal, within
the Q-0014 envelope; no new dependency added (`npm ls` unchanged), F6 satisfied.
`HeroSceneWrapper` (3D scene, `next/dynamic` + `ssr:false`) is pre-existing and
untouched by this bundle — not a regression here.

---

## Verdict

**GATE8_PASS.** No BLOCKER, no High, no Medium. The headline risk — a per-crawl-hit
catalog scan — does not exist: the maker walk is capped (≤20 reads / ≤960 URLs) and
`revalidate=3600`-gated, so it runs at most hourly, not per bot. OG/canonical metadata
reuses the page's request-memoized fetch (no double-fetch). Static pages are zero-JS
RSCs. One non-gating Nit logged: lift/split the 20-page sitemap cap before the catalog
approaches ~960 makers. Hand back to reviewer.
