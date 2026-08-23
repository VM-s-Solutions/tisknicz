#!/usr/bin/env node
/**
 * WCAG contrast gate for the theme palettes (T-0191).
 *
 * The design language fixes a hard contrast contract — a muted-text floor on
 * every surface the text can land on. That contract used to live only in a
 * comment in `globals.css`, and a light theme doubles the ground it has to
 * hold. This re-derives every pair straight from the stylesheet so the two
 * palettes can never drift apart or silently regress.
 *
 * Reads the raw `--dk-*` / `--lt-*` constants (each hex is declared exactly
 * once there), so it needs no CSS cascade emulation.
 *
 *   node scripts/check-contrast.mjs          # gate, exits 1 on a violation
 *   node scripts/check-contrast.mjs --report # per-token worst case, exits 0
 */
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const CSS_PATH = join(here, '..', 'src', 'app', 'globals.css');

function parsePalettes(css) {
  const palettes = { dark: {}, light: {} };
  const declaration = /--(dk|lt)-([a-z0-9-]+)\s*:\s*(#[0-9a-fA-F]{3,8})\s*;/g;
  let match;
  while ((match = declaration.exec(css)) !== null) {
    const [, prefix, name, hex] = match;
    palettes[prefix === 'dk' ? 'dark' : 'light'][name] = hex;
  }
  return palettes;
}

function toRgb(hex) {
  const value = hex.replace('#', '');
  const full = value.length === 3 ? value.split('').map((c) => c + c).join('') : value;
  return [0, 2, 4].map((i) => parseInt(full.slice(i, i + 2), 16));
}

/** WCAG 2.1 relative luminance. */
function luminance(hex) {
  const [r, g, b] = toRgb(hex).map((channel) => {
    const c = channel / 255;
    return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
  });
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

function contrast(a, b) {
  const [hi, lo] = [luminance(a), luminance(b)].sort((x, y) => y - x);
  return (hi + 0.05) / (lo + 0.05);
}

/**
 * Backgrounds text lands on, split by the bar each has to clear.
 *
 * REST — the four surfaces plus `ink-900`, the recessed control fill behind
 * every input, textarea and dropdown trigger. Text sits on these at rest, so
 * they hold the AAA target.
 *
 * STATE — `ink-800`, the hover / chip fill. A transient state that in
 * practice only carries high-emphasis text, so it holds AA. The split is not
 * a convenience: the shipped dark ramp puts `ink-400` at 6.45:1 on `ink-800`,
 * so one flat AAA number would have meant restyling the approved dark theme
 * as a side effect of adding a light one.
 */
const REST_BACKGROUNDS = [
  'surface-primary',
  'surface-secondary',
  'surface-card',
  'surface-elevated',
  'ink-900',
];
const STATE_BACKGROUNDS = ['ink-800'];

/** foreground -> minimum ratio on a rest surface / on a state fill. */
const TEXT_MINIMUMS = {
  'ink-50': { rest: 7, state: 7 },
  'ink-100': { rest: 7, state: 7 },
  'ink-200': { rest: 7, state: 7 },
  'ink-300': { rest: 7, state: 7 },
  'ink-400': { rest: 7, state: 4.5 },
  'ink-500': { rest: 4.5, state: 4.5 },
  'brand-200': { rest: 4.5, state: 4.5 },
  'brand-300': { rest: 4.5, state: 4.5 },
  'brand-400': { rest: 4.5, state: 4.5 },
  'status-success': { rest: 4.5, state: 4.5 },
  'status-warning': { rest: 4.5, state: 4.5 },
  'status-error': { rest: 4.5, state: 4.5 },
  'status-info': { rest: 4.5, state: 4.5 },
};

/** Explicit pairs that are not text-on-surface. */
const PAIRS = [
  // Ink on a solid brand fill: checkbox tick, selected calendar day,
  // completed timeline step. Small text, so AA applies. This is the pair
  // that forces `--on-brand` to be its own token rather than a zinc step —
  // a zinc step flips with the theme and takes the contrast with it.
  { fg: 'on-brand', bg: 'brand-400', min: 4.5 },
  { fg: 'on-brand', bg: 'brand-500', min: 4.5 },
  // WCAG 1.4.11 (3:1) on the boundary that identifies a control: the
  // switch / checkbox / radio border against the surface behind it.
  //
  // The switch KNOB against its own track is deliberately NOT asserted.
  // The knob is separated by elevation (shadow) and position, its state is
  // carried by the track colour plus role="switch" + aria-checked, and
  // demanding 3:1 there would mean darkening the approved teal "on" track
  // in the shipped dark theme.
  { fg: 'ink-600', bg: 'surface-card', min: 3 },
  { fg: 'ink-600', bg: 'surface-primary', min: 3 },
  // Hairline dividers are decorative and exempt from 1.4.11; these floors
  // only keep them from vanishing into the surface they divide.
  { fg: 'ink-800', bg: 'surface-card', min: 1.15 },
  { fg: 'ink-700', bg: 'surface-card', min: 1.35 },
];

/**
 * Pairs that exist in ONE palette only.
 *
 * The tint fills are the case: the dark theme spells them as a `color-mix()`
 * of a palette colour (so the chip stays translucent over whatever surface it
 * lands on, exactly as the `/10` utility did), while the light theme spells
 * them as flat hexes, because a 10 % mix of an AA-dark teal into white is
 * white. Only the flat side can be measured here, and only the flat side
 * needs measuring — the dark tints sit on dark surfaces under bright text
 * that already clears its floor on `ink-800`, a darker fill than any tint.
 */
const LIGHT_ONLY_PAIRS = [
  { fg: 'on-tint-brand', bg: 'tint-brand', min: 4.5 },
  { fg: 'on-tint-brand', bg: 'tint-brand-strong', min: 4.5 },
  { fg: 'on-tint-success', bg: 'tint-success', min: 4.5 },
  { fg: 'on-tint-warning', bg: 'tint-warning', min: 4.5 },
  { fg: 'on-tint-error', bg: 'tint-error', min: 4.5 },
  { fg: 'on-tint-error', bg: 'tint-error-strong', min: 4.5 },
  { fg: 'on-tint-info', bg: 'tint-info', min: 4.5 },
  // WCAG 1.4.11 on the hairline that IS the button: the light theme spends
  // its accent on this line, so it has to stay a boundary and not become
  // decoration.
  { fg: 'brand-line', bg: 'surface-card', min: 3 },
  { fg: 'brand-line', bg: 'surface-primary', min: 3 },
  { fg: 'brand-line', bg: 'surface-secondary', min: 3 },
  // The label inside that boundary. Gated on the surfaces a control actually
  // sits on — page, card, band — and deliberately not on `surface-elevated`,
  // which is the image/skeleton fill and never hosts a button.
  { fg: 'brand-ink', bg: 'surface-card', min: 4.5 },
  { fg: 'brand-ink', bg: 'surface-primary', min: 4.5 },
  { fg: 'brand-ink', bg: 'surface-secondary', min: 4.5 },
  // A control keeps its brand label through hover and press, so its own fill
  // is gated against that label rather than against a near-black chip ink.
  // The pressed state darkens the label one ramp step (`active:text-brand-300`),
  // which is a no-op on dark where `brand-ink` already IS `brand-300`.
  { fg: 'brand-ink', bg: 'brand-fill-soft', min: 4.5 },
  { fg: 'brand-300', bg: 'brand-fill-soft-strong', min: 4.5 },
  { fg: 'status-error', bg: 'error-fill-soft', min: 4.5 },
  { fg: 'status-error', bg: 'error-fill-soft-strong', min: 4.5 },
  // No ramp step may be used as ink on a light tint: the fills are solid and
  // saturated, so anything but `on-tint-*` (white) fails. A `bg-tint-*` in a
  // component must always be paired with `text-on-tint-*`.
];

function run() {
  const css = readFileSync(CSS_PATH, 'utf8');
  const palettes = parsePalettes(css);
  const report = process.argv.includes('--report');
  const failures = [];
  let checks = 0;

  for (const [themeName, palette] of Object.entries(palettes)) {
    const prefix = themeName === 'dark' ? 'dk' : 'lt';
    if (Object.keys(palette).length === 0) {
      failures.push(`palette "${themeName}" parsed as empty — did the --dk-/--lt- naming change?`);
      continue;
    }

    const rows = [];
    const check = (fg, bg, min) => {
      const fgHex = palette[fg];
      const bgHex = palette[bg];
      if (!fgHex || !bgHex) {
        failures.push(`${themeName}: missing token --${prefix}-${fgHex ? bg : fg}`);
        return;
      }
      checks += 1;
      const ratio = contrast(fgHex, bgHex);
      if (ratio < min) {
        failures.push(
          `${themeName}: ${fg} (${fgHex}) on ${bg} (${bgHex}) = ${ratio.toFixed(2)}:1, needs ${min}:1`
        );
      }
      rows.push({ fg, bg, ratio, min, ok: ratio >= min });
    };

    for (const [fg, minimums] of Object.entries(TEXT_MINIMUMS)) {
      for (const bg of REST_BACKGROUNDS) check(fg, bg, minimums.rest);
      for (const bg of STATE_BACKGROUNDS) check(fg, bg, minimums.state);
    }
    for (const pair of PAIRS) check(pair.fg, pair.bg, pair.min);
    if (themeName === 'light') {
      for (const pair of LIGHT_ONLY_PAIRS) check(pair.fg, pair.bg, pair.min);
    }

    if (report) {
      console.log(`\n=== ${themeName} ===`);
      const worstByFg = new Map();
      for (const row of rows) {
        const worst = worstByFg.get(row.fg);
        // Rank by headroom, not by raw ratio: a 5:1 that needs 4.5 is
        // tighter than a 6.4:1 that needs 4.5, and the tight one is what a
        // future palette edit will break first.
        if (!worst || row.ratio / row.min < worst.ratio / worst.min) worstByFg.set(row.fg, row);
      }
      for (const [fg, row] of worstByFg) {
        console.log(
          `${row.ok ? 'ok  ' : 'FAIL'} ${fg.padEnd(16)} ${row.ratio.toFixed(2).padStart(6)}:1 (>= ${String(row.min).padEnd(4)}) on ${row.bg}`
        );
      }
    }
  }

  if (failures.length > 0) {
    console.error(`\ncheck:contrast — ${failures.length} violation(s) across ${checks} checks:\n`);
    for (const failure of failures) console.error(`  x ${failure}`);
    console.error('');
    process.exit(1);
  }

  console.log(`check:contrast — ${checks} pairs pass in both themes.`);
}

run();
