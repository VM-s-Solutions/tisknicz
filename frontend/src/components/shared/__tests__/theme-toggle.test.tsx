import { fireEvent, render, screen } from '@testing-library/react';
import { axe } from 'jest-axe';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { ThemeToggle } from '@/components/shared/theme-toggle';
import { THEME_STORAGE_KEY } from '@/lib/theme/theme';

function setSystemPrefersDark(prefersDark: boolean): void {
  window.matchMedia = ((query: string) =>
    ({
      matches: query.includes('dark') ? prefersDark : false,
      media: query,
      onchange: null,
      addEventListener: () => {},
      removeEventListener: () => {},
      addListener: () => {},
      removeListener: () => {},
      dispatchEvent: () => false,
    })) as unknown as typeof window.matchMedia;
}

beforeEach(() => {
  window.localStorage.clear();
  document.documentElement.removeAttribute('data-theme');
  setSystemPrefersDark(true);
});

afterEach(() => {
  window.localStorage.clear();
  document.documentElement.removeAttribute('data-theme');
});

describe('ThemeToggle', () => {
  it('cycles system → light → dark → system and writes the resolved theme to <html>', () => {
    render(<ThemeToggle />);
    const button = screen.getByRole('button');

    // Starts on "system"; nothing stored, nothing forced.
    expect(button).toHaveAccessibleName(/Podle systému/);
    expect(window.localStorage.getItem(THEME_STORAGE_KEY)).toBeNull();

    fireEvent.click(button);
    expect(document.documentElement.getAttribute('data-theme')).toBe('light');
    expect(window.localStorage.getItem(THEME_STORAGE_KEY)).toBe('light');

    fireEvent.click(button);
    expect(document.documentElement.getAttribute('data-theme')).toBe('dark');
    expect(window.localStorage.getItem(THEME_STORAGE_KEY)).toBe('dark');

    // Back to "system": the key is REMOVED, not set to the string "system",
    // so the bootstrap script and the toggle agree on what "unset" means.
    fireEvent.click(button);
    expect(window.localStorage.getItem(THEME_STORAGE_KEY)).toBeNull();
    // …and the resolved value follows the OS, which prefers dark here.
    expect(document.documentElement.getAttribute('data-theme')).toBe('dark');
  });

  it('resolves "system" against a light OS', () => {
    setSystemPrefersDark(false);
    render(<ThemeToggle />);
    const button = screen.getByRole('button');

    fireEvent.click(button); // light
    fireEvent.click(button); // dark
    fireEvent.click(button); // system
    expect(document.documentElement.getAttribute('data-theme')).toBe('light');
  });

  it('reads an already-stored preference instead of restarting at system', () => {
    window.localStorage.setItem(THEME_STORAGE_KEY, 'light');
    render(<ThemeToggle />);
    expect(screen.getByRole('button')).toHaveAccessibleName(/Světlý/);
  });

  it('names both the current and the next theme, so the control is not icon-only to a screen reader', () => {
    render(<ThemeToggle />);
    const button = screen.getByRole('button');
    expect(button).toHaveAccessibleName('Motiv: Podle systému. Přepnout na: Světlý.');
    expect(button).toHaveAttribute('title', 'Přepnout na motiv: Světlý');
  });

  it('has no accessibility violations', async () => {
    const { container } = render(<ThemeToggle />);
    expect(await axe(container)).toHaveNoViolations();
  });
});
