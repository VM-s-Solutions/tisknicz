import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { ProfileLoadError } from '../profile-load-error';
import type { ApiError } from '@/lib/runtime/result';

// RefreshButton calls useRouter(); the app router isn't mounted in jsdom.
vi.mock('next/navigation', () => ({
  useRouter: () => ({ refresh: vi.fn() }),
}));

/**
 * T-0173 (audit CUST-M1): both profile pages rendered
 * `result.error.message` verbatim — against the project's own rule that
 * a raw backend message never reaches the UI — with no retry. The
 * Unauthorized case redirects at the page level; everything else lands
 * here with translated copy.
 */
describe('ProfileLoadError', () => {
  it('renders translated copy and a retry, never the raw backend message', () => {
    const error: ApiError = {
      code: 'network.timeout',
      message: 'RAW BACKEND STRING that must not reach the UI',
      type: 'Transient',
    };

    render(<ProfileLoadError error={error} />);

    expect(screen.getByText('Profil se nepodařilo načíst')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Zkusit znovu/ })).toBeInTheDocument();
    expect(
      screen.queryByText('RAW BACKEND STRING that must not reach the UI'),
    ).not.toBeInTheDocument();
  });
});
