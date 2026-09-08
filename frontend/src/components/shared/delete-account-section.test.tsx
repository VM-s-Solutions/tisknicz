import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { DeleteAccountSection } from './delete-account-section';
import { t } from '@/lib/i18n';

/**
 * Pins the DISCLOSURE, not the markup.
 *
 * Makables deletes accounts in two tiers by design (ADR 0013): the
 * self-service button deactivates, and GDPR erasure is a separate admin-only
 * command run on request. That is defensible only while the UI actually says
 * so — otherwise "Smazat účet" reads as a promise the system does not keep.
 * These tests are the regression guard on that wording, so a future copy edit
 * cannot quietly turn the two-tier design back into a misleading one.
 */

vi.mock('next/navigation', () => ({
  useRouter: () => ({ push: vi.fn(), refresh: vi.fn() }),
}));

vi.mock('@/lib/api-client-helpers/profile', () => ({
  deleteMyAccount: vi.fn(),
}));

describe('DeleteAccountSection — GDPR disclosure', () => {
  it('states that deactivation is not erasure', () => {
    render(<DeleteAccountSection host="customer" />);

    const description = t('profile.delete_account.description');
    expect(screen.getByText(description, { exact: false })).toBeInTheDocument();
    // The load-bearing clause: without it the section implies the data is gone.
    expect(description).toContain('nejsou vymazány');
    expect(description).toContain('deaktivován');
  });

  it('surfaces a real route to request full erasure', () => {
    render(<DeleteAccountSection host="customer" />);

    const email = t('static.contact.operator_email_value');
    // A route the user can actually act on — before this, the capability
    // existed (admin-only) but nothing told the user how to reach it.
    expect(email).toMatch(/^[^@\s]+@[^@\s]+\.[^@\s]+$/);
    expect(screen.getByText(new RegExp(email.replace('.', '\\.')))).toBeInTheDocument();
  });

  it('tells the user some records are retained regardless', () => {
    render(<DeleteAccountSection host="customer" />);

    const note = t('profile.delete_account.erasure_note', {
      email: t('static.contact.operator_email_value'),
    });
    expect(note).toContain('faktury');
    expect(screen.getByText(note, { exact: false })).toBeInTheDocument();
  });

  it('shows the maker-specific catalog note only on the maker host', () => {
    const { rerender } = render(<DeleteAccountSection host="customer" />);
    const makerNote = t('profile.delete_account.maker_note');
    expect(screen.queryByText(makerNote, { exact: false })).not.toBeInTheDocument();

    rerender(<DeleteAccountSection host="maker" />);
    expect(screen.getByText(makerNote, { exact: false })).toBeInTheDocument();
  });
});
