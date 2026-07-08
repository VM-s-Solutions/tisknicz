import { act, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AppleSignInButton } from './apple-sign-in-button';

/**
 * Component test for the "Sign in with Apple" trigger (T-0139, AC-10).
 * `startAppleOAuth` is mocked — this is a UI test, not an integration test
 * against the .NET backend.
 */

const startAppleOAuth = vi.fn();
vi.mock('@/lib/api-client-helpers/auth', () => ({
  startAppleOAuth: (...args: unknown[]) => startAppleOAuth(...args),
}));

describe('AppleSignInButton', () => {
  // jsdom doesn't implement real navigation, so a plain `href` assignment
  // logs a "Not implemented: navigation" error and is silently ignored —
  // stub `window.location` with a writable object for every test so the
  // component's redirect assignment is both silent and observable.
  const originalLocation = window.location;

  beforeEach(() => {
    startAppleOAuth.mockReset();
    Object.defineProperty(window, 'location', {
      configurable: true,
      value: { ...originalLocation, href: '' },
    });
  });

  afterEach(() => {
    Object.defineProperty(window, 'location', { configurable: true, value: originalLocation });
  });

  it('renders with the Apple sign-in label', () => {
    render(<AppleSignInButton host="customer" onError={vi.fn()} />);
    expect(screen.getByRole('button', { name: 'Pokračovat přes Apple' })).toBeInTheDocument();
  });

  it('calls startAppleOAuth with the given host on click', async () => {
    startAppleOAuth.mockResolvedValue({ success: true, value: { authorizationUrl: 'https://appleid.apple.com/auth' } });
    render(<AppleSignInButton host="maker" onError={vi.fn()} />);

    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: 'Pokračovat přes Apple' }));
    });

    await vi.waitFor(() => expect(startAppleOAuth).toHaveBeenCalledWith('maker'));
  });

  it('redirects the browser to the returned authorization URL on success', async () => {
    startAppleOAuth.mockResolvedValue({
      success: true,
      value: { authorizationUrl: 'https://appleid.apple.com/auth?state=abc' },
    });

    render(<AppleSignInButton host="customer" onError={vi.fn()} />);

    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: 'Pokračovat přes Apple' }));
    });

    await vi.waitFor(() => expect(window.location.href).toBe('https://appleid.apple.com/auth?state=abc'));
  });

  it.each([
    ['auth.oauthNotAllowedForAdmin', 'Administrátorský účet se nepřihlašuje přes Apple ani Google — použijte e-mail a heslo.'],
    ['auth.oauthInvalidState', 'Platnost přihlašovacího požadavku vypršela. Zkuste to prosím znovu.'],
    ['auth.oauthEmailNotVerified', 'E-mail u zvoleného účtu není ověřený, přihlášení nelze dokončit.'],
    ['auth.oauthExchangeFailed', 'Přihlášení se nepodařilo dokončit. Zkuste to prosím znovu.'],
  ])('maps the %s error code to its i18n message', async (code, expectedMessage) => {
    startAppleOAuth.mockResolvedValue({ success: false, error: { code, message: 'x', type: 'Validation' } });
    const onError = vi.fn();
    render(<AppleSignInButton host="customer" onError={onError} />);

    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: 'Pokračovat přes Apple' }));
    });

    await vi.waitFor(() => expect(onError).toHaveBeenCalledWith(expectedMessage));
  });

  it('falls back to the generic start-failed message for an unmapped error code', async () => {
    startAppleOAuth.mockResolvedValue({
      success: false,
      error: { code: 'auth.somethingUnexpected', message: 'x', type: 'Validation' },
    });
    const onError = vi.fn();
    render(<AppleSignInButton host="customer" onError={onError} />);

    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: 'Pokračovat přes Apple' }));
    });

    await vi.waitFor(() =>
      expect(onError).toHaveBeenCalledWith('Přihlášení přes Apple se nepodařilo spustit. Zkuste to prosím znovu.'),
    );
  });
});
