import { describe, expect, it } from 'vitest';
import {
  THEME_BOOTSTRAP_SCRIPT,
  THEME_PREFERENCES,
  THEME_STORAGE_KEY,
  isThemePreference,
  nextThemePreference,
  parseStoredPreference,
  resolveTheme,
  type ThemePreference,
} from './theme';

describe('resolveTheme', () => {
  it('defers to the OS while the preference is system', () => {
    expect(resolveTheme('system', true)).toBe('dark');
    expect(resolveTheme('system', false)).toBe('light');
  });

  it('ignores the OS once a theme is pinned', () => {
    expect(resolveTheme('light', true)).toBe('light');
    expect(resolveTheme('dark', false)).toBe('dark');
  });
});

describe('nextThemePreference', () => {
  it('cycles system -> light -> dark -> system', () => {
    expect(nextThemePreference('system')).toBe('light');
    expect(nextThemePreference('light')).toBe('dark');
    expect(nextThemePreference('dark')).toBe('system');
  });

  it('returns to every preference within one full cycle', () => {
    const seen = new Set<string>();
    let current: ThemePreference = THEME_PREFERENCES[0];
    for (let step = 0; step < THEME_PREFERENCES.length; step += 1) {
      seen.add(current);
      current = nextThemePreference(current);
    }
    // A two-way light/dark flip would strand the user away from "system";
    // the cycle must reach all three and come back to where it started.
    expect(seen).toEqual(new Set(THEME_PREFERENCES));
    expect(current).toBe(THEME_PREFERENCES[0]);
  });
});

describe('parseStoredPreference', () => {
  it('accepts the three valid values', () => {
    expect(parseStoredPreference('system')).toBe('system');
    expect(parseStoredPreference('light')).toBe('light');
    expect(parseStoredPreference('dark')).toBe('dark');
  });

  it('falls back to system for an absent or corrupted entry', () => {
    // A visitor who has never chosen and a visitor whose entry got mangled
    // should both follow the OS rather than be pinned to a fixed theme.
    expect(parseStoredPreference(null)).toBe('system');
    expect(parseStoredPreference('')).toBe('system');
    expect(parseStoredPreference('LIGHT')).toBe('system');
    expect(parseStoredPreference('{"theme":"light"}')).toBe('system');
  });
});

describe('isThemePreference', () => {
  it('rejects non-preference values', () => {
    expect(isThemePreference(undefined)).toBe(false);
    expect(isThemePreference(0)).toBe(false);
    expect(isThemePreference('auto')).toBe(false);
  });
});

describe('THEME_BOOTSTRAP_SCRIPT', () => {
  /**
   * The script is injected as raw HTML, runs before any bundle, and shares
   * its storage key with the React toggle. These assertions pin the three
   * properties that make that safe.
   */
  it('uses the same storage key as the toggle', () => {
    expect(THEME_BOOTSTRAP_SCRIPT).toContain(JSON.stringify(THEME_STORAGE_KEY));
  });

  it('cannot break out of the inline <script> element', () => {
    expect(THEME_BOOTSTRAP_SCRIPT).not.toContain('</script');
  });

  it('swallows a throwing localStorage instead of leaving the page unthemed', () => {
    expect(THEME_BOOTSTRAP_SCRIPT).toContain('catch');
  });

  it('applies the resolved theme that the CSS actually keys on', () => {
    const documentElement = { attributes: new Map<string, string>() };
    const runBootstrap = (stored: string | null, prefersDark: boolean) => {
      documentElement.attributes.clear();
      const scope = {
        localStorage: { getItem: () => stored },
        window: { matchMedia: () => ({ matches: prefersDark }) },
        document: {
          documentElement: {
            setAttribute: (name: string, value: string) =>
              documentElement.attributes.set(name, value),
          },
        },
      };
      // Run the real script text, not a re-implementation of it — a
      // re-implementation would keep passing after the script regressed.
      new Function(
        'localStorage',
        'window',
        'document',
        THEME_BOOTSTRAP_SCRIPT
      )(scope.localStorage, scope.window, scope.document);
      return documentElement.attributes.get('data-theme');
    };

    expect(runBootstrap('light', true)).toBe('light');
    expect(runBootstrap('dark', false)).toBe('dark');
    expect(runBootstrap(null, true)).toBe('dark');
    expect(runBootstrap(null, false)).toBe('light');
    // "system" is never written to the DOM — only ever resolved away.
    expect(runBootstrap('system', false)).toBe('light');
    expect(runBootstrap('nonsense', true)).toBe('dark');
  });
});
