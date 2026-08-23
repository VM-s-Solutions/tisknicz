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
  *Proof:* `.icon-tile` (the accent tile) computed background
  `rgb(20, 184, 166)` in Safari;
  captures of `/katalog` (mint avatar tiles + "Ověřený výrobce" badge),
  `/dashboard/admin/orders` (mint "Přijato" / "Zaplaceno", amber "Čeká na
  platbu"), the maker dashboard (mint active tab and account chip).
- **AC-3** — The primary is saturated; nothing else is.
  *Proof:* the accent fill is `#14b8a6` (identity tiles, selected nav item,
  current timeline step) and brand text `#006f64` / `#00786c` (was `#0c6259`);
  chips and every status tint stay quiet pastels (`#cfeee9`, `#d7f2e0`,
  `#fbeacd`, `#fbdde1`, `#dbe9fb`) under near-black ink of their own hue.
  Three rounds of operator feedback pinned this: "hrozne bez barev" →
  "vyblity jak kdybych byl barvoslepy" → "ted je to jak omalovanky, popremysli
  nad spravnymi pomery barev". The budget the palette now holds is ~60 % white
  / 30 % neutral / 10 % primary, and the component styles are unchanged
  throughout — only the palette moved.

- **AC-4** — Contrast contract holds in both palettes, including the new
  fills.
  *Proof:* `npm run check:contrast` — 175 pairs pass, up from 168; the seven
  added pairs put every `on-tint-*` ink on its own tint at ≥ 4.5:1.
- **AC-5** — The dark theme is unchanged.
  *Proof:* the dark tints are the exact `color-mix(in oklab, …)` the `/10` and
  `/15` modifiers generated; `/katalog` dark capture matches the pre-change
  one; `--surface-primary` still `#0b1417`.
- **AC-6** — No `bg-tint-*` anywhere sits under ink that is not
  `text-on-tint-*`, at rest or on hover.
  *Proof:* `grep -rn "bg-tint-" --include="*.tsx" | grep -v text-on-tint`
  returns nothing; 29 call sites (10 of them hover-only) gained the ink class.
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
