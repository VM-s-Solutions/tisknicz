# Test plan — public-polish bundle (T-0130 static pages + T-0131 SEO)

- **Branch:** `feat/public-polish-bundle` (3 commits: groom → T-0130 → T-0131)
- **Scope:** frontend presentation-layer only. No backend, no migration, no NSwag regen, no new `BusinessErrorMessage` codes.
- **Preconditions:** none for the static pages (fully static SSR). For the sitemap, seeded/published makers exercise the dynamic walk; the static-route assertions verify with an empty catalog too.
- **Manual step (env):** `NEXT_PUBLIC_SITE_URL=https://makables.cz` must be set in prod/staging. Unset → defaults to `https://makables.cz` (prod) / `http://localhost:3000` (dev). Read only via `frontend/src/lib/seo/site-url.ts`.
- **Gate 9 status:** `node scripts/check-consistency.mjs` exit 0 at 151 tracked. T8/T9 (hard, never baselined) GREEN — zero new violations. The 3 bundle commits did NOT touch `docs/audits/consistency-violations.md` (the +26 lines vs master are carried-in admin/payout/review bundles, not this one).

---

## Part A — T-0130 static public pages

Routes live under the `(public)` route group (ADR 0005 — group folder does not affect URL path). The old non-grouped copies (`app/jak-to-funguje`, `app/pro-makery`, `app/vop`, `app/gdpr`) were DELETED in this bundle — verified absent, so no duplicate-route ambiguity.

| TC | AC | Case | Method | Expected | Result |
|----|----|------|--------|----------|--------|
| TC-130-1 | route | `/jak-to-funguje` renders 200 | preview GET | Page renders, no error boundary | PENDING preview |
| TC-130-2 | route | `/pro-makery` renders 200 | preview GET | Page renders | PENDING preview |
| TC-130-3 | route | `/vop` renders 200 | preview GET | Page renders | PENDING preview |
| TC-130-4 | route | `/gdpr` renders 200 | preview GET | Page renders | PENDING preview |
| TC-130-5 | content | `/jak-to-funguje` shows REAL content | view page | 6 numbered steps (search→upload→pay→accept→ship→pickup), intro, CTA to `/katalog`. NOT a placeholder banner | PASS (code) — `(public)/jak-to-funguje/page.tsx` maps 6 `static.how_it_works.step*` keys |
| TC-130-6 | content | `/pro-makery` shows REAL content | view page | 6 benefit cards + "Příklad kalkulace" example block (3 lines + note) + CTA to maker register. NOT placeholder | PASS (code) — `(public)/pro-makery/page.tsx` |
| TC-130-7 | placeholder | `/vop` shows PLACEHOLDER banner, NO legal prose | view page | Visible `Alert variant="warning"` reading "PLACEHOLDER — čeká se na schválený právní text (JVM YORE s.r.o.)" + keyed note. NO invented clauses | PASS (code) — `(public)/vop/page.tsx` renders only title + `static.legal_placeholder.banner` Alert + `static.terms.placeholder_note` |
| TC-130-8 | placeholder | `/gdpr` shows PLACEHOLDER banner, NO privacy prose | view page | Same warning banner + `static.privacy.placeholder_note`. NO invented privacy/cookie copy | PASS (code) — `(public)/gdpr/page.tsx` |
| TC-130-9 | placeholder | Assert NO legal/privacy paragraphs leaked into the placeholder | read body | Body = `<h1>` + banner + one note `<p>` only. Grep page bodies for prose | PASS (code) — both shells are exactly heading + Alert + one note |
| TC-130-10 | a11y/banner | Banner is VISIBLE (not `hidden`/`sr-only`/aria-hidden) | inspect | `Alert variant="warning"` rendered in normal flow | PASS (code) |
| TC-130-11 | responsive | `/jak-to-funguje` at 375 / 768 / 1280 | resize | Grid reflows 1→2→3 cols; no overflow; CTA reachable | PENDING preview |
| TC-130-12 | responsive | `/pro-makery` at 375 / 768 / 1280 | resize | Benefit grid 1→2→3; example card single-col stable | PENDING preview |
| TC-130-13 | responsive | `/vop` + `/gdpr` at 375 / 768 / 1280 | resize | `max-w-3xl` column, banner full-width, readable | PENDING preview |
| TC-130-14 | i18n | ALL strings on the 4 pages are i18n-keyed | read code | Every `<h*>`/`<p>`/CTA pulls from `t('static.*')`. No inline Czech | PASS (code) — all four pages 100% keyed via `static.how_it_works.*` / `static.for_makers.*` / `static.terms.*` / `static.privacy.*` / `static.legal_placeholder.banner` |
| TC-130-15 | i18n | Keys exist in `cs-CZ.ts` | grep | All referenced keys present | PASS — verified in `frontend/src/lib/i18n/cs-CZ.ts` lines 286–375 |
| TC-130-16 | tone | Vykání (V form) for customer-facing copy | read | "Vyberte", "Vyplňte", "Zaplaťte", "Převezmete", "získáte", "Připraveni" | PASS (code) — consistent vykání |
| TC-130-17 | hygiene | No `console.*`, no `any`, no `'use client'`, Server Components | read + tsc | All 4 pages are Server Components, zero client JS | PASS — `npx tsc --noEmit` exit 0; no `'use client'`; no `any`/`console` |
| TC-130-18 | nav | `/jak-to-funguje` + `/pro-makery` reachable from header nav | preview | `nav.how_it_works` / `nav.for_makers` keys present (cs-CZ:163–164) | PENDING preview (key wiring present) |

