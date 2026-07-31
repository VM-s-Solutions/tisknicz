import { render } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { MakerListItem } from '@/lib/api-client-helpers/catalog';
import { axeAA } from '@/lib/testing/axe';
import { MakerCard } from '../maker-card';

/**
 * a11y test for the catalog list surface (T-0133, ADR 0023 §5).
 *
 * The catalog page is a Server Component that fetches; the a11y-relevant
 * markup is the `MakerCard` grid it renders. We render the presentational
 * `MakerCard` with seeded rows (verified + unverified, rated + unrated)
 * and assert zero WCAG 2.1 AA violations. No network — props are seeded.
 */

const verifiedRated: MakerListItem = {
  makerId: '11111111-1111-1111-1111-111111111111',
  slug: 'alfa-tisk',
  companyName: 'Alfa Tisk s.r.o.',
  bio: 'Kvalitní 3D tisk a gravírování na zakázku v Brně.',
  city: 'Brno',
  isVerified: true,
  ratingAverageBp: 47000,
  ratingCount: 128,
  totalOrders: 540,
  logoBlobPath: null,
};

const unverifiedUnrated: MakerListItem = {
  makerId: '22222222-2222-2222-2222-222222222222',
  slug: 'beta-print',
  companyName: 'Beta Print',
  bio: null,
  city: 'Ostrava',
  isVerified: false,
  ratingAverageBp: 0,
  ratingCount: 0,
  totalOrders: 0,
  logoBlobPath: null,
};

describe('catalog MakerCard a11y', () => {
  it('has no axe violations for a verified, rated maker', async () => {
    const { container } = render(<MakerCard item={verifiedRated} />);
    expect(await axeAA(container)).toHaveNoViolations();
  });

  it('has no axe violations for an unverified, unrated maker', async () => {
    const { container } = render(<MakerCard item={unverifiedUnrated} />);
    expect(await axeAA(container)).toHaveNoViolations();
  });

  it('has no axe violations for a grid of mixed maker cards', async () => {
    const { container } = render(
      <ul>
        <li>
          <MakerCard item={verifiedRated} />
        </li>
        <li>
          <MakerCard item={unverifiedUnrated} />
        </li>
      </ul>,
    );
    expect(await axeAA(container)).toHaveNoViolations();
  });
});
