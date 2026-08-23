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
re-declares the tokens for its descendants. The landing hero uses this.

## Alternatives considered

- **Sweep the utilities into `dark:` variants** — rejected: 1 174 call sites,
  every one a chance to typo a step, and it doubles the class string on almost
  every element in the app for no expressive gain.
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
- Positive: subtree theming falls out for free, which is what let the additive-
  blended WebGL hero keep its exact artwork on a light page.
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

## Compliance / verification

- `npm run check:contrast` passes (gate; 168 pairs, both palettes).
- No literal `#hex` / `rgb()` / `text-white` / stock Tailwind hue
  (`red-*`, `amber-*`, `emerald-*`, …) in `src/**/*.tsx` outside
  `hero-scene.tsx` and `opengraph-image.tsx`, both of which are fixed art.
- `@theme inline` contains only `var(--…)` values, never a literal colour.
- A new colour token is added to `--dk-*` **and** `--lt-*` **and** all three
  active-token blocks.
