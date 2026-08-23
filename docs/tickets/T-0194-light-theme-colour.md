---
id: T-0194
title: Light theme carries colour (white canvas, saturated accents)
status: in_review
size: M
owner: claude
created: 2026-08-23
updated: 2026-08-23
depends_on: [T-0191]
blocks: []
user_stories: []
adrs: [0028]
phase: 8
manual_steps: []
security_touching: false
layers: [frontend]
---

# T-0194 — Light theme carries colour

## Context

Operator, on the T-0191 light theme: *"Light theme je takovej hrozne bez
barev"*, then, mid-implementation: *"background chci bily, ale ty barvy jsou
vybledle jinak vsude"*.

T-0191's light palette was rebuilt on apple.com's structure — near-neutral
greys, near-black ink, the teal left as the only chromatic thing on the page.
That fixed the "flat cyan wash" of the cut before it and overshot: with every
brand step pinned to AA-on-white (which forces a dark, low-chroma teal) and
every chip fill spelled as a 10 % opacity of that same dark teal, the light
theme had no saturated colour anywhere.

The fix is not a tint on the canvas — the operator explicitly wants the
background white. It is that **fill strength and fill ink are theme
properties**, not component properties:

- `bg-brand-500/10` means "a visible teal wash" on a near-black page and
  "white" on a white card. One utility cannot serve both themes.
- A text colour that clears AA on white does **not** clear AA on a fill
  saturated enough to read as colour, so a tinted chip needs its own ink.

Two new token families carry that, per [ADR 0028](../adr/0028-runtime-theming-css-variable-indirection.md#amendment-t-0194--tint-fills-and-their-ink):
`--tint-*` (the fill) and `--on-tint-*` (the ink that sits on it).

## Acceptance criteria

- **AC-1** — The light background is white.
  *Proof:* `body` computed background `rgb(255, 255, 255)` on `/`, `/katalog`,
  `/register`, the maker dashboard and the admin console, in Chrome (CDP) and
  Safari (safaridriver).
- **AC-2** — Chips, badges, icon tiles and status fills read as colour on the
  white page rather than as off-white.
  *Proof:* `.icon-tile` computed background `rgb(159, 227, 216)` in Safari;
  captures of `/katalog` (mint avatar tiles + "Ověřený výrobce" badge),
  `/dashboard/admin/orders` (mint "Přijato" / "Zaplaceno", amber "Čeká na
  platbu"), the maker dashboard (mint active tab and account chip).
- **AC-3** — The brand and status steps are at the chroma ceiling their
  contrast floor allows.
  *Proof:* brand text `#006e63` (was `#0c6259`), fills `#007a6f` / `#00847a`,
  status `#0a7a34` / `#8a5a00` / `#cc0d2f` / `#0067d0`; all gated below.
- **AC-4** — Contrast contract holds in both palettes, including the new
  fills.
  *Proof:* `npm run check:contrast` — 176 pairs pass, up from 168; the seven
  added pairs put every `on-tint-*` ink on its own tint at ≥ 4.5:1.
- **AC-5** — The dark theme is unchanged.
  *Proof:* the dark tints are the exact `color-mix(in oklab, …)` the `/10` and
  `/15` modifiers generated; `/katalog` dark capture matches the pre-change
  one; `--surface-primary` still `#0b1417`.
- **AC-6** — No hover state paints a tint under ink that then fails AA.
  *Proof:* every `hover:bg-tint-*` in `src/**/*.tsx` carries a matching
  `hover:text-*` (audited by grep; 10 call sites gained one).
- **AC-7** — The landing hero renders on the white page.
  *Proof:* Safari/WebKit capture of `/` at `data-theme=light`, canvas
  2148×933, knot / event horizon / stars visible.
- **AC-8** — Chrome and WebKit, 375 / 768 / 1280.
  *Proof:* captures at each width; Safari via safaridriver for the WebGL hero
  and the `color-mix()` tints.

## Out of scope

- The dark palette. Its tint strengths are pinned to what the opacity
  modifiers already produced.
- `opengraph-image.tsx` — fixed art, deliberately dark.
