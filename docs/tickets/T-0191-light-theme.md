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
- **AC-7** — The landing hero keeps its approved WebGL artwork.
  *Proof:* the hero section pins `data-theme="dark"`; on a light page its
  background stays `rgb(11,20,23)` and its `h1` `rgb(240,247,249)`, verified in
  both browsers.

## Out of scope

- Renaming `zinc-*` / `surface-*` utilities to semantic names — see ADR 0028
  alternatives. The indirection added here makes that a later, cheap rename.
- Re-authoring the hero WebGL scene for a light background.
- `opengraph-image.tsx` — a fixed-art social card, deliberately dark.
