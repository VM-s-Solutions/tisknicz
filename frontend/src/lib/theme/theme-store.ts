'use client';

import {
  THEME_STORAGE_KEY,
  parseStoredPreference,
  resolveTheme,
  type ResolvedTheme,
  type ThemePreference,
} from '@/lib/theme/theme';

/**
 * Client-side store for the theme preference (T-0191).
 *
 * Exists so the toggle can read the preference through `useSyncExternalStore`
 * instead of copying it into React state from an effect. The preference is
 * genuinely external — it lives in `localStorage`, it can change in another
 * tab, and while it is `system` the OS can change the resolved value out from
 * under us — so it is a subscription, not component state.
 */
const listeners = new Set<() => void>();

function darkQuery(): MediaQueryList {
  return window.matchMedia('(prefers-color-scheme: dark)');
}

/**
 * Writes the RESOLVED theme onto <html>, which is the only thing `globals.css`
 * keys on. Same contract as the bootstrap script in `app/layout.tsx`.
 */
export function applyTheme(preference: ThemePreference): void {
  document.documentElement.setAttribute(
    'data-theme',
    resolveTheme(preference, darkQuery().matches)
  );
}

export function readPreference(): ThemePreference {
  try {
    return parseStoredPreference(window.localStorage.getItem(THEME_STORAGE_KEY));
  } catch {
    // Storage throws outright in Safari private mode and in a cookie-blocked
    // iframe. Behave like a visitor who has never chosen.
    return 'system';
  }
}

/**
 * The server has no access to the visitor's storage, so it always renders the
 * neutral state. React re-renders with the real snapshot straight after
 * hydration; no mismatch, and no wrong palette either — `data-theme` was
 * already applied before first paint.
 */
export function readServerPreference(): ThemePreference {
  return 'system';
}

export function writePreference(preference: ThemePreference): void {
  try {
    if (preference === 'system') {
      // Remove rather than store "system": an absent key and an explicit
      // "system" mean the same thing to the bootstrap script, and removing
      // stops a stale value from outliving a future rename.
      window.localStorage.removeItem(THEME_STORAGE_KEY);
    } else {
      window.localStorage.setItem(THEME_STORAGE_KEY, preference);
    }
  } catch {
    // Preference is lost on reload, but the current page still switches.
  }
  applyTheme(preference);
  for (const listener of listeners) listener();
}

export function subscribe(onStoreChange: () => void): () => void {
  listeners.add(onStoreChange);

  // Another tab changed the preference.
  const onStorage = (event: StorageEvent) => {
    if (event.key === THEME_STORAGE_KEY || event.key === null) {
      applyTheme(readPreference());
      onStoreChange();
    }
  };
  window.addEventListener('storage', onStorage);

  // The OS switched appearance (macOS auto appearance at sunset, a Windows
  // schedule). Only matters while the preference is `system`, and `applyTheme`
  // already resolves that — the bootstrap script runs on full loads only.
  const media = darkQuery();
  const onSystemChange = () => {
    applyTheme(readPreference());
    onStoreChange();
  };
  media.addEventListener('change', onSystemChange);

  return () => {
    listeners.delete(onStoreChange);
    window.removeEventListener('storage', onStorage);
    media.removeEventListener('change', onSystemChange);
  };
}

/**
 * The RESOLVED theme currently painted on `<html>` — for code that has to
 * react to the palette rather than merely inherit it. CSS gets the swap for
 * free through the custom properties; a `<canvas>` does not, because WebGL
 * materials hold their own colour and blend state (see `hero-scene.tsx`).
 */
export function readResolvedTheme(): ResolvedTheme {
  return document.documentElement.getAttribute('data-theme') === 'light' ? 'light' : 'dark';
}

/** Matches the `:root` default, which is what a no-JS render paints. */
export function readServerResolvedTheme(): ResolvedTheme {
  return 'dark';
}

/**
 * `data-theme` is written directly to the DOM — by the bootstrap script
 * before hydration, and by `applyTheme` afterwards — so there is no React
 * state to subscribe to. Observe the attribute itself.
 */
export function subscribeToResolvedTheme(onStoreChange: () => void): () => void {
  const observer = new MutationObserver(onStoreChange);
  observer.observe(document.documentElement, {
    attributes: true,
    attributeFilter: ['data-theme'],
  });
  return () => observer.disconnect();
}
