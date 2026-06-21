# Final review — public-polish bundle (T-0130 static content + T-0131 SEO)

> Branch `feat/public-polish-bundle` · 3 commits (016d047 grooming + f2ece1d T-0130 + 834de75 T-0131).
> Scope verified against `016d047~1..834de75` (22 files; the `master...HEAD --stat` superset is unrelated admin/payout/review work from an older base — NOT this PR).

## VERDICT: REQUEST CHANGES — 1 BLOCKER (AC-6 dead CTA). Everything else passes.

One real defect: the `/pro-makery` primary CTA links to a 404. Everything else in both tickets is clean — the legal-placeholder lock holds, canonicals are correct, and all gates pass. Fix the one href and this approves.

---

## (a) Legal-placeholder confirmation — PASS (fake text truly gone)

- **Old root-level pages DELETED, clean:** `frontend/src/app/{vop,gdpr,jak-to-funguje,pro-makery}/page.tsx` (the old versions carrying invented legal text + hardcoded Czech) are removed (`git ls-files` returns empty). Route groups don't change URLs, so `/vop`, `/gdpr`, `/jak-to-funguje`, `/pro-makery` still resolve via the new `(public)/` versions. No orphan imports, no broken internal links to old paths (the URLs are identical). Resolved route table confirms exactly one page per URL — no dup-route.
- **`(public)/vop/page.tsx` + `(public)/gdpr/page.tsx`** render ONLY: the heading (`static.{terms,privacy}.title`), a visible `Alert variant="warning"` whose body is `static.legal_placeholder.banner` = "PLACEHOLDER — čeká se na schválený právní text (JVM YORE s.r.o.)", and a single keyed placeholder note. Both notes (cs-CZ.ts:362, 370) are pure "text is being prepared, pending operator approval" — ZERO legal substance.
- **Invented-legal-clause scan = clean** on both `(public)/{vop,gdpr}` and their i18n keys (359–375): no §clauses, no GDPR articles, no retention periods, no cookie tables, no data-subject-rights prose. (The single `GDPR čl. 17` hit at cs-CZ.ts:1542 is an unrelated T-0127 admin delete-user string — not on these pages, not in this bundle.)
- **Q-0030 + launch blocker present:** `docs/questions/open.md` Q-0030 (agent will NOT draft legal text; blocking pre-launch). `docs/launch-checklist.md:10` carries the BLOCKING legal-text line. AC-11 met.
- **Marketing prose is GENUINE** (`jak-to-funguje` 6 steps; `pro-makery` 6 benefits + illustrative calc): real vykání copy, cited per-block to PROJEKT-VIZE.md, not Lorem Ipsum, not invented features. The 15% commission / weekly-payout / ARES / Zásilkovna claims match the documented business model. The `Příklad kalkulace` is explicitly labelled illustrative + keyed (never computed client-side) — backend owns the math. Good.

## (b) SEO correctness — PASS (canonical clean on every page type)

- **`sitemap.ts`** uses `MetadataRoute.Sitemap` (not hand-rolled XML), enumerates the 6 static routes + maker slugs via a capped (`MAX_MAKER_PAGES=20`) `getPagedMakers` walk that falls back to static-only on a failed read and never throws (AC-3). `revalidate=3600`. Product-id enumeration deferred with a documented rationale (no bulk product-id read; full walk would be N+1). **RULING: deferral ACCEPTED for MVP** — a product walk would be the wrong call; maker-slugs + deferred products is the sound choice.
- **`robots.ts`** uses `MetadataRoute.Robots`, allow-all, references `canonicalUrl('/sitemap.xml')` + `host`. Correct MVP posture.
- **`site-url.ts`** is the single origin source: reads `NEXT_PUBLIC_SITE_URL` (NOT the API base — explicitly documented), strips trailing slashes, `canonicalUrl('/')` → bare origin (no double-slash). `metadataBase` set on root `layout.tsx`. `NEXT_PUBLIC_SITE_URL` is non-secret (`NEXT_PUBLIC_*`).
- **Canonical = own URL on EVERY page type, verified:** landing → `canonicalUrl('/')`; `/katalog` → unfiltered `/katalog` (filter params excluded — duplicate-content hygiene, correct even though force-dynamic); `/katalog/[slug]` → own slug URL; `/produkt/[productId]` → own id URL. On `/katalog/[slug]` and `/produkt/[productId]` the canonical + OG are present on BOTH the success AND the NotFound/error branches, and the NotFound-safe title branch is preserved. No shared/wrong canonical anywhere.

