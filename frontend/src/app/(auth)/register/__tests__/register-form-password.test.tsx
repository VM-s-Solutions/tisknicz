import { fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { RegisterForm } from '../register-form';

/**
 * Password confirmation + reveal on customer registration. The form is
 * `noValidate`, so nothing but the submit handler stops a mismatched or
 * empty confirm — both paths are pinned here.
 */

const registerCustomer = vi.fn();

vi.mock('@/lib/api-client-helpers/auth', () => ({
  registerCustomer: (...args: unknown[]) => registerCustomer(...args),
  lookupCompanyPreview: vi.fn(),
  startGoogleOAuth: vi.fn(async () => ({
    success: false,
    error: { code: 'auth.oauthExchangeFailed', message: 'n/a', type: 'Transient' },
  })),
}));

function fill(password: string, confirm: string | null): void {
  fireEvent.change(screen.getByLabelText('Jméno a příjmení'), {
    target: { value: 'Anna Nováková' },
  });
  fireEvent.change(screen.getByLabelText('E-mail'), { target: { value: 'anna@example.cz' } });
  fireEvent.change(screen.getByLabelText('Heslo'), { target: { value: password } });
  if (confirm !== null) {
    fireEvent.change(screen.getByLabelText('Heslo znovu'), { target: { value: confirm } });
  }
}

describe('RegisterForm — password confirm + reveal', () => {
  beforeEach(() => {
    registerCustomer.mockResolvedValue({ success: true, value: { userId: 'u1' } });
  });

  afterEach(() => vi.clearAllMocks());

  it('renders a separate confirm field, both masked', () => {
    render(<RegisterForm />);

    expect(screen.getByLabelText('Heslo')).toHaveAttribute('type', 'password');
    expect(screen.getByLabelText('Heslo znovu')).toHaveAttribute('type', 'password');
  });

  it('reveals only the field whose eye was clicked', () => {
    render(<RegisterForm />);

    fireEvent.click(screen.getAllByRole('button', { name: 'Zobrazit heslo' })[0]);

    expect(screen.getByLabelText('Heslo')).toHaveAttribute('type', 'text');
    expect(screen.getByLabelText('Heslo znovu')).toHaveAttribute('type', 'password');
  });

  it('shows the mismatch error inline as soon as the confirm diverges', () => {
    render(<RegisterForm />);

    fill('abcd1234567', 'abcd123456X');

    expect(screen.getByText('Hesla se neshodují.')).toBeInTheDocument();
  });

  it('blocks submit on a mismatch — no request is sent', () => {
    render(<RegisterForm />);

    fill('abcd1234567', 'abcd123456X');
    fireEvent.click(screen.getByRole('button', { name: 'Vytvořit účet' }));

    expect(registerCustomer).not.toHaveBeenCalled();
  });

  it('blocks submit when the confirm is left empty', () => {
    render(<RegisterForm />);

    fill('abcd1234567', null);
    fireEvent.click(screen.getByRole('button', { name: 'Vytvořit účet' }));

    expect(registerCustomer).not.toHaveBeenCalled();
    expect(screen.getByText('Hesla se neshodují.')).toBeInTheDocument();
  });

  it('submits once both fields agree, sending the password only once', async () => {
    render(<RegisterForm />);

    fill('abcd1234567', 'abcd1234567');
    expect(screen.queryByText('Hesla se neshodují.')).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Vytvořit účet' }));

    await screen.findByText('Účet vytvořen');
    const payload = registerCustomer.mock.calls[0][1] as Record<string, unknown>;
    expect(payload.password).toBe('abcd1234567');
    expect('passwordConfirm' in payload).toBe(false);
  });
});
