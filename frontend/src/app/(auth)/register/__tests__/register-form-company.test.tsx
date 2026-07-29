import { act, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { RegisterForm } from '../register-form';

/**
 * Behavioral tests for the T-0162 "Jsem firma" block on the customer
 * registration form: checkbox-gated IČO input, local mod-11 gate, the
 * debounced ARES preview (name + DIČ / neplátce), and the submit payload
 * shape (the field is absent entirely for private persons — AC-1).
 *
 * IČO fixtures mirror czech-ico.test.ts: 27074358 is valid (Avast),
 * 12345678 fails the mod-11 checksum.
 */

const registerCustomer = vi.fn();
const lookupCompanyPreview = vi.fn();

vi.mock('@/lib/api-client-helpers/auth', () => ({
  registerCustomer: (...args: unknown[]) => registerCustomer(...args),
  lookupCompanyPreview: (...args: unknown[]) => lookupCompanyPreview(...args),
  startGoogleOAuth: vi.fn(async () => ({
    success: false,
    error: { code: 'auth.oauthExchangeFailed', message: 'n/a', type: 'Transient' },
  })),
}));

function fillBaseFields(): void {
  fireEvent.change(screen.getByLabelText('Jméno a příjmení'), {
    target: { value: 'Anna Nováková' },
  });
  fireEvent.change(screen.getByLabelText('E-mail'), {
    target: { value: 'anna@example.cz' },
  });
  fireEvent.change(screen.getByLabelText('Heslo'), {
    target: { value: 'abcd1234567' },
  });
}

const preview = {
  registrationNumber: '27074358',
  companyName: 'Avast Software s.r.o.',
  legalForm: 'Společnost s ručením omezeným',
  vatId: 'CZ27074358',
  street: 'Pikrtova',
  houseNumber: '1737',
  city: 'Praha',
  zip: '14000',
  isActiveInRegistry: true,
  isStale: false,
};

describe('RegisterForm — Jsem firma (T-0162)', () => {
  beforeEach(() => {
    registerCustomer.mockResolvedValue({ success: true, value: { userId: 'u1' } });
    lookupCompanyPreview.mockResolvedValue({ success: true, value: preview });
  });

  afterEach(() => {
    vi.clearAllMocks();
    vi.useRealTimers();
  });

  it('AC-1: unchecked by default — no IČO input, payload has no companyRegistrationNumber', async () => {
    render(<RegisterForm />);

    expect(screen.queryByLabelText('IČO')).not.toBeInTheDocument();

    fillBaseFields();
    fireEvent.click(screen.getByRole('button', { name: 'Vytvořit účet' }));

    await screen.findByText('Účet vytvořen');
    expect(registerCustomer).toHaveBeenCalledTimes(1);
    const payload = registerCustomer.mock.calls[0][1] as Record<string, unknown>;
    expect('companyRegistrationNumber' in payload).toBe(false);
    expect(lookupCompanyPreview).not.toHaveBeenCalled();
  });

  it('AC-2: valid IČO fires one debounced preview and renders name + DIČ', async () => {
    vi.useFakeTimers();
    render(<RegisterForm />);

    fireEvent.click(screen.getByLabelText('Jsem firma'));
    fireEvent.change(screen.getByLabelText('IČO'), { target: { value: '27074358' } });

    await act(async () => {
      vi.advanceTimersByTime(400);
    });
    vi.useRealTimers();

    expect(await screen.findByText('Avast Software s.r.o.')).toBeInTheDocument();
    expect(screen.getByText(/CZ27074358/)).toBeInTheDocument();
    expect(lookupCompanyPreview).toHaveBeenCalledTimes(1);
    expect(lookupCompanyPreview).toHaveBeenCalledWith('27074358');
  });

  it('AC-2: company without DIČ renders the neplátce note', async () => {
    lookupCompanyPreview.mockResolvedValue({
      success: true,
      value: { ...preview, vatId: null },
    });
    vi.useFakeTimers();
    render(<RegisterForm />);

    fireEvent.click(screen.getByLabelText('Jsem firma'));
    fireEvent.change(screen.getByLabelText('IČO'), { target: { value: '27074358' } });

    await act(async () => {
      vi.advanceTimersByTime(400);
    });
    vi.useRealTimers();

    expect(await screen.findByText(/Neplátce DPH/)).toBeInTheDocument();
  });

  it('AC-4: checksum-invalid IČO shows inline error and blocks submit locally', async () => {
    render(<RegisterForm />);

    fireEvent.click(screen.getByLabelText('Jsem firma'));
    fillBaseFields();
    fireEvent.change(screen.getByLabelText('IČO'), { target: { value: '12345678' } });

    expect(
      (await screen.findAllByText('Toto není platné české IČO — zkontrolujte prosím překlepy.'))
        .length,
    ).toBeGreaterThan(0);

    fireEvent.click(screen.getByRole('button', { name: 'Vytvořit účet' }));
    expect(registerCustomer).not.toHaveBeenCalled();
    expect(lookupCompanyPreview).not.toHaveBeenCalled();
  });

  it('AC-3: checked + valid IČO submits companyRegistrationNumber', async () => {
    vi.useFakeTimers();
    render(<RegisterForm />);

    fireEvent.click(screen.getByLabelText('Jsem firma'));
    fillBaseFields();
    fireEvent.change(screen.getByLabelText('IČO'), { target: { value: '27074358' } });
    await act(async () => {
      vi.advanceTimersByTime(400);
    });
    vi.useRealTimers();

    fireEvent.click(screen.getByRole('button', { name: 'Vytvořit účet' }));

    await screen.findByText('Účet vytvořen');
    expect(registerCustomer).toHaveBeenCalledTimes(1);
    const payload = registerCustomer.mock.calls[0][1] as Record<string, unknown>;
    expect(payload.companyRegistrationNumber).toBe('27074358');
  });

  it('debounce collapses rapid retypes into a single preview call (QA fold F-1)', async () => {
    vi.useFakeTimers();
    render(<RegisterForm />);

    fireEvent.click(screen.getByLabelText('Jsem firma'));
    // First full valid IČO arms the timer; a second valid IČO typed 200 ms
    // later must CANCEL it (clearTimeout) — without the cancel the mock
    // would record two calls even though the seq guard hides the first
    // result. 00006947 = Ministerstvo financí (czech-ico.test.ts fixture).
    fireEvent.change(screen.getByLabelText('IČO'), { target: { value: '27074358' } });
    await act(async () => {
      vi.advanceTimersByTime(200);
    });
    fireEvent.change(screen.getByLabelText('IČO'), { target: { value: '00006947' } });
    await act(async () => {
      vi.advanceTimersByTime(400);
    });
    vi.useRealTimers();

    await screen.findByText('Avast Software s.r.o.');
    expect(lookupCompanyPreview).toHaveBeenCalledTimes(1);
    expect(lookupCompanyPreview).toHaveBeenCalledWith('00006947');
  });

  it('renders the dissolved alert when the preview reports an inactive company (QA fold F-3)', async () => {
    lookupCompanyPreview.mockResolvedValue({
      success: true,
      value: { ...preview, isActiveInRegistry: false },
    });
    vi.useFakeTimers();
    render(<RegisterForm />);

    fireEvent.click(screen.getByLabelText('Jsem firma'));
    fireEvent.change(screen.getByLabelText('IČO'), { target: { value: '27074358' } });
    await act(async () => {
      vi.advanceTimersByTime(400);
    });
    vi.useRealTimers();

    expect(
      await screen.findByText('Tato firma je v ARES vedena jako zaniklá — registrace nebude možná.'),
    ).toBeInTheDocument();
  });

  it('maps user.companyDissolved submit reject to its Czech copy (QA fold F-2)', async () => {
    registerCustomer.mockResolvedValue({
      success: false,
      error: { code: 'user.companyDissolved', message: 'raw-server-text', type: 'Permanent' },
    });
    render(<RegisterForm />);

    fireEvent.click(screen.getByLabelText('Jsem firma'));
    fillBaseFields();
    fireEvent.change(screen.getByLabelText('IČO'), { target: { value: '27074358' } });
    fireEvent.click(screen.getByRole('button', { name: 'Vytvořit účet' }));

    expect(
      await screen.findByText(
        'Tato firma je v ARES vedena jako zaniklá. Firemní účet na ni nelze založit.',
      ),
    ).toBeInTheDocument();
  });

  it('maps company.notFound submit reject to the invalid-IČO copy (QA fold F-2)', async () => {
    registerCustomer.mockResolvedValue({
      success: false,
      error: { code: 'company.notFound', message: 'raw-server-text', type: 'NotFound' },
    });
    render(<RegisterForm />);

    fireEvent.click(screen.getByLabelText('Jsem firma'));
    fillBaseFields();
    fireEvent.change(screen.getByLabelText('IČO'), { target: { value: '27074358' } });
    fireEvent.click(screen.getByRole('button', { name: 'Vytvořit účet' }));

    expect(
      await screen.findByText('IČO je neplatné nebo neexistuje v rejstříku ARES.'),
    ).toBeInTheDocument();
  });

  it('unchecking the box drops previously typed company state from the payload', async () => {
    vi.useFakeTimers();
    render(<RegisterForm />);

    const checkbox = screen.getByLabelText('Jsem firma');
    fireEvent.click(checkbox);
    fillBaseFields();
    fireEvent.change(screen.getByLabelText('IČO'), { target: { value: '27074358' } });
    await act(async () => {
      vi.advanceTimersByTime(400);
    });
    vi.useRealTimers();

    fireEvent.click(checkbox); // uncheck
    expect(screen.queryByLabelText('IČO')).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Vytvořit účet' }));
    await screen.findByText('Účet vytvořen');
    const payload = registerCustomer.mock.calls[0][1] as Record<string, unknown>;
    expect('companyRegistrationNumber' in payload).toBe(false);
  });
});
