# Preliminary review notes — public-polish bundle (T-0130 content + T-0131 SEO)

> **Status:** PRELIMINARY (concurrent draft). Written by the Reviewer in parallel with the implementer, BEFORE any implementation file exists on `feat/public-polish-bundle`. These are pre-flight watch-items the implementer should design against and the final review will check row-by-row. Not an approval.
>
> **Inputs read:** T-0130 + T-0131 ticket files; `docs/review/checklist.md`; `docs/process/quality-gates.md`; `docs/review/recurring-findings.md` (#2 i18n CODIFIED as T8, `hard`); ADR 0023 (NFRs — perf/SEO/a11y); `patterns.md` §B.9/§B.13 and the §B frontend index; existing `(public)/katalog/page.tsx`, `katalog/[slug]/page.tsx`, root `layout.tsx`, landing `page.tsx`, `components/ui/alert.tsx`, `scripts/check-consistency.mjs` (`ruleT8`).
>
> **Repo state at draft time:** none of the four new pages, `sitemap.ts`, `robots.ts`, or `lib/seo/site-url.ts` exist yet; `static.*` / `home.metadata.*` keys absent from `cs-CZ.ts`; `docs/launch-checklist.md` absent. Clean slate — all findings below are FORWARD-LOOKING.

---

## HEADLINE — legal-placeholder discipline (the user lock that must not break)

This is the single highest-risk item in the bundle. The user explicitly locked `/vop` + `/gdpr` as **clearly-marked placeholders with NO invented legal text** (T-0130 §A.2, Option B rejected). The exact failure mode to hunt for in final review is "an agent helpfully drafted best-effort terms / privacy / cookie prose."

Final-review hard gates (any one failing = REQUEST CHANGES, no approval):

1. **`frontend/src/app/(public)/vop/page.tsx`** renders a **visible `Alert variant="warning"`** whose body is `t('static.legal_placeholder.banner')` = "PLACEHOLDER — awaiting approved legal text (JVM YORE s.r.o.)". `Alert variant="warning"` confirmed to exist (`components/ui/alert.tsx:16` — amber styling, `role="alert"`). The banner must be a rendered UI element, NOT an HTML comment / build warning (Option F rejected).
2. **`/gdpr`** reuses the SAME shared `static.legal_placeholder.banner` key (single-sourced banner copy) + its own `static.privacy.placeholder_note`.
3. **NO invented legal clauses anywhere on either page.** Read every line of both `page.tsx` files AND the `static.terms.*` / `static.privacy.*` i18n values. Watch for: paragraphs of "Tyto obchodní podmínky upravují…", enumerated clauses (§1, §2…), GDPR articles (čl. 6 GDPR, "správce osobních údajů…"), cookie tables, retention periods, data-subject-rights prose. The ONLY allowed body text is the heading (`static.terms.title` = "Obchodní podmínky" / `static.privacy.title` = "Ochrana osobních údajů"), the warning Alert, and a SHORT keyed placeholder note saying the text is pending legal approval. `placeholder_note` itself must NOT smuggle in quasi-legal content — it is a "this is coming" notice, not a mini-policy.
4. **`docs/launch-checklist.md`** (created by T-0130) carries the BLOCKING line: "Legal text (Q-0030, BLOCKING): JVM YORE s.r.o. must supply approved VOP + GDPR/cookie text…". **`docs/questions/open.md`** carries Q-0030. (AC-11.) Verify both exist — neither file/entry exists at draft time.
5. The pages still return **200** and are reachable by URL (AC-3/AC-4) — they are real shells, not 404s (Option C rejected; T-0131's sitemap enumerates `/vop` + `/gdpr`, so a 404 would also break the sitemap honesty).

If the implementer wrote ANY substantive terms/privacy/cookie prose, that is a hard reject regardless of quality — only JVM YORE s.r.o.'s approved text is binding, and shipping agent-drafted legal text is the exact liability the user lock forbids.

---

## T8 i18n — IMPORTANT correction to the ticket narrative

Both tickets repeatedly assert that the **T8 gate fails CI on any unkeyed Czech string in a `(public)` page** (T-0130 §"Context"/AC-9; T-0131 §"Context"/AC-11). **This claim is inaccurate as the script actually stands.** I read `scripts/check-consistency.mjs`: `ruleT8` (lines 383–509) enforces exactly ONE thing — **`BusinessErrorMessage` code ↔ `cs-CZ.ts` key parity** (every `public const string … = "dotted.value"` has a matching `cs-CZ` key or sits in `T8_NO_KEY_REQUIRED`). There is **no rule that scans `(public)` page JSX for hardcoded Czech literals.** I confirmed no other rule (T1–T7, T9) does this either.

Consequences for this review:

- **`check-consistency.mjs` will exit 0 even if the implementer hardcodes Czech prose directly in the marketing JSX.** AC-9 ("check-consistency exits 0 with no new T8 violations") is therefore a NECESSARY-but-NOT-SUFFICIENT proof of i18n compliance. Since this bundle adds **zero new `BusinessErrorMessage` codes** (no backend touch), T8 is trivially green regardless of the JSX. Do not let a green `check-consistency` run stand in for "all public strings keyed."
- **The "every public string is keyed" requirement is enforced by HUMAN REVIEW here, not by the gate.** The final review must manually read all four `page.tsx` files and confirm every user-facing Czech string flows through `t('static.…')`. This is a LOT of keys (T-0130 enumerates ~30+ for the two marketing pages alone) — spot-check the prose bodies, the step-card titles/bodies, the benefit-card titles/bodies, the CTAs, the meta title/description, and the legal notes. The brand exception (e.g. "Makables", "Where Ideas Take Shape") is the only allowed literal.
- **Recurring-findings note:** the T8 row (#2) is codified-in-script and `hard`, but its scope is BusinessErrorMessage-only. If the implementer ships hardcoded Czech in `(public)` JSX and we catch it in review, that is a NEW finding category (not the existing #2 row — which is about error-code parity). It is a candidate for a NEW recurring-findings row if it recurs ("public-page JSX hardcodes Czech instead of `static.*` key"). Do NOT bump row #2 for it.
- **Possible doc-accuracy follow-up (flag to Architect/PM, do not fix here):** either the script should grow a JSX-hardcoded-Czech scan for `(public)` pages, or the two tickets overstate the gate. Reviewer does not modify process docs/scripts; flag it.

---

## SEO correctness (T-0131) — watch-items

### sitemap.ts
- **Must use `MetadataRoute.Sitemap`** — default-export `async function sitemap(): Promise<MetadataRoute.Sitemap>` returning an array of `{ url, lastModified, changeFrequency, priority }`. **Reject any hand-rolled XML string** or custom route handler emitting XML (A.1, Options A/B rejected). File lives at `frontend/src/app/sitemap.ts` (app root, NOT inside `(public)` — route groups don't own conventions; ADR 0005/B).
- **Static set must be exactly the six routes:** `/`, `/katalog`, `/jak-to-funguje`, `/pro-makery`, `/vop`, `/gdpr`, each as an **absolute** URL via `canonicalUrl(path)` (AC-1). Verify `/jak-to-funguje` + `/pro-makery` + `/vop` + `/gdpr` are present (they depend on T-0130 landing first — same PR).
- **Transient-failure resilience (AC-3):** if `getPagedMakers` returns `{ success: false }`, the sitemap must fall back to static-only and NOT throw / NOT 500. This is the `Result<T,ApiError>` helper convention (§B.16) — confirm the dynamic walk is wrapped and a failed read degrades gracefully.
- **Dynamic-enumeration decision (A.4 — implementer's call, must be flagged):** default preference is enumerate maker slugs via a capped `getPagedMakers` walk and DEFER product ids (no bulk product-id read exists — confirmed: products are only reachable through a maker profile, so full enumeration is N+1). Either "maker slugs + deferred products w/ `// TODO(T-NNNN)`" OR "static-only + PR flag" is acceptable. **Final review must confirm the PR description states which path was taken.** Judge soundness: a capped maker walk (≤20 pages/480 makers) at MVP scale (ADR 0023: ≤1000 DAU, catalog RPS ≤50, well under the 50k single-sitemap limit) is sound; static-only is also defensible for MVP. A full product walk would be the WRONG call (disproportionate N+1) — flag if attempted.
- `export const revalidate = 3600` present (hourly regen).

### robots.ts
- **Must use `MetadataRoute.Robots`** — default-export returning `{ rules: [{ userAgent: '*', allow: '/' }], sitemap: canonicalUrl('/sitemap.xml'), host: SITE_URL }` (AC-4). No `Disallow` at MVP (A.2; Option C rejected — don't advertise auth paths). Confirm `sitemap` field references the sitemap absolute URL.

### Absolute-URL base / canonical correctness (the SEO-tanking risk)
- **New `frontend/src/lib/seo/site-url.ts`** is the single source of truth: `SITE_URL` (reads `NEXT_PUBLIC_SITE_URL`, default `https://makables.cz` in prod, `http://localhost:3000` in dev) + `canonicalUrl(path)`. Confirm EVERY absolute URL (sitemap entries, OG `url`, canonical, robots sitemap ref) flows through this one helper — no hardcoded `https://makables.cz` literals scattered across pages (mirrors the `buildProductImageUrl` centralisation, §B.19).
- **`NEXT_PUBLIC_SITE_URL` must NOT be confused with `NEXT_PUBLIC_API_PUBLIC_BASE_URL`** (Option F rejected). The API base points at the backend host (`localhost:5104` in dev); a canonical pointing there would tank SEO. Verify `site-url.ts` reads the SITE var, not the API var.
- **`metadataBase: new URL(SITE_URL)`** set on the root `layout.tsx` `metadata` object (C.2). Confirmed absent at draft time (`layout.tsx:10–16` has only title/description) — must be ADDED. Without it Next resolves relative OG URLs against localhost and warns at build.
- **Canonical = the page's OWN absolute URL** (the bug to hunt — a canonical pointing at a wrong/duplicate URL silently tanks rankings):
  - Landing `/` → `canonicalUrl('/')` (bare origin). Verify `canonicalUrl('/')` returns the bare origin without a trailing-slash/double-slash artifact (the `site-url.test.ts` case 5 covers this — confirm it asserts `'/'` → bare origin and that `canonicalUrl('/katalog')` === `https://makables.cz/katalog` with no doubled slash).
  - `/katalog` → `canonicalUrl('/katalog')`, **filter query params EXCLUDED** (C.6 / Option I rejected — every filtered view points back to the bare catalog; standard faceted-listing duplicate-content hygiene). Verify the canonical is the unfiltered URL even though the page is `force-dynamic` and reads `searchParams`.
  - `/katalog/[slug]` → `canonicalUrl('/katalog/' + slug)`; `/produkt/[productId]` → `canonicalUrl('/produkt/' + productId)`. Each page's canonical = its own URL, not a sibling's.

### OG / Twitter tags on the 4 page types (AC-5..AC-8)
- **Landing `/`** currently has NO `generateMetadata` (confirmed — `page.tsx` exports only `HomePage`, inherits root-layout metadata). T-0131 ADDS one returning `openGraph {title, description, url: canonicalUrl('/'), type:'website'}` + `twitter {card:'summary', title, description}` + `alternates.canonical`, with title/description from the NEW `home.metadata.title` / `home.metadata.description` keys (AC-5; the only new i18n keys in T-0131). Verify those two keys land in `cs-CZ.ts` and are NOT hardcoded literals.
- **`/katalog`** — extend the existing `generateMetadata` (`page.tsx:17–22`) to ADD og/twitter/canonical; title/description unchanged (`catalog.title`/`catalog.subtitle`).
- **`/katalog/[slug]` + `/produkt/[productId]`** — the NotFound-safe title branch (§B.9, confirmed present at `katalog/[slug]/page.tsx:28–40`) must be **PRESERVED**; og/canonical objects ADDED inside BOTH the success AND the error-fallback return paths (AC-9 — a NotFound still returns a valid `Metadata` with a canonical, does not throw, does not tell the indexer the entity is gone on a transient error). Watch for the implementer adding OG only to the happy path and dropping it (or crashing) on the error branch.
- **OG `type` per page:** `website` (landing, /katalog), `profile` (/katalog/[slug]), `product` (/produkt/[productId]). Twitter card `summary` everywhere (text-only — no OG image asset at MVP, Option H; `images` omitted with a `// TODO` + launch-checklist follow-up — acceptable, do not require an image).
- **OG strings reuse existing meta** (PM default) — `openGraph.title`/`twitter.title` reuse the computed `title`; same for description. The only NEW keys are the two `home.metadata.*`. If the implementer invents extra OG-copy i18n keys, that is scope creep — flag (not a reject, but note it).

---

## Server-Components-first (T-0130 AC-10) — watch-items
- All four new `page.tsx` MUST be Server Components: **no `'use client'`, no `useEffect`, no client state, no data fetching** (these are static content pages). This is straightforward — the precedent `(public)/katalog/page.tsx` is a Server Component and these are simpler (no `force-dynamic` needed either, unlike catalog). Any `'use client'` here is an automatic finding (CLAUDE.md frontend rule 1; checklist B).
- `sitemap.ts` / `robots.ts` / `site-url.ts` are server-side by nature — confirm no client-only import leaks in.
- UI primitives from `components/ui/`: `Alert` (legal banner — confirmed exists w/ `warning` variant), `Card` (step/benefit cards — confirmed `components/ui/card.tsx` exists), `Icon` (confirmed). No new UI primitives expected; no inline `style={}` for layout; `brand-*`/`surface-*` tokens only; no arbitrary Tailwind values; responsive 375/768/1280 (AC-7, checklist E).

---

## AC traceability (final-review map)

**T-0130:** AC-1 /jak-to-funguje (≥5 step cards, traceable to PROJEKT-VIZE §"Jak to funguje") · AC-2 /pro-makery (6 benefit cards: registrace zdarma/IČO, provize 15 %, týdenní výplaty, automatická fakturace, doprava Zásilkovna, žádné minimum — all traceable to PROJEKT-VIZE §"Byznys model"/§"6 kategorií"; source content confirmed present in `PROJEKT-VIZE.md`) · AC-3/AC-4 legal placeholders (see HEADLINE) · AC-5 generateMetadata title+description from `static.*` keys on all 4 · AC-6 CTAs resolve to real routes (see below) · AC-7 responsive · AC-8 vykání (V-form) on marketing pages — spot-check the prose uses "vyberete/zaplatíte/převezmete", NOT tykání (T-0130 §A.3) · AC-9 all strings keyed + check-consistency exit 0 (see T8 correction — green run is necessary-not-sufficient) · AC-10 Server Components + hygiene · AC-11 launch-checklist + Q-0030 + no backend touch (`frontend/src/lib/api-client/` untouched — verify the diff does NOT touch the generated client; this bundle has NO NSwag regen).

**T-0131:** AC-1..AC-4 sitemap/robots · AC-5..AC-8 OG/canonical on 4 page types · AC-9 NotFound-safe metadata · AC-10 site-url env + metadataBase · AC-11 build clean + check-consistency exit 0 + new SEO tests pass + no `'use client'` + no NSwag regen.

### AC-6 — CTA route resolution (a real 404 risk — verify carefully)
- **/jak-to-funguje → /katalog** ("Prohlédnout katalog") — `/katalog` exists. OK.
- **/pro-makery → maker registration** — T-0130 says "link `/registrace` or the maker-register entry the auth pages establish — implementer confirms the live route." I checked: the actual maker-registration route is **`(auth)/register/maker/page.tsx`**, i.e. the live URL is **`/register/maker`**. The existing landing page (`page.tsx:152`) links makers to **`/auth/register?role=maker`** — note the `(auth)` route group does NOT add an `/auth` URL segment, so `/auth/register…` may itself be a stale/404 link on the CURRENT landing page (pre-existing, out of scope for this bundle, but worth flagging to PM). **For /pro-makery's CTA, the correct target is `/register/maker`** (or `/register`). If the implementer links `/registrace` (a non-existent Czech alias) or copies the landing page's `/auth/register?role=maker`, that is a 404 — REQUEST CHANGES under AC-6. Final review must click-test the actual rendered href against the real route table.

---

## Hygiene / cross-stack
- **No backend touch / no NSwag regen** (both tickets, AC-11). Confirm the diff touches zero `backend/**` and zero `frontend/src/lib/api-client/**`. (At draft time the branch base shows only unrelated api-client churn from the prior order-cleanup bundle in `git status`; the public-polish diff itself must not touch generated client.)
- **No `console.*`, no `any`, no unsafe `!`, no unused imports, no dead code** across all new files (checklist A; AC-10/AC-11).
- **Tests (T-0131):** `sitemap.test.ts`, `robots.test.ts`, `site-url.test.ts`, landing-metadata test — these are PURE LOGIC (URL joining, static-route enumeration, fallback behavior). **Gate 5 / checklist H TDD watch:** T-0131 is well past the T-0067 grandfather cutoff. `site-url.ts` (`canonicalUrl` join logic) and the sitemap static-set/fallback logic are pure logic per `must-cover-tests.md` categories. If these tests are committed AFTER the implementation commit, that is an after-the-fact-test HARD FAIL (Gate 5). Final review must inspect the branch commit order (test commit before or alongside impl) for `site-url.ts` and `sitemap.ts`. **The four new page components and OG-metadata wiring are presentation, NOT pure logic → manual/visual verification, no TDD mandate.** Draw the line at: `canonicalUrl` joining + sitemap enumeration/fallback = TDD-required; JSX pages + `generateMetadata` glue = manual.
- **Optimizer:** NOT a hot path. Static pages, one capped paged read in the sitemap (well within ADR 0023 budgets; revalidate=3600 amortises it). No Optimizer ping warranted unless the sitemap does an uncapped or N+1 walk (then ping). Note: a product-id walk WOULD be N+1 — if attempted, ping Optimizer AND reject per A.4.
- **Security:** `security_touching: false` on both. `NEXT_PUBLIC_SITE_URL` is non-secret (NEXT_PUBLIC_* allowed in client bundle, CLAUDE.md Security; checklist D). robots allow-all is the correct posture (auth-gated dashboards 401, not robots-protected — A.2). No SecOps ping needed. Confirm no secret leaks into the client and the new env var is documented in env-vars docs + the T-0131 `manual_steps`.
- **Docs (Gate 7):** new `NEXT_PUBLIC_SITE_URL` must be added to the env-var list + appsettings/deploy templates (T-0131 §Env/config + manual_steps). launch-checklist gets BOTH the legal-text blocker (T-0130) AND the SEO env + OG-image follow-up lines (T-0131).

---

## Preliminary verdict
**NOT APPROVED (preliminary — implementation not yet on branch).** No blockers identified in the DESIGN; the ticket pair is sound and the locked decisions are correct. Final approval is gated on row-by-row checklist verification once the diff exists, with these as the load-bearing gates, in priority order:
1. **Legal placeholders contain ZERO invented legal text** + visible `Alert variant="warning"` + Q-0030/launch-checklist present (HEADLINE).
2. **All public Czech strings keyed via `static.*`** — verified by HUMAN read, NOT by the green `check-consistency` run (T8 only checks BusinessErrorMessage parity; it will NOT catch hardcoded JSX Czech).
3. **Canonical = own URL**, sitemap/robots use `MetadataRoute.*` (not hand-rolled XML), `metadataBase` set, `NEXT_PUBLIC_SITE_URL` ≠ API base.
4. **AC-6 CTAs resolve** — /pro-makery → `/register/maker` (NOT `/registrace`, NOT the landing page's stale `/auth/register?role=maker`).
5. **TDD order** for `site-url.ts` + `sitemap.ts` pure logic (Gate 5); zero `'use client'`; no backend/api-client touch.
