import { act, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { GoogleSignInButton } from './google-sign-in-button';

/**
 * Component test for the "Sign in with Google" trigger (T-0026 frontend
 * wiring). `startGoogleOAuth` is mocked — this is a UI test, not an
 * integration test against the .NET backend. Mirrors the Apple button
 * test one-for-one.
 */

const startGoogleOAuth = vi.fn();
vi.mock('@/lib/api-client-helpers/auth', () => ({
  startGoogleOAuth: (...args: unknown[]) => startGoogleOAuth(...args),
}));

describe('GoogleSignInButton', () => {
  // jsdom doesn't implement real navigation, so a plain `href` assignment
  // logs a "Not implemented: navigation" error and is silently ignored —
  // stub `window.location` with a writable object for every test so the
  // component's redirect assignment is both silent and observable.
  const originalLocation = window.location;

  beforeEach(() => {
    startGoogleOAuth.mockReset();
    Object.defineProperty(window, 'location', {
      configurable: true,
      value: { ...originalLocation, href: '' },
    });
  });

  afterEach(() => {
    Object.defineProperty(window, 'location', { configurable: true, value: originalLocation });
  });

  it('renders with the Google sign-in label', () => {
    render(<GoogleSignInButton host="customer" onError={vi.fn()} />);
    expect(screen.getByRole('button', { name: 'Pokračovat přes Google' })).toBeInTheDocument();
  });

  it('calls startGoogleOAuth with the given host on click', async () => {
    startGoogleOAuth.mockResolvedValue({ success: true, value: { authorizationUrl: 'https://accounts.google.com/o/oauth2/v2/auth' } });
    render(<GoogleSignInButton host="maker" onError={vi.fn()} />);

    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: 'Pokračovat přes Google' }));
    });

    await vi.waitFor(() => expect(startGoogleOAuth).toHaveBeenCalledWith('maker'));
  });

  it('redirects the browser to the returned authorization URL on success', async () => {
    startGoogleOAuth.mockResolvedValue({
      success: true,
      value: { authorizationUrl: 'https://accounts.google.com/o/oauth2/v2/auth?state=abc' },
    });

    render(<GoogleSignInButton host="customer" onError={vi.fn()} />);

    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: 'Pokračovat přes Google' }));
    });

    await vi.waitFor(() => expect(window.location.href).toBe('https://accounts.google.com/o/oauth2/v2/auth?state=abc'));
  });

  it.each([
    ['auth.oauthNotAllowedForAdmin', 'Administrátorský účet se nepřihlašuje přes Apple ani Google — použijte e-mail a heslo.'],
    ['auth.oauthInvalidState', 'Platnost přihlašovacího požadavku vypršela. Zkuste to prosím znovu.'],
    ['auth.oauthEmailNotVerified', 'E-mail u zvoleného účtu není ověřený, přihlášení nelze dokončit.'],
    ['auth.oauthExchangeFailed', 'Přihlášení se nepodařilo dokončit. Zkuste to prosím znovu.'],
  ])('maps the %s error code to its i18n message', async (code, expectedMessage) => {
    startGoogleOAuth.mockResolvedValue({ success: false, error: { code, message: 'x', type: 'Validation' } });
    const onError = vi.fn();
    render(<GoogleSignInButton host="customer" onError={onError} />);

    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: 'Pokračovat přes Google' }));
    });

    await vi.waitFor(() => expect(onError).toHaveBeenCalledWith(expectedMessage));
  });

  it('falls back to the generic start-failed message for an unmapped error code', async () => {
    startGoogleOAuth.mockResolvedValue({
      success: false,
      error: { code: 'auth.somethingUnexpected', message: 'x', type: 'Validation' },
    });
    const onError = vi.fn();
    render(<GoogleSignInButton host="customer" onError={onError} />);

    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: 'Pokračovat přes Google' }));
    });

    await vi.waitFor(() =>
      expect(onError).toHaveBeenCalledWith('Přihlášení přes Google se nepodařilo spustit. Zkuste to prosím znovu.'),
    );
  });
});
