import { fireEvent, render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { CancelOrderClient } from '../[id]/cancel-order-client';
import { cancelPendingOrder } from '@/lib/api-client-helpers/orders-client';

/**
 * T-0181 / Q-0041 (audit CUST-M3): the customer's only exit from an
 * abandoned unpaid order used to be the silent 24 h auto-expiry.
 */

const refresh = vi.fn();
const push = vi.fn();

vi.mock('next/navigation', () => ({
  useRouter: () => ({ refresh, push }),
}));

vi.mock('@/lib/api-client-helpers/orders-client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/lib/api-client-helpers/orders-client')>();
  return { ...actual, cancelPendingOrder: vi.fn() };
});

const cancelMock = vi.mocked(cancelPendingOrder);
type CancelResult = Awaited<ReturnType<typeof cancelPendingOrder>>;

beforeEach(() => {
  vi.clearAllMocks();
});

describe('CancelOrderClient', () => {
  it('is confirm-gated — opening the dialog sends no request', () => {
    render(<CancelOrderClient orderId="o-1" />);

    fireEvent.click(screen.getByRole('button', { name: 'Zrušit objednávku' }));

    expect(screen.getByText('Zrušit objednávku?')).toBeInTheDocument();
    expect(cancelMock).not.toHaveBeenCalled();
  });

  it('cancels and refreshes on confirm', async () => {
    cancelMock.mockResolvedValue({
      success: true,
      value: { orderId: 'o-1', state: 'Cancelled' },
    } as CancelResult);
    render(<CancelOrderClient orderId="o-1" />);

    fireEvent.click(screen.getByRole('button', { name: 'Zrušit objednávku' }));
    fireEvent.click(screen.getByRole('button', { name: 'Ano, zrušit' }));

    await vi.waitFor(() => {
      expect(cancelMock).toHaveBeenCalledWith('o-1');
    });
    expect(refresh).toHaveBeenCalled();
  });

  it('keeps the dialog open and shows why when the backend refuses', async () => {
    cancelMock.mockResolvedValue({
      success: false,
      error: {
        code: 'order.invalidTransition',
        message: 'Objednávku v tomto stavu nelze zrušit.',
        type: 'Conflict',
      },
    } as CancelResult);
    render(<CancelOrderClient orderId="o-1" />);

    fireEvent.click(screen.getByRole('button', { name: 'Zrušit objednávku' }));
    fireEvent.click(screen.getByRole('button', { name: 'Ano, zrušit' }));

    // resolveErrorMessage maps the CODE to canonical Czech copy — the raw
    // backend message is deliberately not what reaches the user.
    expect(
      await screen.findByText('Tuto akci nelze v aktuálním stavu objednávky provést.'),
    ).toBeInTheDocument();
    expect(refresh).not.toHaveBeenCalled();
  });
});
