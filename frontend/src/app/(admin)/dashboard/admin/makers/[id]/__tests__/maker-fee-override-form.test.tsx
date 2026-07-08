import { fireEvent, render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MakerFeeOverrideForm } from '../maker-fee-override-form';

/**
 * Component test for the maker fee-rate override form (T-0140,
 * US-admin-0018). Covers the three basic interactions: the current
 * country-default rate is displayed, a "set override" submit posts the
 * converted basis points, and a "clear override" submit posts `null`.
 * `setMakerFeeOverride` is mocked — this is a UI test, not an integration
 * test against the .NET backend.
 */

const refresh = vi.fn();
vi.mock('next/navigation', () => ({
  useRouter: () => ({ push: vi.fn(), refresh }),
}));

const setMakerFeeOverride = vi.fn();
vi.mock('@/lib/api-client-helpers/admin-ops-client', () => ({
  setMakerFeeOverride: (...args: unknown[]) => setMakerFeeOverride(...args),
}));

describe('MakerFeeOverrideForm', () => {
  beforeEach(() => {
    setMakerFeeOverride.mockReset();
    refresh.mockReset();
  });

  it('renders the country-default rate as a percentage', () => {
    render(<MakerFeeOverrideForm makerId="maker-1" countryCode="CZ" countryDefaultBp={700} />);
    expect(screen.getByText('7 %')).toBeInTheDocument();
  });

  it('shows the unavailable copy when the country default failed to load', () => {
    render(<MakerFeeOverrideForm makerId="maker-1" countryCode="CZ" countryDefaultBp={undefined} />);
    expect(screen.getByText('Výchozí provizi země se nepodařilo načíst.')).toBeInTheDocument();
  });

  it('submits a set-override request with the converted basis points and reason', async () => {
    setMakerFeeOverride.mockResolvedValue({ success: true, value: undefined });
    render(<MakerFeeOverrideForm makerId="maker-1" countryCode="CZ" countryDefaultBp={700} />);

    fireEvent.change(screen.getByLabelText('Sazba provize (%)'), { target: { value: '3,5' } });
    fireEvent.change(screen.getByLabelText('Důvod'), { target: { value: 'Věrný výrobce 2 roky' } });

    const submit = screen.getByRole('button', { name: 'Uložit override' });
    expect(submit).not.toBeDisabled();
    fireEvent.click(submit);

    await screen.findByText('Individuální sazba provize byla nastavena na 3,5 %.');
    expect(setMakerFeeOverride).toHaveBeenCalledWith('maker-1', {
      feeRateOverrideBp: 350,
      reason: 'Věrný výrobce 2 roky',
    });
  });

  it('disables the submit button when the entered rate exceeds the country default', () => {
    render(<MakerFeeOverrideForm makerId="maker-1" countryCode="CZ" countryDefaultBp={700} />);

    fireEvent.change(screen.getByLabelText('Sazba provize (%)'), { target: { value: '9' } });
    fireEvent.change(screen.getByLabelText('Důvod'), { target: { value: 'Testovací důvod' } });

    expect(screen.getByRole('button', { name: 'Uložit override' })).toBeDisabled();
    expect(
      screen.getByText('Zadaná sazba přesahuje výchozí provizi země — override může být pouze slevou.'),
    ).toBeInTheDocument();
    expect(setMakerFeeOverride).not.toHaveBeenCalled();
  });

  it('submits a clear-override request with a null rate', async () => {
    setMakerFeeOverride.mockResolvedValue({ success: true, value: undefined });
    render(<MakerFeeOverrideForm makerId="maker-1" countryCode="CZ" countryDefaultBp={700} />);

    fireEvent.change(screen.getByLabelText('Akce'), { target: { value: 'clear' } });
    fireEvent.change(screen.getByLabelText('Důvod'), { target: { value: 'Konec spolupráce' } });

    const submit = screen.getByRole('button', { name: 'Zrušit override' });
    expect(submit).not.toBeDisabled();
    fireEvent.click(submit);

    await screen.findByText('Individuální sazba provize byla zrušena — platí výchozí sazba země.');
    expect(setMakerFeeOverride).toHaveBeenCalledWith('maker-1', {
      feeRateOverrideBp: null,
      reason: 'Konec spolupráce',
    });
  });

  it('surfaces the backend error message on a failed submit', async () => {
    setMakerFeeOverride.mockResolvedValue({
      success: false,
      error: { code: 'maker.feeOverrideExceedsCountryDefault', message: 'x', type: 'Validation' },
    });
    render(<MakerFeeOverrideForm makerId="maker-1" countryCode="CZ" countryDefaultBp={undefined} />);

    // No known country default client-side → the ceiling check is skipped
    // and the submit reaches the backend, which is authoritative (AC-5).
    fireEvent.change(screen.getByLabelText('Sazba provize (%)'), { target: { value: '9' } });
    fireEvent.change(screen.getByLabelText('Důvod'), { target: { value: 'Testovací důvod' } });
    fireEvent.click(screen.getByRole('button', { name: 'Uložit override' }));

    await screen.findByText(
      'Zadaná sazba přesahuje výchozí provizi pro danou zemi — override může být pouze slevou.',
    );
  });
});
