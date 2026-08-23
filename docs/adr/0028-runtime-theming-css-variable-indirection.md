---
id: 0028
title: Runtime theming through CSS custom-property indirection
status: accepted
date: 2026-08-23
deciders: [Architect, frontend, claude]
living_docs: []
---

# 0028 — Runtime theming through CSS custom-property indirection

## Context

The app shipped dark-only. Every colour in it is spelled as a Tailwind utility
(`text-zinc-400`, `bg-surface-card`, `border-brand-500/60`), and the palette was
declared in one `@theme inline { … }` block of literal hexes in
[globals.css](../../frontend/src/app/globals.css). Adding a light theme touched
**1 174 `zinc-*` usages across 193 files** plus 204 `surface-*` and 369
`brand-*` usages — far past the point where per-component work is sane.

Two properties of the existing setup shaped the decision:

- `@theme inline` bakes the *literal value* into every generated utility
  (`.bg-surface-card { background: #121d21 }`). That is precisely what makes a
  runtime swap impossible: there is nothing left to re-point.
- A handful of usages are *inverted* — `text-zinc-950` on a bright teal fill,
  `bg-zinc-100` for the switch knob. These read "the opposite end of the ramp",
  not "step 950", so a blind lightness flip inverts their contrast.

The dark palette is approved and repeatedly re-litigated (three rounds of
restyling), so the light theme had to be additive: not one dark pixel moves.

## Decision

Colour resolves through three layers instead of one.

1. **Raw palettes** — `--dk-*` and `--lt-*` custom properties on `:root`. Each
   hex is declared exactly once in the codebase, here.
2. **Active tokens** — `--ink-*`, `--surface-*`, `--brand-*`, `--status-*`.
   `:root` points them at the dark palette; `[data-theme='light']` re-points
   them at the light one.
3. **`@theme inline`** hands the *active tokens* to Tailwind, so every generated
   utility emits `var(--ink-400)` rather than a literal.

The applied theme is the resolved value (`light` / `dark`, never `system`)
written to `data-theme` on `<html>` by an inline bootstrap script in `<head>`.
Preference (`system` / `light` / `dark`) lives in `localStorage`.

Because the theme selectors are plain attribute selectors rather than
`:root[…]`, **a subtree can pin its own theme**: `<section data-theme="dark">`
re-declares the tokens for its descendants. The landing hero used this as its
first-cut answer to the WebGL problem below; the scene was subsequently
re-authored for light and the pin removed.

## Alternatives considered

- **Sweep the utilities into `dark:` variants** — rejected: 1 174 call sites,
  every one a chance to typo a step, and it doubles the class string on almost
  every element in the app for no expressive gain.
- **Mirroring the dark ramp's lightness for the light palette** — the obvious
  first move, and what shipped in the first pass. Rejected after review: a
  mirrored cyan-slate ramp reads as a flat, murky wash, because a hue that
  gives a large dark field its character makes a large light field look dirty.
  The light palette is now built on the apple.com model instead — near-neutral
  greys, near-black ink, true-white cards on a `#f5f5f7` canvas — with contrast
  carried by surface steps rather than by hue.
- **A semantic-token rename (`text-muted`, `bg-card`, …)** — the "correct"
  refactor, and still worth doing one day, but it is a 193-file rename landing
  in the same PR as a palette change. Rejected for this ticket: it makes the
  diff unreviewable and couples "add a theme" to "rename the design system".
  The indirection layer added here is what makes that rename cheap later.
- **`light-dark()` CSS function** — rejected: it collapses the whole thing into
  one declaration per token, but it is Baseline-2024 and this app has been bitten
  by Safari twice (WebGL hero, `Secure` cookies on localhost). Plain custom
  properties introduce **zero** new browser-feature surface: the `color-mix()`
  Tailwind already emits for `/60` opacity modifiers keeps working unchanged.
- **`next-themes`** — rejected: ~40 lines of bootstrap script and store replace
  a dependency, and CLAUDE.md already forbids global state libraries. The theme
  is one attribute on one element.

## Consequences

- Positive: a theme is ~35 variable re-assignments. No component knows a theme
  exists. Adding a third (high-contrast, print) is one more selector block.
- Positive: subtree theming falls out for free — the escape hatch for any
  surface that cannot follow the palette, and the hero's stopgap while its
  scene was still dark-only.
