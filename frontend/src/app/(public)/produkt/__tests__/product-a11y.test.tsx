import { render } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { ProductImageItem } from '@/lib/api-client-helpers/catalog';
import { axeAA } from '@/lib/testing/axe';
import { ProductGallery } from '../[productId]/product-gallery';

/**
 * a11y test for the product detail surface (T-0133, ADR 0023 §5).
 *
 * The product page is a Server Component; its only interactive,
 * a11y-sensitive subtree is the `ProductGallery` (thumbnail buttons with
 * `aria-label`/`aria-pressed`, the primary image `alt`). We render it
 * with seeded images and assert zero WCAG 2.1 AA violations across the
 * empty, single-image, and multi-image cases.
 */

const images: readonly ProductImageItem[] = [
  { imageId: 'a', blobPath: 'products/p1/img-a.jpg', sortOrder: 0 },
  { imageId: 'b', blobPath: 'products/p1/img-b.jpg', sortOrder: 1 },
  { imageId: 'c', blobPath: 'products/p1/img-c.jpg', sortOrder: 2 },
];

describe('product ProductGallery a11y', () => {
  it('has no axe violations with multiple images (thumbnail buttons)', async () => {
    const { container } = render(
      <ProductGallery images={images} title="Stojánek na telefon" />,
    );
    expect(await axeAA(container)).toHaveNoViolations();
  });

  it('has no axe violations with a single image (no thumbnail strip)', async () => {
    const { container } = render(
      <ProductGallery images={[images[0]]} title="Stojánek na telefon" />,
    );
    expect(await axeAA(container)).toHaveNoViolations();
  });

  it('has no axe violations with no images (placeholder)', async () => {
    const { container } = render(
      <ProductGallery images={[]} title="Stojánek na telefon" />,
    );
    expect(await axeAA(container)).toHaveNoViolations();
  });
});
