import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { ProductDetail } from '@/lib/api-client-helpers/catalog';
import { ProductInfo } from '../[productId]/page';

/**
 * T-0144 AC-3 — the product detail badge reads "Na zakázku" for
 * `FulfillmentType.MadeToOrder` and "Skladem" for `InStock`. One test
 * per branch, per the ticket's test-plan reference.
 */

const baseProduct: ProductDetail = {
  productId: 'p1',
  title: 'Stojánek na telefon',
  description: 'Praktický stojánek z PLA.',
  priceAmountMinor: 29900,
  priceCurrency: 'CZK',
  priceType: 'Fixed',
  fulfillmentType: 'MadeToOrder',
  weightGrams: 120,
  categoryId: 'cat-1',
  makerId: 'm1',
  makerSlug: 'alfa-tisk',
  makerCompanyName: 'Alfa Tisk s.r.o.',
  makerIsVerified: true,
  images: [],
};

describe('product detail fulfillment-type badge', () => {
  it('renders "Na zakázku" for MadeToOrder', () => {
    render(<ProductInfo product={{ ...baseProduct, fulfillmentType: 'MadeToOrder' }} />);
    expect(screen.getByText('Na zakázku')).toBeInTheDocument();
  });

  it('renders "Skladem" for InStock', () => {
    render(<ProductInfo product={{ ...baseProduct, fulfillmentType: 'InStock' }} />);
    expect(screen.getByText('Skladem')).toBeInTheDocument();
  });
});
