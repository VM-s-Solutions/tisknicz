---
id: T-0147
title: Cookie consent banner + consent management (provider-agnostic mechanism)
status: ready
size: M
owner: frontend
created: 2026-07-07
updated: 2026-07-07
depends_on: [T-0015, T-0130]
blocks: [T-0151]
user_stories: [US-customer-0024]
adrs: []
phase: 7
manual_steps: []
security_touching: false
layers: [frontend, l10n]
---

# T-0147 — Cookie consent banner + consent management

## Context

Per [dopady-rozhodnuti-na-platformu.md §2.8](../meetings/dopady-rozhodnuti-na-platformu.md#28-cookie-lišta--gdpr--kontakt--sm-frontend-před-launchem) (dopady §1 Q16), the business has confirmed a cookie consent banner is needed, distinguishing necessary cookies from analytics/marketing (specific tools not yet chosen). The GDPR page already promises a "nastavení souhlasu" (consent settings) mechanism (verified by reading `frontend/src/app/(public)/gdpr/page.tsx`) that does not exist today. This ticket builds that mechanism.

This is the **mechanism half** of §2.8 — the GDPR processor-naming text fix (naming Stripe/Zásilkovna/Resend/ARES/Mapbox/Azure explicitly) was already applied directly outside ticket flow per the Phase-7 manifest header note, and is not re-listed here.

Grounding check: reading `frontend/src/app/layout.tsx`, there is currently **no analytics or marketing script anywhere in the codebase** — nothing needs to be retroactively wrapped. This significantly simplifies the ticket: it ships the banner, the category model, and the storage/gating primitive that a *future* script (whichever tool Q16 eventually picks) will check before loading, entirely provider-agnostically. Satisfies US-customer-0024.

## Scope

- New consent banner component, shown on first visit (no consent choice recorded yet) across every route group (public, auth, customer, maker, admin) — a root-layout-level concern, not per-page.
- Category model: `necessary` (always on, non-togglable, no UI toggle needed for it), `analytics`, `marketing`. Exactly two user actions on first view: "Pouze nezbytné" and "Přijmout vše", plus an expandable "Nastavit předvolby" (customize) view exposing the analytics/marketing toggles individually.
- First-party persistence: a single client-side store (cookie or `localStorage`, implementer's choice — not prescribed here since it's an implementation detail with no business-rule consequence) recording the chosen categories + a timestamp/version, so a future policy-text revision can force re-consent by bumping a version number.
- A small, provider-agnostic gating primitive (e.g. a `hasConsent(category)` check or a `<ConsentGate category="analytics">` wrapper component) that any future script-loading code calls before injecting a `<script>` tag or initializing an SDK. No analytics/marketing script exists yet to wrap — this primitive is the seam the next such ticket (e.g. T-0151 newsletter, or a future analytics-tool ticket) plugs into.
- A "cookie settings" link/entry point (footer, per the GDPR page's existing promise) that reopens the customize view pre-filled with the current choices and lets the visitor change them.
- New `cookieConsent.*` i18n keys in `cs-CZ.ts`.
- No backend change, no NSwag regen — purely frontend, consistent with `depends_on: [T-0015, T-0130]` (scaffold + the GDPR page it hooks into) and no other ticket dependency.

## Alternatives Considered

- **Option A — Wait until a specific analytics/marketing tool is chosen (Q16 unresolved) before building any consent mechanism.** *Rejected* — the GDPR page already publishes a promise ("nastavení souhlasu") that doesn't exist yet, which is itself a compliance gap independent of which tool eventually gets picked. Building the mechanism provider-agnostically now means the eventual tool pick only needs to wire its script tag behind the existing `hasConsent()` check — it doesn't reopen a "build the consent banner" ticket. Waiting also risks the operational mistake of someone adding an analytics snippet directly to `layout.tsx` before any consent gate exists.
- **Option B — Build a granular per-vendor consent list (e.g. "Google Analytics", "Meta Pixel") instead of category-level ("necessary/analytics/marketing").** *Rejected for MVP* — no vendor has been chosen yet (Q16), so a per-vendor list has nothing to list. Category-level consent is the standard, GDPR-compliant granularity and is exactly what dopady §2.8 asks for ("nezbytné vs. analytické/marketingové"). A vendor-level list can be layered on top later without changing the category model — the gating primitive already accepts a category argument, which any future vendor-specific check can still key off.
- **Option C — Store consent server-side (a backend table + endpoint) instead of first-party client storage.** *Rejected for MVP* — CLAUDE.md's frontend-is-a-pure-presentation-layer rule and the "no mocks" / no-backend-without-a-reason posture both argue against introducing a backend endpoint + table for a capability that has no cross-device or auditable-defense requirement stated in the meeting notes. First-party client storage is standard practice for cookie-consent tools industry-wide and satisfies the GDPR page's existing "nastavení souhlasu" promise. Revisit if a legal reviewer later requires a server-side consent audit trail.

## Out of scope

- Choosing or integrating any specific analytics/marketing tool (Q16 leaves the tool choice open — this ticket ships the mechanism only, not a live script).
- Per-vendor consent granularity (Option B, deferred).
- Server-side consent logging / an auditable consent trail for legal defense (Option C, deferred).
- Geo-detection or a different consent regime per visitor country (Czech-only launch per CLAUDE.md; no CCPA or other-regime branching).
- Newsletter/marketing-consent capture itself — that's **T-0151** (blocked separately on the dopady §5.5 MVP-vs-v1.1 scope decision), which depends on this ticket's category mechanism existing.

## Acceptance criteria

- **AC-1** Given a visitor with no consent choice recorded, when they load any page, then the consent banner renders, offering "Pouze nezbytné", "Přijmout vše", and a way to open a customize view with individual analytics/marketing toggles (necessary is shown as always-on, not a toggle).
- **AC-2** Given the visitor clicks "Přijmout vše", when confirmed, then all categories are recorded as accepted, the choice is persisted first-party, and the banner does not reappear on the next page load or subsequent visit (until the choice expires or the consent version is bumped).
- **AC-3** Given the visitor clicks "Pouze nezbytné", when confirmed, then only `necessary` is recorded as accepted (analytics/marketing recorded as declined), persisted the same way.
- **AC-4** Given the visitor opens the customize view and toggles only "Analytika" on (leaving "Marketing" off), when they save, then exactly that combination is persisted.
- **AC-5** Given no consent choice has been made yet, when any code path calls the `hasConsent('analytics')` (or `'marketing'`) gating primitive, then it returns `false` — the default is blocked-until-explicit-consent, never permissive-by-default.
- **AC-6** Given a consent choice was already made, when the visitor clicks the "cookie settings" link (the entry point the GDPR page references), then the customize view reopens pre-filled with the visitor's current choices, and saving a change updates the stored consent immediately (`hasConsent()` reflects the new value on the next call).
- **AC-7** Given the codebase has no analytics or marketing script wired at all (verified: `frontend/src/app/layout.tsx` has none today), then this ticket introduces none either — it ships only the banner + storage + gating primitive, with zero behavior change to any currently-loaded script.
- **AC-8** Given the banner is rendered, when checked at 375/768/1280 viewport widths, then it's fully usable (no cut-off buttons, no overlap with the mobile nav per the unrelated-but-adjacent V1 fix already applied) and keyboard-navigable (focus trap while open, per accessibility baseline).

## Technical notes

- No existing analytics/marketing script exists to retrofit (checked via `Grep` across `frontend/src` for `cookie|consent|Consent` before writing this ticket — only i18n/GDPR-copy and auth-cookie-unrelated hits found). This means the ticket is purely additive with zero regression risk to existing behavior.
- Root-layout placement: `frontend/src/app/layout.tsx` is the natural mount point (renders for every route group) — a Client Component island for the banner itself (interactivity requires `'use client'`), consistent with CLAUDE.md's "Server Components by default, `'use client'` only for interactivity" rule.
- The GDPR page (`frontend/src/app/(public)/gdpr/page.tsx`) is itself still a T-0130 legal placeholder (Q-0030 unresolved) — this ticket's "cookie settings" link target should be a stable route/anchor so the eventual final GDPR text doesn't need to change the link, only the banner/mechanism cross-references it.

## Files touched (expected)

- `frontend/src/components/shared/cookie-consent-banner.tsx` — new Client Component.
- `frontend/src/lib/consent/` — new module: category model, `hasConsent()`, storage read/write, `<ConsentGate>` wrapper.
- `frontend/src/app/layout.tsx` — mount the banner.
- `frontend/src/lib/i18n/cs-CZ.ts` — new `cookieConsent.*` keys.
- `frontend/src/app/(public)/gdpr/page.tsx` — wire the "cookie settings" link to the new customize view (no change to the placeholder legal text itself).
- `frontend/src/components/shared/public-navbar.tsx` or footer component — add the "cookie settings" entry point if not already promised elsewhere.

## Test plan reference

`docs/test-plans/T-0147.md` (to be created by the implementer; cover first-visit banner render, each of the three consent actions, `hasConsent()` default-false-before-choice, and the customize-view re-open/edit path).

## Status log

- 2026-07-07 `draft` by PM — added to the Phase 7 business-model-pivot manifest per dopady §6 work-package table.
- 2026-07-07 `draft → ready` by BA. Wrote US-customer-0024 with Given/When/Then AC + Alternatives Considered. Confirmed via code read that no analytics/marketing script exists yet anywhere in the frontend, so this ticket is purely additive (banner + category model + gating primitive), with zero live script to retrofit. Locked: category-level (not per-vendor) consent; first-party client storage (not server-side); default-blocked-until-consent for the gating primitive. No new open question raised — Q16's tool choice remains a separate, already-tracked open item that does not block this ticket.
