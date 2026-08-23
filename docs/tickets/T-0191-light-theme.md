---
id: T-0191
title: Light theme for the whole web app
status: in_review
size: M
owner: claude
created: 2026-08-23
updated: 2026-08-23
depends_on: []
blocks: []
user_stories: []
adrs: [0028]
phase: 8
manual_steps: []
security_touching: false
layers: [frontend]
---

# T-0191 — Light theme

## Context

Operator: *"Priprav light theme pro tuto web app."*

The app was dark-only, with the palette inlined into every generated Tailwind
utility. See [ADR 0028](../adr/0028-runtime-theming-css-variable-indirection.md)
for why the fix is an indirection layer rather than 1 174 `dark:` variants.

## Acceptance criteria

- **AC-1** — Every page renders in a light theme with no per-page work.
  *Proof:* landing, katalog, maker detail, login, register, kontakt, 404, the
  maker dashboard (objednávky / produkty / výplaty / recenze) and the admin
  console (přehled / objednávky / fronta / uživatelé) captured at
  `data-theme=light`, body `rgb(237,243,245)`.
- **AC-2** — The dark theme is pixel-unchanged.
  *Proof:* dark palette hexes untouched in `globals.css`; katalog dark capture
  matches the pre-change layout; `--surface-primary` still `#0b1417`.
- **AC-3** — Theme follows the OS by default, is overridable, and persists.
  *Proof:* browser-driven cycle system → light → dark → system, with
  `localStorage` asserted at each step, surviving a hard reload and a
  client-side navigation.
- **AC-4** — No flash of the wrong palette on load.
  *Proof:* the resolved theme is written by a synchronous `<head>` script
  before first paint; hard reload with `light` pinned reports
  `data-theme=light` and light `body` background with no intermediate frame.
- **AC-5** — Contrast contract holds in BOTH palettes.
  *Proof:* `npm run check:contrast` — 168 pairs pass.
- **AC-6** — Chrome and WebKit.
  *Proof:* Chrome 151 via CDP and Safari/WebKit via safaridriver; both resolve
  the token indirection and the themed `color-mix()` opacity modifiers.
- **AC-7** — The landing hero animation runs on the light background, and the
  dark scene is unchanged.
  *Proof:* Safari/WebKit captures of `/` at `data-theme=light` (hero
  `rgb(245,245,247)`, `h1` `rgb(17,17,20)`, canvas 2028x933 with the knot,
  event horizon and corona visible) and at `data-theme=dark` (unchanged from
  the approved scene). Every additive-blended layer swaps to NormalBlending
  and draws dark; the corona's interior is punched out on light so the event
  horizon stays solid instead of turning grey-green.
- **AC-8** — The light palette reads bright rather than tinted-grey.
  *Proof:* rebuilt on apple.com's structure after the first cut was rejected
  as boring — `#f5f5f7` canvas, pure-white cards, `#1d1d1f` ink,
  `#d2d2d7`-weight hairlines. `check:contrast` still 168/168.

## Out of scope

- Renaming `zinc-*` / `surface-*` utilities to semantic names — see ADR 0028
  alternatives. The indirection added here makes that a later, cheap rename.
- `opengraph-image.tsx` — a fixed-art social card, deliberately dark.
