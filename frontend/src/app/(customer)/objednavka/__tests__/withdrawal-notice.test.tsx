import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { WithdrawalNotice } from '../order-form-client';

/**
 * T-0144 AC-4/AC-5/AC-7 — the checkout withdrawal-right notice
 * branches on the ordered product's `fulfillmentType`: MadeToOrder
 * renders the withdrawal-exemption copy, InStock renders the standard
 * 14-day copy. Both variants carry the interim-copy label per the
 * T-0130 legal-placeholder-lock pattern (AC-7).
 */

describe('checkout WithdrawalNotice', () => {
  it('renders the made-to-order withdrawal-exemption notice', () => {
    render(<WithdrawalNotice fulfillmentType="MadeToOrder" />);
    expect(screen.getByText('Zboží vyrobené na míru')).toBeInTheDocument();
    expect(screen.queryByText('Právo na odstoupení do 14 dnů')).not.toBeInTheDocument();
  });

  it('renders the standard 14-day withdrawal-right notice', () => {
    render(<WithdrawalNotice fulfillmentType="InStock" />);
    expect(screen.getByText('Právo na odstoupení do 14 dnů')).toBeInTheDocument();
    expect(screen.queryByText('Zboží vyrobené na míru')).not.toBeInTheDocument();
  });

  it('always shows the interim-copy label (T-0130 legal-placeholder-lock pattern)', () => {
    render(<WithdrawalNotice fulfillmentType="MadeToOrder" />);
    expect(
      screen.getByText('Předběžné znění — čeká na právní kontrolu'),
    ).toBeInTheDocument();
  });
});
