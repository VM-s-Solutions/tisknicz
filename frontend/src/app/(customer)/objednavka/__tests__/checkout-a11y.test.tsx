import { render } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { ProductDetail } from '@/lib/api-client-helpers/catalog';
import { axeAA } from '@/lib/testing/axe';
import { OrderSummary } from '../order-summary';

/**
 * a11y test for the order/checkout surface (T-0133, ADR 0023 §5).
 *
 * Order placement is a customer-facing critical path. The checkout page
 * is a Server Component whose presentational summary is `OrderSummary`
 * (the sticky product card next to the form). We render it with a seeded
 * `ProductDetail` and assert zero WCAG 2.1 AA violations for the
 * fixed-price, "from"-price, and image-less cases.
 */

const baseProduct: ProductDetail = {
  productId: 'p1',
  title: 'Stojánek na telefon',
  description: 'Praktický stojánek z PLA.',
  priceAmountMinor: 29900,
  priceCurrency: 'CZK',
  priceType: 'Fixed',
  weightGrams: 120,
  categoryId: 'cat-1',
  makerId: 'm1',
  makerSlug: 'alfa-tisk',
  makerCompanyName: 'Alfa Tisk s.r.o.',
  makerIsVerified: true,
  images: [{ imageId: 'i1', blobPath: 'products/p1/img.jpg', sortOrder: 0 }],
};

describe('checkout OrderSummary a11y', () => {
  it('has no axe violations for a fixed-price product with an image', async () => {
    const { container } = render(<OrderSummary product={baseProduct} />);
    expect(await axeAA(container)).toHaveNoViolations();
  });

  it('has no axe violations for a "from"-price product without an image', async () => {
    const product: ProductDetail = {
      ...baseProduct,
      priceType: 'From',
      makerIsVerified: false,
      images: [],
    };
    const { container } = render(<OrderSummary product={product} />);
    expect(await axeAA(container)).toHaveNoViolations();
  });
});
