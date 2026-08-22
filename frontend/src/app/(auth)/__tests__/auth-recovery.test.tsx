import { fireEvent, render, screen } from '@testing-library/react';
import { StrictMode } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ResendConfirmationForm } from '@/components/shared/resend-confirmation-form';
import {
  confirmEmail,
  consumeMagicLink,
  resendConfirmation,
} from '@/lib/api-client-helpers/auth';
import { MagicClient } from '../magic/magic-client';
import { VerifyClient } from '../verify/verify-client';

/**
 * auth-recovery bundle (T-0167 + T-0168): one-time tokens survive dev
 * StrictMode double-mount, makers' magic links complete via the maker
 * host, and every dead end grew a recovery affordance.
 */

const replace = vi.fn();
const refresh = vi.fn();
let currentSearch = '';

vi.mock('next/navigation', () => ({
  useRouter: () => ({ replace, refresh }),
  useSearchParams: () => new URLSearchParams(currentSearch),
}));

vi.mock('@/lib/api-client-helpers/auth', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/lib/api-client-helpers/auth')>();
  return {
    ...actual,
    confirmEmail: vi.fn(),
    consumeMagicLink: vi.fn(),
    requestMagicLink: vi.fn(),
    resendConfirmation: vi.fn(),
  };
});

const confirmEmailMock = vi.mocked(confirmEmail);
const consumeMagicLinkMock = vi.mocked(consumeMagicLink);
const resendConfirmationMock = vi.mocked(resendConfirmation);

type AuthResult = Awaited<ReturnType<typeof confirmEmail>>;

const okResult = { success: true, value: undefined } as AuthResult;
const forbidden = {
  success: false,
  error: { code: 'auth.forbidden', message: '', type: 'Forbidden' },
} as AuthResult;
const invalid = {
  success: false,
  error: { code: 'auth.magicLinkInvalid', message: '', type: 'Validation' },
} as AuthResult;

beforeEach(() => {
  vi.clearAllMocks();
  currentSearch = 'token=tok-1';
});

describe('VerifyClient (T-0168, AUTH-M1)', () => {
  it('fires the one-time confirm exactly once under StrictMode double-mount', async () => {
    confirmEmailMock.mockResolvedValue(okResult);
    render(
      <StrictMode>
        <VerifyClient />
      </StrictMode>,
    );

    expect(await screen.findByText('E-mail potvrzen')).toBeInTheDocument();
    expect(confirmEmailMock).toHaveBeenCalledTimes(1);
  });

  it('offers login and resend on failure instead of dead-ending', async () => {
    confirmEmailMock.mockResolvedValue(invalid);
    render(<VerifyClient />);

    expect(await screen.findByRole('link', { name: 'Přihlásit se' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Poslat potvrzovací e-mail znovu' })).toBeInTheDocument();
  });
});

describe('MagicClient consume (T-0168, AUTH-H3)', () => {
  it("retries the maker host on auth.forbidden and lands on the maker dashboard", async () => {
    consumeMagicLinkMock.mockImplementation(async (host) =>
      host === 'customer' ? forbidden : okResult,
    );
    render(<MagicClient />);

    await vi.waitFor(() => {
      expect(replace).toHaveBeenCalledWith('/dashboard/maker/objednavky');
    });
    expect(consumeMagicLinkMock).toHaveBeenCalledTimes(2);
    expect(refresh).toHaveBeenCalled();
  });

  it('shows owned copy + recovery links when both hosts reject', async () => {
    consumeMagicLinkMock.mockResolvedValue(invalid);
    render(<MagicClient />);

    expect(
      await screen.findByText('Odkaz je neplatný nebo už vypršel.'),
    ).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Poslat nový odkaz' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Přihlásit se' })).toBeInTheDocument();
  });
});

describe('ResendConfirmationForm (T-0168, AUTH-M2)', () => {
  it('shows the uniform sent copy on success', async () => {
    resendConfirmationMock.mockResolvedValue(okResult);
    render(<ResendConfirmationForm defaultEmail="anna@example.cz" compact />);

    fireEvent.submit(document.querySelector('form') as HTMLFormElement);

    expect(await screen.findByText(/nový potvrzovací e-mail je na cestě/i)).toBeInTheDocument();
  });

  it('surfaces a failed resend instead of staying silent', async () => {
    resendConfirmationMock.mockResolvedValue({
      success: false,
      error: { code: 'network.timeout', message: 'Server neodpověděl včas. Zkuste to prosím znovu.', type: 'Transient' },
    } as AuthResult);
    render(<ResendConfirmationForm defaultEmail="anna@example.cz" compact />);

    fireEvent.submit(document.querySelector('form') as HTMLFormElement);

    expect(await screen.findByText(/Server neodpověděl včas/)).toBeInTheDocument();
  });
});
