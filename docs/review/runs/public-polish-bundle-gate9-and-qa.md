# Gate 9 + QA audit — public-polish bundle (T-0130 + T-0131)

- **Branch:** `feat/public-polish-bundle` — 3 commits (`016d047` groom → `f2ece1d` T-0130 → `834de75` T-0131)
- **Date:** 2026-06-21
- **Role:** Tester (QA)

## Task 1 — Gate 9 (consistency)

```
node scripts/check-consistency.mjs  →  exit 0
check-consistency: clean (151 tracked).
```

- **Exit 0 at 151 tracked** — matches expected. No backend T-row added (frontend-only bundle).
- **T8 (BusinessErrorMessage ↔ cs-CZ key parity) — GREEN.** Zero T8 violations. The bundle adds a large public i18n surface (`static.how_it_works.*`, `static.for_makers.*`, `static.terms.*`, `static.privacy.*`, `static.legal_placeholder.banner`, `home.metadata.*`, `nav.how_it_works`, `nav.for_makers`) — all keyed in `cs-CZ.ts`. No new `BusinessErrorMessage` code introduced, so T8's parity check has nothing to fail on; the new keys are additive.
- **T9 — GREEN** (no DB index changes).
- **Baseline diff vs master:** `docs/audits/consistency-violations.md` shows +26 lines vs master, BUT `git diff HEAD~3..HEAD -- docs/audits/consistency-violations.md` is **EMPTY** — the 3 bundle commits did NOT touch the baseline. The +26 are carried-in entries from other bundles already on the branch (admin-read-gaps, payouts, reviews). For THIS bundle the baseline is **UNCHANGED**, as expected.

### T8 scope caveat (important for the verdict)

T8 enforces **error-code ↔ i18n-key parity only** — it is NOT a generic "hardcoded Czech string" scanner. The task framing ("T8 enforces every marketing/static string keyed") overstates T8's reach. Verified directly:
- The four T-0130 pages ARE fully keyed (manual code read) — this is implementer discipline, not a T8 catch.
- The **landing page `app/page.tsx` body still contains hardcoded Czech** (hero copy, stat labels, step cards, category descriptions). T8 does NOT and CANNOT flag this. It is explicitly OUT OF SCOPE for both T-0130 (body re-key deferred) and T-0131 (metadata-only) per the ticket. Flagged here so no one assumes a green Gate 9 means the landing body is keyed — it is not.

**Gate 9 verdict: PASS.**

## Task 2 — QA

Full plan: `docs/test-plans/public-polish-bundle.md` (31 test cases: 18 T-0130 + 13 T-0131, plus 8 edge cases). Static-code verification done; preview-dependent cases marked PENDING.

### Verified by code read (PASS)
- T-0130: `/jak-to-funguje` + `/pro-makery` render REAL keyed content (6 steps / 6 benefits + example block); `/vop` + `/gdpr` render ONLY heading + visible `Alert variant="warning"` placeholder banner + one keyed note — NO invented legal/privacy prose. Vykání throughout. All Server Components, `tsc --noEmit` exit 0, no `'use client'`/`console`/`any`. Old non-`(public)` route copies deleted (no duplicate routes).
- T-0131: `sitemap.ts` enumerates 6 static routes + maker slugs (capped, non-throwing fallback to static-only on catalog failure), product enumeration deferred; `robots.ts` allow-all + sitemap ref; OG/twitter/canonical wired on landing + catalog + maker + product (success AND NotFound branches); `metadataBase` on root layout; `site-url.ts` single source of truth, canonical host ≠ API host.

### Defects / gaps surfaced (Reviewer decides)
- **DEF-1 (test gap):** T-0131 mandates 6 SEO unit tests and AC-11 requires them to pass; **none exist** (frontend has zero `*.test.ts`, no `test` script). `canonicalUrl` (a pure formatting predicate) is unit-test-worthy and unpinned. AC-11 test clause unmet.
- **DEF-2 (justified deviation):** AC-8 wants `og:type=product`; impl emits `og:type=website` (Next 16 type union excludes `product`). Documented inline. Follow-up NOT yet in launch-checklist.
- **DEF-3 (doc gap):** `NEXT_PUBLIC_SITE_URL` not added to `docs/deployment/env-vars.md` (required by ticket §Env/config). Implemented + in launch-checklist, just missing the env-vars row.

### Launch-checklist cross-ref
- Q-0030 (BLOCKING) legal text — tracked, go-live blocked until banner removed + keys populated. PASS (tracked).
- `NEXT_PUBLIC_SITE_URL` manual step — tracked §SEO. PASS.
- `og:type=product` follow-up — NOT tracked (DEF-2). Recommend adding.

## Verdict

Gate 9 PASS (exit 0 @151, T8/T9 green, baseline unchanged). Code-level QA PASS for content/placeholder/i18n/SEO wiring. Three gaps (DEF-1 mandated tests, DEF-2/DEF-3 doc follow-ups) surfaced for Reviewer. Preview-manual cases (sitemap.xml, robots.txt, view-source OG, responsive 375/768/1280) PENDING the Vercel deploy.
