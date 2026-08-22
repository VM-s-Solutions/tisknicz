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
  ratingAverageBp: 0,
  ratingCount: 0,
  makerId: 'm1',
  makerSlug: 'alfa-tisk',
  makerCompanyName: 'Alfa Tisk s.r.o.',
  makerIsVerified: true,
  makerPersonalPickupEnabled: false,
  makerPickupNote: null,
  makerLogoBlobPath: null,
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

/**
 * An account is bound to one audience (`User.MatchesAudience`), so the
 * order CTA sent a signed-in maker to a login screen their credentials
 * could never satisfy. The CTA is replaced by a note instead.
 */
describe('product detail order CTA', () => {
  it('links to checkout for a visitor who is not a maker', () => {
    render(<ProductInfo product={baseProduct} />);
    expect(screen.getByRole('link', { name: /Objednat/ })).toHaveAttribute(
      'href',
      '/objednavka?productId=p1',
    );
  });

  // T-0171 (audit PUB-L6): widened from maker-only — an admin session hit
  // the same unsatisfiable login loop, since an account is bound to ONE
  // audience and no non-customer session can mint a customer JWT.
  it('replaces the CTA with an explanation for any signed-in non-customer', () => {
    render(<ProductInfo product={baseProduct} isOtherAudience />);
    expect(screen.queryByRole('link', { name: /Objednat/ })).not.toBeInTheDocument();
    expect(
      screen.getByText('Objednávat může jen zákaznický účet — jste přihlášeni jako maker.'),
    ).toBeInTheDocument();
  });
});