- Positive: the contrast contract became executable —
  [`scripts/check-contrast.mjs`](../../frontend/scripts/check-contrast.mjs)
  re-derives 168 text/surface pairs from the stylesheet in both palettes.
- Negative: one level of indirection between a utility and its hex. Reading a
  colour in DevTools now shows `var(--ink-400)`; the resolved value is one hop
  away in the computed panel.
- Negative: the active-token assignment list is repeated three times (dark
  default, `[data-theme='light']`, `[data-theme='dark']` for subtree pinning)
  plus a no-JS media-query fallback. The *hexes* are not repeated, so the two
  palettes cannot drift — but a newly added token must be added to each block.
  `check:contrast` fails loudly on a token missing from a palette.
- Neutral: `text-white` is no longer legal anywhere — it cannot follow a theme.
  Titles use `text-zinc-50`, ink on a brand fill uses `text-on-brand`.
- Neutral: a `<canvas>` gets none of this for free. WebGL materials hold their
  own colour and blend state, so `hero-scene.tsx` subscribes to the resolved
  theme and reads the tokens back out of computed style. Any future canvas or
  chart has to do the same.

## Compliance / verification

- `npm run check:contrast` passes (gate; 176 pairs, both palettes).
- No literal `#hex` / `rgb()` / `text-white` / stock Tailwind hue
  (`red-*`, `amber-*`, `emerald-*`, …) in `src/**/*.tsx` outside
  `opengraph-image.tsx` (a fixed social card) and the dark branch of
  `hero-scene.tsx`'s palette, which pins the authored scene by design.
- `@theme inline` contains only `var(--…)` values, never a literal colour.
- A new colour token is added to `--dk-*` **and** `--lt-*` **and** all three
  active-token blocks.

## Amendment (T-0194) — tint fills and their ink

The indirection above covers *colours*. It did not cover **fill strength**,
and that turned out to be the thing that made the light theme read as
colourless.

A chip, badge, icon tile or selected row was spelled `bg-brand-500/10`. That
utility bakes one strength into the component, and a strength is not portable
between palettes: 10 % of the teal over the near-black page is a clearly
visible wash, while 10 % of the (necessarily dark, because it must clear AA on
white) light-theme teal over a white card is white. The same applies to the
ink: a text colour picked to clear 4.5:1 on white does not clear 4.5:1 on a
fill saturated enough to actually read as colour.

Two token families close that gap, and they follow the same three-layer shape
as everything else:

- `--tint-brand` / `--tint-brand-strong` / `--tint-success` / `--tint-warning`
  / `--tint-error` / `--tint-error-strong` / `--tint-info` — the fill. Dark
  spells them as the exact `color-mix(in oklab, var(--dk-…) 10%, transparent)`
  the opacity modifiers used to generate, so nothing dark moved; light spells
  them as flat, saturated hexes mixed from the *vivid* end of each hue.
- `--on-tint-brand` / `--on-tint-success` / `--on-tint-warning` /
  `--on-tint-error` / `--on-tint-info` — the ink that sits on a tint. Its own
  token for the same reason `--on-brand` is: on dark the chip is a wash over a
  near-black page and the text must be the bright end of the hue, on light the
  chip is a saturated pastel and the text must be the dark end. A ramp step
  cannot mean both.

Consequences:

- `bg-brand-500/10`-style fills are banned in `src/**/*.tsx`; a fill is
  `bg-tint-*` and its text is `text-on-tint-*`. Opacity modifiers remain
  legal for *borders* and *rings*, which carry no text.
- A `hover:bg-tint-*` must come with a `hover:text-on-tint-*` whenever the
  resting ink is a plain status/brand step — the tint is strong enough to
  break AA under it.
- `check-contrast.mjs` grew a `LIGHT_ONLY_PAIRS` list: the dark tints are
  `color-mix()` expressions and cannot be measured from the stylesheet, while
  the light ones are flat hexes and are gated like any other surface.
- The light palette itself is on its third cut. Mirroring the dark cyan-slate
  ramp read as a flat wash; the near-neutral apple.com rebuild read as no
  colour at all. What ships now is a **white** canvas and white cards (the
  operator's explicit ask) with the colour carried entirely by saturated
  fills, chips and accents rather than by a background tint.

