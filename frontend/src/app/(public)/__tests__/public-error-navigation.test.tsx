import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { axeAA } from '@/lib/testing/axe';
import PublicError from '../error';
import RootError from '../../error';

/**
 * T-0171 (audit PUB-M1): the public surface — the only one anonymous
 * visitors see — was the ONE route group with no error boundary, so an
 * unhandled render throw fell through to Next's default English screen
 * with no navigation and no way back.
 */
describe('public error boundaries', () => {
  const error = Object.assign(new Error('boom'), { digest: 'abc' });

  it('renders Czech copy, a working retry and a way onward', () => {
    const reset = vi.fn();
    render(<PublicError error={error} reset={reset} />);

    expect(screen.getByText('Něco se pokazilo')).toBeInTheDocument();
    const retry = screen.getByRole('button', { name: /Zkusit znovu/ });
    retry.click();
    expect(reset).toHaveBeenCalledTimes(1);
    // Navigation onward, so the boundary is not itself a dead end.
    expect(screen.getByRole('link', { name: /katalog/i })).toBeInTheDocument();
  });

  it('root boundary stands alone without the public chrome', () => {
    const reset = vi.fn();
    render(<RootError error={error} reset={reset} />);

    expect(screen.getByText('Něco se pokazilo')).toBeInTheDocument();
    screen.getByRole('button', { name: /Zkusit znovu/ }).click();
    expect(reset).toHaveBeenCalledTimes(1);
  });

  it('has no axe violations', async () => {
    const { container } = render(<PublicError error={error} reset={() => {}} />);
    expect(await axeAA(container)).toHaveNoViolations();
  });
});
