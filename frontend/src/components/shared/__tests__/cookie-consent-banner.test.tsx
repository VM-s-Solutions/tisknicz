import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { hasConsent } from '@/lib/consent/consent';
import { readStoredConsent } from '@/lib/consent/storage';
import { axeAA } from '@/lib/testing/axe';
import { CookieConsentBanner } from '../cookie-consent-banner';
import { CookieSettingsLink } from '../cookie-settings-link';

/**
 * Behavioral tests for the cookie consent banner (T-0147). Covers
 * AC-1 (first-visit render), AC-2/AC-3/AC-4 (the three consent
 * actions), and AC-6 (the settings-link reopen/edit path).
 *
 * The banner gates its first paint on a `useSyncExternalStore`-based
 * "has the client mounted" check (see `useHasMounted` in the
 * component) so it never flashes for returning visitors; in jsdom
 * that resolves synchronously, so no `act`/`waitFor` is needed to see
 * the first render, but we still `await` a microtask via
 * `findByRole` where helpful.
 */
describe('CookieConsentBanner', () => {
  afterEach(() => {
    document.cookie = 'makables_cookie_consent=; path=/; max-age=0';
  });

  it('AC-1: renders the first-visit summary view with the three actions', async () => {
    render(<CookieConsentBanner />);

    expect(await screen.findByRole('dialog')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Přijmout vše' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Pouze nezbytné' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Nastavit předvolby' })).toBeInTheDocument();
  });

  it('AC-8: the summary view has no axe WCAG 2.1 AA violations', async () => {
    const { container } = render(<CookieConsentBanner />);
    await screen.findByRole('dialog');
    expect(await axeAA(container)).toHaveNoViolations();
  });

  it('AC-8: the customize view has no axe WCAG 2.1 AA violations', async () => {
    const { container } = render(<CookieConsentBanner />);
    await screen.findByRole('dialog');
    fireEvent.click(screen.getByRole('button', { name: 'Nastavit předvolby' }));
    await screen.findByText('Nastavení souhlasu s cookies');
    expect(await axeAA(container)).toHaveNoViolations();
  });

  it('AC-2: "Přijmout vše" persists all categories as accepted and closes the banner', async () => {
    render(<CookieConsentBanner />);
    await screen.findByRole('dialog');

    fireEvent.click(screen.getByRole('button', { name: 'Přijmout vše' }));

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(readStoredConsent()?.choices).toEqual({ analytics: true, marketing: true });
    expect(hasConsent('analytics')).toBe(true);
    expect(hasConsent('marketing')).toBe(true);
  });

  it('AC-3: "Pouze nezbytné" persists only necessary (analytics/marketing declined)', async () => {
    render(<CookieConsentBanner />);
    await screen.findByRole('dialog');

    fireEvent.click(screen.getByRole('button', { name: 'Pouze nezbytné' }));

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(readStoredConsent()?.choices).toEqual({ analytics: false, marketing: false });
    expect(hasConsent('analytics')).toBe(false);
    expect(hasConsent('marketing')).toBe(false);
  });

  it('AC-4: customize view — toggling only analytics on and saving persists that exact combination', async () => {
    render(<CookieConsentBanner />);
    await screen.findByRole('dialog');

    fireEvent.click(screen.getByRole('button', { name: 'Nastavit předvolby' }));
    expect(await screen.findByText('Nastavení souhlasu s cookies')).toBeInTheDocument();

    const [analyticsToggle, marketingToggle] = screen.getAllByRole('switch');
    fireEvent.click(analyticsToggle);

    fireEvent.click(screen.getByRole('button', { name: 'Uložit nastavení' }));

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(readStoredConsent()?.choices).toEqual({ analytics: true, marketing: false });
    expect(marketingToggle).not.toBeChecked();
  });

  it('AC-6: the "cookie settings" link reopens the customize view pre-filled with the current choice', async () => {
    render(
      <>
        <CookieSettingsLink />
        <CookieConsentBanner />
      </>,
    );

    // Make an initial choice (analytics on, marketing off) and dismiss.
    await screen.findByRole('dialog');
    fireEvent.click(screen.getByRole('button', { name: 'Nastavit předvolby' }));
    const [analyticsToggle] = screen.getAllByRole('switch');
    fireEvent.click(analyticsToggle);
    fireEvent.click(screen.getByRole('button', { name: 'Uložit nastavení' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());

    // Reopen via the settings link.
    fireEvent.click(screen.getByRole('button', { name: 'Nastavení cookies' }));

    const dialog = await screen.findByRole('dialog');
    expect(dialog).toBeInTheDocument();
    const [reopenedAnalytics, reopenedMarketing] = screen.getAllByRole('switch');
    expect(reopenedAnalytics).toBeChecked();
    expect(reopenedMarketing).not.toBeChecked();

    // Change to marketing-on too, and confirm the update is reflected
    // immediately by hasConsent() on the very next call (AC-6).
    fireEvent.click(reopenedMarketing);
    fireEvent.click(screen.getByRole('button', { name: 'Uložit nastavení' }));

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(hasConsent('analytics')).toBe(true);
    expect(hasConsent('marketing')).toBe(true);
  });

  it('does not render the banner on a subsequent visit once a choice was already made', async () => {
    const { unmount } = render(<CookieConsentBanner />);
    await screen.findByRole('dialog');
    fireEvent.click(screen.getByRole('button', { name: 'Přijmout vše' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    unmount();

    render(<CookieConsentBanner />);
    // Give the mount-detection microtask a chance to flush, then
    // assert the dialog never appears.
    await act(async () => {
      await Promise.resolve();
    });
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });
});