### T-0130 edge cases / adversarial

- **EC-130-A** — Placeholder must not silently become real prose: a future legal-text drop replaces `static.terms.*`/`static.privacy.*`; re-run TC-130-7/8 to confirm the banner is REMOVED at that point (today it must STILL be visible). Today: banner present = correct.
- **EC-130-B** — `/pro-makery` example calculation copy (504 Kč) is STATIC keyed text, never computed client-side (pricing math is backend-owned per CLAUDE.md). Confirmed: `example_*` keys are literal strings, no `formatCzk` call on this page. PASS.
- **EC-130-C** — Brand copy exception: the example uses literal "Kč" inside keyed strings — acceptable (it is keyed copy, not a runtime money format). No `formatCzk` misuse.

---

## Part B — T-0131 SEO surface

| TC | AC | Case | Method | Expected | Result |
|----|----|------|--------|----------|--------|
| TC-131-1 | AC-1 | `GET /sitemap.xml` returns valid XML with 6 static URLs | curl preview | Valid `<urlset>`; absolute URLs for `/`, `/katalog`, `/jak-to-funguje`, `/pro-makery`, `/vop`, `/gdpr` under `SITE_URL` | PENDING preview (code: `STATIC_ROUTES` lists all 6 via `canonicalUrl`) |
| TC-131-2 | AC-2 | Maker slugs `/katalog/{slug}` enumerated when catalog has makers | curl preview (seeded) | Each published maker slug appears as absolute URL | PENDING preview (code: `collectMakerSlugs` walks `getPagedMakers`, capped 20 pages) |
| TC-131-3 | AC-2 | Product `/produkt/{id}` enumeration DEFERRED — documented | read | Products NOT in sitemap (no bulk product-id read at MVP). Flagged in launch-checklist | PASS (code + launch-checklist line) — intended, not a bug |
| TC-131-4 | AC-3 | Catalog read fails → sitemap falls back to static-only, no 500 | force backend down / read code | `collectMakerSlugs` breaks on `!result.success`, returns partial; sitemap never throws | PASS (code) — `break` on failure, try-free non-throwing walk |
| TC-131-5 | AC-4 | `GET /robots.txt` allows all + references sitemap | curl preview | `User-agent: *` / `Allow: /`, NO `Disallow`, `Sitemap: {SITE_URL}/sitemap.xml` | PENDING preview (code: `robots.ts` rule `{ userAgent: '*', allow: '/' }` + `sitemap: canonicalUrl('/sitemap.xml')`) |
| TC-131-6 | AC-5 | Landing `/` head: OG + twitter + canonical | view-source `<meta property="og:*">` | `og:title`, `og:description`, `og:url`={SITE_URL}, `og:type=website`, `twitter:card=summary`, `<link rel="canonical" href="{SITE_URL}">`; title/desc from `home.metadata.*` keys (not literal) | PENDING preview (code: `page.tsx generateMetadata` complete) |
| TC-131-7 | AC-6 | `/katalog` head: OG + twitter + canonical | view-source | `og:type=website`, `og:url`={SITE_URL}/katalog, canonical={SITE_URL}/katalog, filter params NOT in canonical | PENDING preview (code: canonical hard-coded to unfiltered `/katalog`) |
| TC-131-8 | AC-7 | `/katalog/{slug}` head: OG + twitter + canonical | view-source | `og:type=profile`, `og:url`+canonical={SITE_URL}/katalog/{slug}; title/desc reuse company name + bio-derived | PENDING preview (code complete, success + NotFound branches) |
| TC-131-9 | AC-8 | `/produkt/{id}` head: OG + twitter + canonical | view-source | canonical={SITE_URL}/produkt/{id}, `twitter:card=summary` | PENDING preview — **DEVIATION: `og:type=website`, NOT `product`** (see DEF-2) |
| TC-131-10 | AC-9 | NotFound (404) slug: `generateMetadata` returns valid Metadata with canonical, does not throw | request bad slug | Canonical = requested URL; NotFound-safe title branch; transient error does NOT emit "not found" title | PASS (code) — both dynamic pages branch only on `error.type==='NotFound'`, keep canonical |
| TC-131-11 | AC-10 | `NEXT_PUBLIC_SITE_URL` drives all absolute URLs via `site-url.ts`; `metadataBase` set on root layout | read code + preview | All sitemap/OG/canonical/robots use `SITE_URL`; `layout.tsx` sets `metadataBase: new URL(SITE_URL)` | PASS (code) — single source of truth; `metadataBase` present |
| TC-131-12 | AC-11 | Build clean; Gate 9 exit 0; T8 green; no `console`/`any`/`'use client'`; api-client untouched | tsc + gate9 + git | tsc exit 0; gate 9 exit 0 @151; `lib/api-client/` not in diff | PASS — tsc 0, gate9 0, no api-client changes in bundle |
| TC-131-13 | AC-10 | Canonical host ≠ API host | read | `site-url.ts` rejects reuse of `NEXT_PUBLIC_API_PUBLIC_BASE_URL`; defaults to `makables.cz` not the API | PASS (code + JSDoc) |