## (c) og:type ruling — ACCEPT `website` for the product page

Next 16's typed `OpenGraph.type` union has no `'product'` member. The impl emits `type:'website'` (a valid, clean, framework-supported card) and documents the deviation inline. The alternative (raw `<meta property="og:type" content="product">` passthrough) would emit a DUPLICATE og:type tag alongside Next's generated one — strictly worse. **RULING: ACCEPTED.** The type system forced a valid choice; no duplicate tag; clean card. Noted, not blocked.

## (d) BLOCKER

**BLOCKER-1 — `/pro-makery` CTA is a 404 (AC-6).**
`frontend/src/app/(public)/pro-makery/page.tsx:94` — `href="/auth/register/maker"`. The resolved route table has NO `/auth/register/maker`; the maker-registration route is `(auth)/register/maker/page.tsx`, and route group `(auth)` adds NO URL segment (no physical `app/auth/` dir, no next.config rewrite). **Correct target: `/register/maker`.** This is the page's primary conversion CTA — it must resolve. Fix the href to `/register/maker`.

> Note (NOT a blocker for this PR — pre-existing, flag to PM): the SAME stale `/auth/...` pattern already exists in `(auth)/login/login-form.tsx:86`, `(auth)/register/page.tsx:15`, and landing `page.tsx:168` (all untouched by this bundle). The new CTA copied that broken convention. Either every `/auth/register*` link is broken repo-wide, or there's an intended `/auth` prefix that was never built. Worth a dedicated grooming ticket; out of scope here.

## (e) Fold list (non-blocking — fix in this PR if convenient, else follow-up)

1. **Gate-7 docs gap:** `NEXT_PUBLIC_SITE_URL` is in `launch-checklist.md:21` but NOT in `docs/deployment/env-vars.md` — the T-0131 ticket (lines 102/107) requires it in the env-vars doc. Add the row.
2. **(flag to Architect, standing follow-up — see below):** frontend has zero test harness. `canonicalUrl` + sitemap fallback are pure logic that would normally be TDD-covered.

## (f) Checks / Gates

- **Gate 1 (build/type/lint):** `tsc --noEmit` exit 0. `eslint` exit 0 on all 12 bundle files. `check-consistency.mjs` exit 0 ("clean, 151 tracked").
- **Gate 2 (AC):** T-0130 AC-1..AC-5, AC-7..AC-11 PASS; **AC-6 FAIL** (pro-makery CTA 404). T-0131 AC-1..AC-10 PASS; AC-11 PASS (build clean, no NSwag regen, no `'use client'`).
- **Gate 3 (security):** `NEXT_PUBLIC_SITE_URL` non-secret; robots allow-all correct (dashboards JWT-gated, not robots-hidden); no secret in client bundle. No SecOps ping.
- **Gate 4 (architecture):** all 4 new pages + sitemap/robots/site-url are Server Components (zero `'use client'`). No business logic on frontend (the calc example is static keyed copy). No DB SDK. All strings keyed via `t()` (53 `static.*` keys + 2 `home.metadata.*`). No country branching. No Architect design concern.
- **Gate 5 (tests / TDD):** content + metadata glue = presentation, no domain logic. The only pure logic (`canonicalUrl` string join, sitemap static-set/fallback) is trivial and build-verified; frontend has NO test harness (no vitest/jest, no `*.test.ts` anywhere — long-standing, consistent with every prior frontend slice). **RULING: no after-the-fact pure-logic test was added or deleted; no TDD violation. Accepted deviation — build-verification stands for this static/metadata bundle.** Flag to Architect: the missing frontend harness is a standing gap (`canonicalUrl` is the kind of join that has earned a regression test elsewhere) — recommend a dedicated harness ticket; not a blocker for this PR.
- **Gate 6 (perf):** sitemap walk capped at 20 pages, `revalidate=3600` amortises; not a hot path — no Optimizer ping warranted.
- **Gate 7 (docs):** launch-checklist + Q-0030 added; one gap — env-vars.md missing `NEXT_PUBLIC_SITE_URL` (fold item 1).

**Harvest duty:** the `/auth/...` stale-link pattern is pre-existing in ≥3 files but this is its first appearance as a review *finding* — not yet a recurring-findings entry. No append this round.
