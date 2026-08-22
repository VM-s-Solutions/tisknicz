import { fireEvent, render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { OrderFormClient } from '../order-form-client';

/**
 * T-0172 (audit CUST-H3/H4/L1): a failed submit must land feedback
 * in-viewport (focus the first errored field), field errors must clear
 * as the user fixes them, and the profile's name/phone arrive as
 * editable defaults instead of being retyped every order.
 */

const push = vi.fn();
const refresh = vi.fn();

vi.mock('next/navigation', () => ({
  useRouter: () => ({ push, refresh }),
}));

vi.mock('@/lib/api-client-helpers/orders-client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/lib/api-client-helpers/orders-client')>();
  return { ...actual, createOrder: vi.fn(), uploadOrderAttachment: vi.fn() };
});

function renderForm() {
  return render(
    <OrderFormClient
      productId="p1"
      defaultName="Anna Nováková"
      defaultEmail="anna@example.cz"
      defaultPhone="+420601602603"
      personalPickupEnabled
      pickupNote={null}
      pickupCity="Brno"
      widgetConfig={null}
      fulfillmentType="MadeToOrder"
    />,
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  window.HTMLElement.prototype.scrollIntoView = vi.fn();
});

describe('OrderFormClient usability (T-0172)', () => {
  it('prefills name, email and phone from the profile as editable defaults', () => {
    renderForm();

    expect(screen.getByDisplayValue('Anna Nováková')).toBeInTheDocument();
    expect(screen.getByDisplayValue('anna@example.cz')).toBeInTheDocument();
    expect(screen.getByDisplayValue('+420601602603')).toBeInTheDocument();
  });

  it('focuses the first errored field after a failed mirror validation', async () => {
    renderForm();

    // Empty the name so the mirror validation fails on the FIRST field.
    fireEvent.change(screen.getByDisplayValue('Anna Nováková'), { target: { value: '' } });
    fireEvent.submit(document.querySelector('form') as HTMLFormElement);

    const name = await screen.findByLabelText(/jméno/i);
    expect(name.id).toBe('checkout-name');
    await vi.waitFor(() => {
      expect(document.activeElement).toBe(name);
    });
    expect(name.scrollIntoView).toHaveBeenCalled();
  });

  it('clears a field error as soon as the user edits that field', async () => {
    renderForm();

    fireEvent.change(screen.getByDisplayValue('Anna Nováková'), { target: { value: '' } });
    fireEvent.submit(document.querySelector('form') as HTMLFormElement);
    await screen.findByLabelText(/jméno/i);
    expect(screen.getByText('Zadejte jméno a příjmení (2–100 znaků).')).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText(/jméno/i), { target: { value: 'Anna Nováková' } });

    expect(
      screen.queryByText('Zadejte jméno a příjmení (2–100 znaků).'),
    ).not.toBeInTheDocument();
  });
});