### T-0131 edge cases / adversarial

- **EC-131-A** — `canonicalUrl('/')` returns bare origin (no trailing slash); `canonicalUrl('/katalog')` joins without doubling the slash; leading-slash normalisation. Code review PASS (`site-url.ts` lines 50–54). **Unit test mandated by spec is MISSING — see DEF-1.**
- **EC-131-B** — Sitemap cap: 20 pages × 48 = ≤960 maker URLs, under the 50k single-sitemap limit. PASS (code `MAX_MAKER_PAGES`).
- **EC-131-C** — `revalidate = 3600` so a new maker surfaces within an hour without redeploy. PASS (code).
- **EC-131-D** — robots does NOT `Disallow` the auth dashboards (they 401; listing would advertise `/admin`). Intentional per ticket A.2. PASS.
- **EC-131-E** — OG image: text-only `summary` card at MVP (no 1200×630 asset). `summary_large_image` upgrade tracked in launch-checklist. Not a defect; confirm no broken `og:image` is emitted (none). PASS.

---

## Defects / gaps

- **DEF-1 (gap, ticket-mandated tests absent):** T-0131 §Tests mandates six SEO unit tests (`sitemap.test.ts` ×3, `robots.test.ts`, `site-url.test.ts`, landing-metadata test) and AC-11 requires "the new SEO tests pass." NONE exist — the frontend has **zero** `*.test.ts` files and no `test` script in `package.json`. All SEO ACs are therefore manual-only. The pure-logic unit `canonicalUrl` (string join / slash normalisation — a formatting predicate) is exactly the kind of logic that should be unit-pinned. **Recommend:** Reviewer treats AC-11's test clause as unmet; either add the mandated tests in this PR or split a follow-up ticket with the frontend test harness. This is a frontend-wide pattern (no harness exists), but this ticket explicitly required the files.
- **DEF-2 (spec deviation, justified):** AC-8 specifies `og:type=product` for `/produkt/{id}`; implementation emits `og:type=website` because Next 16's typed `OpenGraph.type` union excludes `product`. Documented inline in `produkt/[productId]/page.tsx`. Acceptable MVP deviation; track the `og:type=product` upgrade (raw `<meta>` passthrough or Next type widening) as a follow-up. **Confirm the launch-checklist carries this follow-up — currently it does NOT (only the OG-image + product-sitemap follow-ups are listed).**
- **DEF-3 (doc gap):** T-0131 §Env/config and §Files-touched require `NEXT_PUBLIC_SITE_URL` documented in `docs/deployment/env-vars.md`. It is NOT present there (only in `docs/launch-checklist.md` + the ticket front-matter `manual_steps`). The env var is implemented and works; only the env-vars doc entry is missing. **Recommend:** add the row to `env-vars.md`.

## Launch-checklist cross-reference

- **Q-0030 (BLOCKING):** approved VOP + GDPR legal/privacy/cookie text from JVM YORE s.r.o. Page shells + banner + i18n keys ship in T-0130; only the TEXT is missing. Tracked in `docs/launch-checklist.md` §Legal and `docs/questions/open.md` Q-0030. Includes the open sub-question on a cookie-consent banner. **QA gate: go-live blocked until the placeholder banner is removed and `static.terms.*`/`static.privacy.*` populated.**
- **`NEXT_PUBLIC_SITE_URL` (manual):** set to `https://makables.cz` in prod/staging; after deploy verify `/sitemap.xml` + `/robots.txt` resolve, submit sitemap to Google Search Console. Tracked §SEO.
- **`og:type=product` follow-up:** NOT yet in the launch-checklist (see DEF-2). Recommend adding alongside the OG-image + product-sitemap deferrals.

## Sign-off

- Gate 9: exit 0 @ 151, T8/T9 green, baseline unchanged by this bundle. PASS.
- Static-code verification: PASS for T-0130 content/placeholder/i18n/vykání/hygiene and T-0131 metadata wiring.
- Outstanding before merge approval (Reviewer's call): DEF-1 (mandated SEO tests), DEF-2/DEF-3 doc follow-ups, and the PENDING-preview manual cases (sitemap.xml, robots.txt, view-source OG, responsive 375/768/1280) executed against the Vercel preview.
