---
ticket: T-0133
author: QA
created: 2026-06-21
adrs: [0023]
kind: manual-checklist
gate: pre-launch (NOT a merge gate)
---

# Manual accessibility checklist — T-0133

The leg the automated axe gate cannot cover (ADR 0023 §5). The vitest +
jest-axe suite (`npm run test:run`) enforces zero **structural** WCAG 2.1
AA violations on every PR — names, roles, labels, landmarks, heading
order, ARIA validity. It runs in jsdom, so it **cannot** evaluate:

- **Colour contrast** (Tailwind classes are not laid out / computed in
  jsdom — axe sees no resolved colours).
- **Reading order** and focus order as a sighted/AT user experiences it.
- **Screen-reader announcements** (label clarity in Czech, live-region
  updates, focus return).
- **Keyboard operability** of real interactive widgets (the Packeta
  shipping-point modal, the gallery, the payment poll).

This checklist is the human + assistive-tech pass. Per ADR 0023 §5 it
runs **once before launch** (and on any major release), executed by QA
with a screen reader. It is **not a merge gate** — the automated axe
suite is the merge gate; a finding here becomes a follow-up ticket, it
does not block the T-0133 PR.

## Preconditions

- A deployed build (staging or a Vercel preview) against a seeded backend
  — keyboard + SR testing needs the real rendered CSS + live data.
- **NVDA** (latest) on **Firefox** for the Czech screen-reader pass (ADR
  0023 §5 names NVDA + Firefox, Czech language). Cross-check one path on
  a second AT (VoiceOver/Safari) if available — not required for launch.
- Browser zoom 100%; OS display scaling 100% for the contrast spot-check.
- A contrast tool for the spot-check rows (browser DevTools contrast
  picker or the axe DevTools extension run against the live page — the
  live page is where contrast is actually evaluable).

## Critical customer paths under test (ADR 0023 §5)

1. **Catalog** `/katalog` (+ filters, pagination, empty state).
2. **Product detail** `/produkt/{id}` (gallery, order CTA).
3. **Checkout / order** `/objednavka` (form) → `/objednavka/{id}`
   (pre-payment, pay button) → `/objednavka/{id}/potvrzeni`
   (confirmation, payment poll).
4. **Static pages** `/jak-to-funguje`, `/pro-makery`, `/vop`, `/gdpr`.
5. **Auth forms** `/auth/login`, `/auth/register` (form-error wiring).

Format: ID | Path | Steps | Expected | Actual | Pass/Fail

### A. Keyboard-only navigation

| ID | Path | Steps | Expected | Actual | P/F |
|---|---|---|---|---|---|
| KB-1 | all | Tab from page load through the whole page | Focus order matches visual order; no element skipped; no off-screen trap | | |
| KB-2 | all | Observe the focus ring on every interactive element | Visible focus indicator on every link/button/input (no `outline:none` without a `focus-visible` replacement — ADR 0023 §5) | | |
| KB-3 | all | Shift+Tab back up | Reverse order is the mirror of forward; focus never lost to `<body>` mid-page | | |
| KB-4 | layout | Tab once from the very top | A skip-to-content affordance reaches the main landmark (or the first focus lands logically in the header nav) | | |
| KB-5 | catalog | Tab into filters; change category/city; Enter | Filter controls operable by keyboard; results update; focus not thrown to the top | | |
| KB-6 | catalog | Tab to a maker card; Enter | Activates the card link → maker profile | | |
| KB-7 | product | Tab to the gallery thumbnails; Space/Enter on each | Thumbnail buttons activate; `aria-pressed` reflects the selected one; primary image swaps | | |
| KB-8 | product | Tab to the "Objednat" CTA; Enter | Navigates to `/objednavka?productId=…` | | |
| KB-9 | checkout | Tab through the order form fields in order | Every field reachable; the attachment picker is keyboard-operable | | |
| KB-10 | checkout | Open the Packeta shipping-point widget by keyboard | Modal is reachable; **Esc closes it**; focus **returns** to the trigger on close; focus is trapped inside while open | | |
| KB-11 | confirmation | Land on `/objednavka/{id}/potvrzeni` while it polls | The "verifying" state is reachable; no keyboard trap during the poll; the success/failure CTA is reachable after the swap | | |
| KB-12 | auth | Tab through login/register; submit with an error | Error message is reachable; focus moves to (or is announced for) the first invalid field | | |

