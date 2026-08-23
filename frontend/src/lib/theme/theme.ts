/**
 * Theme preference model (T-0191).
 *
 * Pure, DOM-free logic so it can be unit-tested and so the inline bootstrap
 * script below shares one storage key with the React toggle instead of
 * duplicating a string that could drift.
 *
 * Three preferences, two outcomes: `system` defers to the OS, `light` and
 * `dark` pin it. Only the RESOLVED value is written to `data-theme` on
 * `<html>` — `globals.css` never has to reason about "system".
 */
export const THEME_STORAGE_KEY = 'makables-theme';

export type ThemePreference = 'system' | 'light' | 'dark';
export type ResolvedTheme = 'light' | 'dark';

/** Cycle order for the toggle button. */
export const THEME_PREFERENCES = ['system', 'light', 'dark'] as const;

export function isThemePreference(value: unknown): value is ThemePreference {
  return value === 'system' || value === 'light' || value === 'dark';
}

export function resolveTheme(
  preference: ThemePreference,
  systemPrefersDark: boolean
): ResolvedTheme {
  if (preference === 'system') {
    return systemPrefersDark ? 'dark' : 'light';
  }
  return preference;
}

/** Next preference in the cycle: system → light → dark → system. */
export function nextThemePreference(current: ThemePreference): ThemePreference {
  const index = THEME_PREFERENCES.indexOf(current);
  return THEME_PREFERENCES[(index + 1) % THEME_PREFERENCES.length];
}

/**
 * A malformed or absent stored value falls back to `system` rather than to a
 * fixed theme: a visitor who has never chosen should follow their OS, and a
 * corrupted entry should behave like a visitor who has never chosen.
 */
export function parseStoredPreference(raw: string | null): ThemePreference {
  return isThemePreference(raw) ? raw : 'system';
}

/**
 * Runs in `<head>` before first paint, so the correct palette is on the
 * first frame — a theme applied from `useEffect` shows one frame of the
 * wrong one, which on a dark→light swap is a full-screen white flash.
 *
 * Deliberately dependency-free and wrapped in try/catch: `localStorage`
 * throws outright in a cookie-blocked iframe or Safari private mode, and a
 * throw here would leave the page with no `data-theme` at all. The catch
 * falls through to the CSS default rather than guessing.
 */
export const THEME_BOOTSTRAP_SCRIPT = `(function(){try{var p=localStorage.getItem(${JSON.stringify(
  THEME_STORAGE_KEY
)});if(p!=='light'&&p!=='dark'){p=window.matchMedia('(prefers-color-scheme: dark)').matches?'dark':'light';}document.documentElement.setAttribute('data-theme',p);}catch(e){}})();`;
