---
id: T-0130
title: Static public pages — how-it-works + for-makers (real content) + VOP/GDPR placeholders
status: ready
size: M
owner: frontend
created: 2026-06-21
updated: 2026-06-21
depends_on: [T-0015]
blocks: []
user_stories: []
adrs: [0005]
phase: 4
manual_steps: []
security_touching: false
layers: [frontend]
---

# T-0130 — Static public pages: /jak-to-funguje, /pro-makery, /vop, /gdpr

## Context

T-0130 is the **content half of the public-polish bundle** (branch `feat/public-polish-bundle`, user-locked 2026-06-20). It ships the four standalone static pages that the `(public)` route group has always reserved space for — the `(public)/layout.tsx` doc comment already enumerates them ("landing, /katalog, /katalog/[slug], /produkt/[id], **/jak-to-funguje, /pro-makery, /vop, /gdpr**"). PROJEKT-VIZE.md's "Zbývá dodělat" list names them as outstanding (`Stránka /jak-to-funguje`, `Stránka /pro-tiskare`, `VOP + GDPR`). T-0131 (SEO: sitemap/robots/OG) is the sibling ticket in the same bundle/PR and enumerates these four routes in `sitemap.ts`, so T-0130 lands first.

Two of the four pages carry **real marketing content** harvested verbatim-in-spirit from PROJEKT-VIZE.md:
- **/jak-to-funguje** (how it works) — the customer-facing order flow as prose + step cards, sourced from PROJEKT-VIZE.md §"Jak to funguje — objednávkový flow" and TISKNI_MVP_SPEC.md flow detail. Vykání (V form — customer-facing public surface).
- **/pro-makery** (for makers) — the maker value proposition, onboarding, and payout model, sourced from PROJEKT-VIZE.md §"Proč to děláme", §"Byznys model", and §"6 kategorií služeb". Vykání at MVP (public marketing page; the tykání-for-makers tone decision in `docs/questions/open.md` applies to authenticated maker dashboard surfaces, not the public acquisition page — see Out of scope).

The other two are **legal placeholders by explicit user lock** — JVM YORE s.r.o. must supply the approved legal text, which is a **blocking pre-launch action item** (logged as Q-0030 + a `docs/launch-checklist.md` note created by this ticket). T-0130 ships only the **scaffolding**: the real page shell, the route, the nav-reachable URL, the wired (but content-empty) i18n keys, and a **visible Alert banner** reading "PLACEHOLDER — awaiting approved legal text (JVM YORE s.r.o.)". The implementer **does not invent legal text** — no terms, no privacy clauses, no cookie copy. The banner is a real UI element (`Alert variant="warning"`), not a code comment.

All four routes live under `frontend/src/app/(public)/` (the layout already exists per T-0015). Each is a **Server Component** `page.tsx` with `generateMetadata` — these are static-ish public pages with zero client JS unless an interaction demands it (none does). **Every new user-facing string is a cs-CZ i18n key** — the T8 i18n gate is LIVE on master (`node scripts/check-consistency.mjs` fails CI on any unkeyed Czech string in a `(public)` page), so brand copy aside, no hardcoded Czech ships.

This ticket adds **no backend contract change** — no NSwag regen, no new endpoint, no `BusinessErrorMessage` codes, no migration. It is pure frontend presentation over static content.

## Locked design decisions

Captured per `docs/process/deliberation.md`. The user locked the bundle scope on 2026-06-20 (marketing pages get REAL content; VOP/GDPR are PLACEHOLDER shells per the legal-text-is-blocking lock). PM-absorbed defaults follow from CLAUDE.md + the `(public)` page precedents (T-0046/T-0047/T-0048).

### A. User-locked (non-negotiable)