### B. NVDA + Firefox (Czech) screen-reader pass

| ID | Path | Steps | Expected | Actual | P/F |
|---|---|---|---|---|---|
| SR-1 | all | Load each path; listen to the page title | Page title announced in Czech and is meaningful (not just "Makables") | | |
| SR-2 | all | Navigate by landmark (D / NVDA landmark list) | `banner` (header), `main`, `contentinfo` (footer) present and announced; one `main` per page | | |
| SR-3 | all | Navigate by heading (H / heading list) | Exactly one `<h1>`; heading levels descend without skipping; headings read in document order | | |
| SR-4 | catalog | Browse the maker cards | Each card announces company name, city, rating, verified status in Czech; the rating is not announced as a bare number with no context | | |
| SR-5 | product | Navigate the gallery | Thumbnail buttons announce a localised name ("Náhled 2") + pressed state; primary image `alt` reads the product title once (not N+1 times) | | |
| SR-6 | checkout | Tab through the form with the virtual cursor off | Every input has an associated label read on focus; required fields announced; the sticky summary is reachable and read | | |
| SR-7 | checkout | Submit with a validation error | The error is announced and associated with its input via `aria-describedby`; the error is **not** colour-only (ADR 0023 §5) | | |
| SR-8 | confirmation | Land while the payment poll runs | The "ověřujeme platbu" state is announced; when it flips to success/failure the change is announced (live region), not silent | | |
| SR-9 | all | Read every button/link | Each has an accessible name in Czech; icon-only controls announce a label, not "tlačítko" with no name | | |
| SR-10 | static | Read `/vop` + `/gdpr` | The placeholder `Alert` (`role="alert"`) is announced; the warning banner is conveyed by text, not colour alone | | |
| SR-11 | auth | Read the login/register forms | Field purpose clear from the label alone; password hint announced; no reliance on placeholder-as-label | | |

### C. Colour-contrast spot-check (live page — ADR 0023 §5)

Run against the deployed page (contrast is not evaluable in jsdom). Brand
dark theme: body text **4.5:1**, large text (≥18.66px bold / ≥24px) **3:1**.

| ID | Path / element | Expected | Actual | P/F |
|---|---|---|---|---|
| CT-1 | catalog — `text-zinc-400`/`text-zinc-500` body copy on the dark surface | ≥ 4.5:1 | | |
| CT-2 | product — `text-brand-400` price + the brand CTA text on its background | ≥ 4.5:1 body / ≥ 3:1 large | | |
| CT-3 | checkout — form labels + the `text-zinc-500` helper/summary notes | ≥ 4.5:1 | | |
| CT-4 | static — the `Alert variant="warning"` amber text on its tinted panel | ≥ 4.5:1 | | |
| CT-5 | all — visible focus ring (`focus-visible:ring-brand-400`) against adjacent surfaces | ≥ 3:1 (focus indicator contrast) | | |
| CT-6 | badges (`Badge variant="brand"`) — label text on the badge fill | ≥ 4.5:1 | | |

## Recording the run

- Fill `Actual` + `P/F` per row during the pre-launch pass.
- Any **Fail** → file a follow-up ticket (link it here) and record the
  verdict in the sprint status / launch checklist. Per ADR 0023 §5 the
  maker/admin dashboards may carry accepted one-off issues; the rows
  above target the **customer-facing** AA surface.
- The automated axe suite (the merge gate) already covers the structural
  AA rules — this pass is the human + AT confirmation of the legs axe
  cannot see.