1. **/jak-to-funguje + /pro-makery ship REAL marketing content** harvested from PROJEKT-VIZE.md, not lorem/placeholder. Prose + step cards. The content is sourced (cite the harvested sections inline in the i18n key comments). **Rejected:** placeholder shells for all four (loses the launch-ready acquisition pages the bundle exists to deliver); generated/AI-invented marketing claims not traceable to PROJEKT-VIZE (risks unsubstantiated promises — every claim maps to a vize section).
2. **/vop + /gdpr ship PLACEHOLDER shells, NOT invented legal text.** Real page shell + nav + i18n keys + a VISIBLE warning banner. The legal TEXT is a blocking pre-launch item (Q-0030 + launch-checklist). **Rejected:** writing best-effort terms/privacy text now (legal liability — JVM YORE s.r.o. must supply approved text; an agent-drafted VOP is not legally binding and risks shipping wrong obligations); omitting the routes entirely until text exists (breaks the footer/nav links T-0131's sitemap enumerates; SEO and layout reserve these URLs today).
3. **Vykání (V form) on the public marketing pages.** Customer-facing public surface. **Rejected:** tykání on /pro-makery (the maker-facing tykání tone — pending in `docs/questions/open.md` — governs the authenticated maker dashboard, not the public acquisition page where the reader is an anonymous prospect, addressed formally).

### B. PM-absorbed (no user input needed)

- **Server Components, zero client JS.** All four pages are static content with no interactivity. No `'use client'`. (CLAUDE.md frontend rule 1.)
- **All strings via cs-CZ i18n keys** under a new `static.*` namespace (`static.how_it_works.*`, `static.for_makers.*`, `static.terms.*`, `static.privacy.*`). T8 gate enforces. (CLAUDE.md i18n rule 6.)
- **Legal placeholder banner = `Alert variant="warning"`**, rendered visibly in-page (not a comment). Copy from `static.legal_placeholder.banner` (kept generic so both /vop and /gdpr reuse it). The exact Czech banner text the user dictated: "PLACEHOLDER — awaiting approved legal text (JVM YORE s.r.o.)" — keyed, displayed inside the Alert.
- **UI primitives from `components/ui/`** — `Alert` for the legal banner, `Card` for the step cards, `Icon` where the existing pages use it. No new UI primitives. (CLAUDE.md frontend rule.)
- **Step cards mirror the existing pattern** — reuse the `Card` + numbered-step composition the landing/catalog pages establish; teal palette (`brand-*` / `surface-*` tokens) per the existing `(public)` pages. No arbitrary Tailwind values; responsive at 375/768/1280.
- **generateMetadata per page** — `title` + `description` from i18n keys, mirroring the `(public)/katalog/page.tsx` `generateMetadata()` shape. T-0131 extends these four with the openGraph/twitter/canonical objects; T-0130 ships the title/description baseline so T-0131 has a `generateMetadata` to extend.
- **No backend touch** — no NSwag regen, no endpoint, no error code, no migration.
- **`docs/launch-checklist.md`** — created/extended by this ticket with the "JVM YORE s.r.o. must supply approved VOP + GDPR/cookie text before launch — pages scaffolded, text missing (Q-0030)" blocking line.

## Scope

### Frontend — marketing pages (REAL content)

- **`frontend/src/app/(public)/jak-to-funguje/page.tsx`** — NEW Server Component.
  - `export function generateMetadata(): Metadata` returning `{ title: t('static.how_it_works.meta_title'), description: t('static.how_it_works.meta_description') }`. (T-0131 extends with openGraph/twitter/canonical.)
  - Hero/intro prose harvested from PROJEKT-VIZE.md §"Co je Tiskni.cz" + §"Jak to funguje — objednávkový flow": "Zákazník najde tiskaře ve svém okolí, objedná, zaplatí online. Maker vyrobí a odešle přes Zásilkovnu." (rebranded to Makables; vykání).
  - **Step cards** rendering the customer order flow from PROJEKT-VIZE.md §"Jak to funguje" diagram, condensed to the customer-visible milestones (each a `Card`): (1) Vyberete makera a produkt; (2) Vyplníte objednávku + vyberete výdejní místo Zásilkovny + nahrajete podklady; (3) Zaplatíte online (Comgate); (4) Maker objednávku přijme a vyrobí; (5) Maker odešle přes Zásilkovnu, dostanete tracking; (6) Převezmete zásilku. Each card carries an `Icon` + a keyed title + keyed body.
  - Closing CTA linking to `/katalog` ("Prohlédnout katalog") — reuse the landing/catalog CTA link styling.
- **`frontend/src/app/(public)/pro-makery/page.tsx`** — NEW Server Component.
  - `generateMetadata` → `static.for_makers.meta_title` / `static.for_makers.meta_description`.
  - Value-prop prose from PROJEKT-VIZE.md §"Proč to děláme" point 3 ("Makeři chtějí prodávat, ne marketovat — nabízíme hotovou infrastrukturu: profil, katalog, platby, doprava, fakturace") + §"6 kategorií služeb" (the 6 categories: 3D tisk, klasický tisk, potisk textilu, laser & CNC, velkoformát, handmade).
  - **Onboarding + payout step/feature cards** from PROJEKT-VIZE.md §"Byznys model": registrace zdarma (stačí IČO), provize 15 % z ceny produktu (ne z dopravy), výplaty 1× týdně hromadným převodem, automatická fakturace (zákazníkovi při platbě, makerovi při výplatě), doprava hradí zákazník přes Zásilkovnu, žádná minimální objednávka. The "Příklad kalkulace" (produkt 500 Kč → provize 75 Kč → maker dostane 504 Kč) MAY render as an illustrative card (keyed numbers; not computed client-side).
  - Closing CTA linking to the maker registration route (`/registrace` or the maker-register entry the auth pages establish — implementer confirms the live route; if unresolved, link `/katalog` and flag).
- Both pages: vykání throughout; `brand-*`/`surface-*` teal tokens; responsive grid for the cards (1 col mobile / 2–3 col desktop, matching the catalog card grid).

### Frontend — legal placeholder pages (SHELL only)

- **`frontend/src/app/(public)/vop/page.tsx`** — NEW Server Component.
  - `generateMetadata` → `static.terms.meta_title` / `static.terms.meta_description`.
  - Page shell: `<h1>{t('static.terms.title')}</h1>` ("Obchodní podmínky") + a visible `Alert variant="warning"` containing `t('static.legal_placeholder.banner')` ("PLACEHOLDER — awaiting approved legal text (JVM YORE s.r.o.)") + a short keyed in-page note (`static.terms.placeholder_note`) explaining the text is pending legal approval. **No invented clauses.**
- **`frontend/src/app/(public)/gdpr/page.tsx`** — NEW Server Component.
  - `generateMetadata` → `static.privacy.meta_title` / `static.privacy.meta_description`.
  - Same shell pattern: `<h1>{t('static.privacy.title')}</h1>` ("Ochrana osobních údajů") + the shared `Alert variant="warning"` placeholder banner + `static.privacy.placeholder_note`. **No invented privacy/cookie text.**
- Both reuse the shared `static.legal_placeholder.banner` key for the Alert body so the banner copy is single-sourced.

### Frontend — i18n keys

- **`frontend/src/lib/i18n/cs-CZ.ts`** — NEW `static.*` block. Keys (Czech values; comments cite the harvested PROJEKT-VIZE section):
  - `static.how_it_works.meta_title`, `static.how_it_works.meta_description`, `static.how_it_works.title`, `static.how_it_works.intro`, `static.how_it_works.step1_title`..`step6_title`, `static.how_it_works.step1_body`..`step6_body`, `static.how_it_works.cta`.
  - `static.for_makers.meta_title`, `static.for_makers.meta_description`, `static.for_makers.title`, `static.for_makers.intro`, the 6 feature/benefit cards (`static.for_makers.benefit_*_title` / `_body` for: registrace zdarma / provize 15 % / týdenní výplaty / automatická fakturace / doprava Zásilkovna / žádné minimum), an optional `static.for_makers.example_*` calc card, `static.for_makers.cta`.
  - `static.terms.meta_title`, `static.terms.meta_description`, `static.terms.title`, `static.terms.placeholder_note`.
  - `static.privacy.meta_title`, `static.privacy.meta_description`, `static.privacy.title`, `static.privacy.placeholder_note`.
  - `static.legal_placeholder.banner` — the shared warning banner copy.

### Docs

- **`docs/launch-checklist.md`** — CREATE (if absent) with a blocking pre-launch section, or extend; add: "**[ ] Legal text (Q-0030, BLOCKING):** JVM YORE s.r.o. must supply approved VOP (obchodní podmínky) + GDPR privacy/cookie text. Pages `/vop` + `/gdpr` are scaffolded (shell + nav + i18n keys + placeholder banner) by T-0130; only the legal TEXT is missing. Replace the placeholder banner + populate the `static.terms.*` / `static.privacy.*` keys before go-live."
- **`docs/questions/open.md`** — APPEND Q-0030 (logged by this ticket — see below).
- **`docs/tickets/INDEX.md`** — PM flips T-0130 to `**done**` post-merge.

### NSwag regen

**None.** No backend contract change. T-0130 is pure frontend presentation; `frontend/src/lib/api-client/` is untouched.

## Alternatives Considered

- **Option A — Placeholder/lorem for all four pages, real content in a later ticket.** *Rejected per A.1* — the public-polish bundle exists precisely to ship launch-ready acquisition pages. /jak-to-funguje and /pro-makery convert visitors; shipping them empty defeats the bundle. PROJEKT-VIZE.md already contains the harvestable content, so there is no blocker to writing it now.
- **Option B — Draft best-effort VOP + GDPR text now.** *Rejected per A.2* — legal liability. An agent-drafted terms/privacy document is not approved by JVM YORE s.r.o. and is not legally binding; shipping it risks publishing wrong obligations or non-compliant privacy disclosures. The user explicitly locked these as placeholders pending approved legal text. The scaffolding (route, nav, i18n keys, banner) ships now so the legal text is a drop-in later.
- **Option C — Omit /vop + /gdpr routes until legal text exists.** *Rejected per A.2* — T-0131's sitemap enumerates `/vop` + `/gdpr`; the footer/nav link to them; the `(public)/layout.tsx` comment reserves them. Omitting the routes yields 404s on linked URLs and a sitemap referencing non-existent pages. The visible placeholder banner is the honest interim state.
- **Option D — Tykání on /pro-makery (maker-facing tone).** *Rejected per A.3* — the public /pro-makery page addresses an anonymous prospect who has not yet registered as a maker; vykání is the correct register for a public acquisition page. The tykání-for-makers decision (`docs/questions/open.md`) governs the authenticated maker dashboard, where the reader is a known, onboarded maker.
- **Option E — Hardcode the Czech copy directly in the page JSX (skip i18n keys).** *Rejected per B.* — the T8 gate (`scripts/check-consistency.mjs`) fails CI on unkeyed Czech in `(public)` pages, and CLAUDE.md i18n rule 6 mandates keys for all user-facing strings. Keying also makes the future locale-add (the cs-CZ.ts doc comment anticipates it) and the legal-text drop-in trivial.
- **Option F — Render the legal placeholder banner as an HTML comment / build-time warning instead of a visible Alert.** *Rejected per B.* — the user explicitly required a VISIBLE banner (a real `Alert` UI element). A visitor (and a reviewer) must see "PLACEHOLDER — awaiting approved legal text" on the rendered page; a comment is invisible to the visitor and useless as an interim disclosure.
- **Option G — Compute the "Příklad kalkulace" (500 → 75 → 504 Kč) client-side from constants.** *Rejected per B + CLAUDE.md frontend rule 3.* — pricing math is backend-owned; the public page must not run commission math. The illustrative numbers ship as static keyed copy (an example, not a live calculator).

## Out of scope

- **SEO — sitemap.ts / robots.ts / OG metadata.** T-0131 (same bundle/PR) owns `sitemap.ts`, `robots.ts`, and the openGraph/twitter/canonical extensions to `generateMetadata` (including these four pages). T-0130 ships only the `title`/`description` baseline for T-0131 to extend.
- **The actual legal text for /vop + /gdpr.** Blocking pre-launch item (Q-0030); JVM YORE s.r.o. supplies it. T-0130 ships the shell + banner + keys only — NO invented legal/privacy/cookie copy.
- **Cookie-consent banner / cookie management UI.** Separate concern; not part of the GDPR *page* scaffold. If required for launch it is a distinct ticket (flag in Q-0030 if the user wants it folded into the legal-text deliverable).
- **Header/footer nav wiring for the four new links.** If the footer/header components do not yet link these routes, adding the links is a thin follow-up; T-0130's pages are directly reachable by URL and enumerated by T-0131's sitemap regardless. (If the footer component exists and is trivially editable, the implementer MAY add the four links within this ticket and note it; otherwise flag.)
- **/pro-tiskare tax/IČO guide.** PROJEKT-VIZE.md lists a separate `/pro-tiskare` "průvodce pro nové makery (IČO, daně, registrace)". /pro-makery is the marketing/value-prop page; a deeper tax/registration guide is a future page, not this ticket.
- **Maker-dashboard tykání tone.** Governed by the open `docs/questions/open.md` tone decision; not relitigated here. The public pages use vykání per A.3.
- **Any backend change** — no endpoint, no NSwag regen, no error code, no migration.
- **Multi-locale content.** cs-CZ only at launch (CLAUDE.md). The `static.*` keys live in the single cs-CZ catalog.

## Acceptance criteria

- **AC-1** Given a visitor navigates to `/jak-to-funguje`, when the page renders, then it returns 200 and shows the how-it-works heading, the intro prose, and the customer order-flow **step cards** (≥5 numbered steps) — all content traceable to PROJEKT-VIZE.md §"Jak to funguje — objednávkový flow" / §"Co je Tiskni.cz".
- **AC-2** Given a visitor navigates to `/pro-makery`, when the page renders, then it returns 200 and shows the maker value-prop intro + the benefit/onboarding cards (registrace zdarma/IČO, provize 15 %, týdenní výplaty, automatická fakturace, doprava Zásilkovna, žádné minimum) — all traceable to PROJEKT-VIZE.md §"Proč to děláme" / §"Byznys model" / §"6 kategorií služeb".
- **AC-3** Given a visitor navigates to `/vop`, when the page renders, then it returns 200, shows the "Obchodní podmínky" heading, and renders a **visible `Alert variant="warning"`** containing "PLACEHOLDER — awaiting approved legal text (JVM YORE s.r.o.)" plus a placeholder note. **No invented legal clauses appear anywhere on the page.**
- **AC-4** Given a visitor navigates to `/gdpr`, when the page renders, then it returns 200, shows the "Ochrana osobních údajů" heading, and renders the same **visible warning placeholder banner** + a placeholder note. **No invented privacy/cookie text appears.**
- **AC-5** Given each of the four pages, when its `generateMetadata` runs, then it returns a `title` and `description` sourced from the page's `static.*` i18n keys (no hardcoded strings). (T-0131 later extends these with openGraph/twitter/canonical.)
- **AC-6** Given any of the four pages, when the CTA / internal links render (e.g., /jak-to-funguje → /katalog, /pro-makery → registration), then clicking them navigates to a real existing route with no 404.
- **AC-7** Given the four pages render at viewport widths 375 / 768 / 1280, then the layout is responsive (step/benefit cards reflow 1-col → 2/3-col; no horizontal overflow; no arbitrary Tailwind values — `brand-*`/`surface-*` tokens only).
- **AC-8** Given the marketing pages, when read, then all customer-facing copy uses **vykání** (V form) — no tykání on the public pages.
- **AC-9** Given the i18n catalog, when inspected, then every user-facing string on all four pages resolves to a `static.*` cs-CZ key (brand copy excepted), and **`node scripts/check-consistency.mjs` exits 0 with no new T8 i18n violations**.
- **AC-10** Given the four `page.tsx` files, when inspected, then each is a **Server Component** (no `'use client'`, no `useEffect`, no data fetching, no client-side state) — pure static presentation. Frontend hygiene clean: zero `console.*`, zero `any`, zero unused imports, zero dead code; UI primitives (`Alert`, `Card`, `Icon`) imported from `components/ui/`.
- **AC-11** Given the docs, when inspected, then `docs/launch-checklist.md` carries the blocking "legal text (Q-0030)" pre-launch line and `docs/questions/open.md` carries Q-0030. No backend files changed; `frontend/src/lib/api-client/` untouched (no NSwag regen).

## Technical notes

### Why the marketing content is harvested from PROJEKT-VIZE.md (not invented)

Every marketing claim on /jak-to-funguje and /pro-makery must be traceable to an existing project document so we never publish an unsubstantiated promise (e.g., a fee rate or payout cadence that contradicts the actual business model). PROJEKT-VIZE.md §"Byznys model" is the authoritative source for "provize 15 %", "výplaty 1× týdně", "registrace zdarma (IČO)"; §"Jak to funguje — objednávkový flow" is the authoritative source for the step sequence. The implementer cites the source section in the i18n key comment so a reviewer can verify the claim. Rebrand "Tiskni.cz" → "Makables" in the copy (the vize predates the rename).

### Why VOP/GDPR ship as visible placeholders

Publishing agent-drafted terms or a privacy policy would be legally hazardous — only JVM YORE s.r.o.'s approved text is binding. But the routes must exist now: T-0131's sitemap enumerates them, the footer links them, and search engines will index the URLs. The honest interim state is a working page that visibly says the legal text is pending. The `Alert variant="warning"` is loud, the i18n keys are wired empty (ready for the drop-in), and Q-0030 + the launch checklist make the missing text a hard pre-launch gate.

### Why Server Components with zero client JS

These are static content pages — no forms, no filters, no interactivity. Per CLAUDE.md frontend rules 1–3, Server Components are the default and these pages have no reason to ship a single byte of client JS. This also keeps them maximally cacheable and SEO-friendly (T-0131 builds OG/canonical on top).

### Why a new `static.*` i18n namespace

The existing catalog groups keys by domain (`auth.*`, `catalog.*`, `order.*`). These four pages are a new domain — static marketing/legal content — so `static.*` keeps them discoverable and isolated. When the legal text arrives, it populates `static.terms.*` / `static.privacy.*` without touching any other namespace.

### Site-URL / canonical dependency (handoff to T-0131)

T-0130 deliberately ships only `title` + `description` in `generateMetadata`. The openGraph/twitter/**canonical** objects T-0131 adds need an absolute site origin (e.g., `https://makables.cz`), and there is currently **no `NEXT_PUBLIC_SITE_URL` / `metadataBase` constant** in `frontend/src/`. T-0131 owns introducing that constant (or `metadataBase` in the root layout) — flagged here so the bundle's SEO ticket resolves it rather than each page hardcoding the origin.

## Files touched (expected)

### New
- `frontend/src/app/(public)/jak-to-funguje/page.tsx`
- `frontend/src/app/(public)/pro-makery/page.tsx`
- `frontend/src/app/(public)/vop/page.tsx`
- `frontend/src/app/(public)/gdpr/page.tsx`
- `docs/launch-checklist.md` (create if absent)

### Modified
- `frontend/src/lib/i18n/cs-CZ.ts` — add the `static.*` key block (marketing copy + legal placeholder banner + meta titles/descriptions).
- `docs/questions/open.md` — append Q-0030 (legal text for VOP + GDPR; blocking pre-launch).
- `docs/tickets/INDEX.md` — PM flips T-0130 to `**done**` post-merge.
- (Optionally) the footer/header nav component — add the four links if trivially editable; otherwise flag per Out of scope.

## Test plan reference

Manual/visual verification (static presentation, no logic): render each route at 375/768/1280; confirm the placeholder banners are visible on /vop + /gdpr; confirm the marketing content matches PROJEKT-VIZE.md; confirm CTAs resolve; confirm `node scripts/check-consistency.mjs` exits 0 (T8 i18n gate green). No separate `docs/test-plans/T-0130.md`.

## Status log

- 2026-06-21 `draft → ready` by PM/BA. Created as the content half of the public-polish bundle (`feat/public-polish-bundle`, user-locked 2026-06-20). Sibling: T-0131 (SEO — sitemap/robots/OG). Scope: 4 new `(public)` Server-Component pages — /jak-to-funguje + /pro-makery (REAL content harvested from PROJEKT-VIZE.md §"Jak to funguje"/§"Byznys model"/§"6 kategorií"), /vop + /gdpr (PLACEHOLDER shells per user lock — visible `Alert` banner "PLACEHOLDER — awaiting approved legal text (JVM YORE s.r.o.)", NO invented legal text). New `static.*` cs-CZ i18n keys; `docs/launch-checklist.md` blocking legal-text line; Q-0030 logged. User-locked: A.1 real marketing content, A.2 legal placeholders (text is blocking pre-launch), A.3 vykání public tone. No backend touch, no NSwag regen, no error codes, no migration. depends_on T-0015 (frontend scaffold + `(public)` layout). **Ready for frontend.**
