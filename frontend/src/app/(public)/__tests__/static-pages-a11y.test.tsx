import type { ReactElement } from 'react';
import { render } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { axeAA } from '@/lib/testing/axe';
import GdprPage from '../gdpr/page';
import HowItWorksPage from '../jak-to-funguje/page';
import ForMakersPage from '../pro-makery/page';
import TermsPage from '../vop/page';

/**
 * a11y tests for the static public pages (T-0133, ADR 0023 §5).
 *
 * `/jak-to-funguje`, `/pro-makery`, `/vop`, `/gdpr` are Server Components
 * returning static prose. They render inside the public layout's `<main>`
 * landmark, so we wrap each in a `<main>` to mirror the real document and
 * assert zero WCAG 2.1 AA violations (heading order, landmarks, contrast,
 * link names).
 */

function renderInMain(node: ReactElement) {
  return render(<main>{node}</main>);
}

describe('static pages a11y', () => {
  it('/jak-to-funguje has no axe violations', async () => {
    const { container } = renderInMain(<HowItWorksPage />);
    expect(await axeAA(container)).toHaveNoViolations();
  });

  it('/pro-makery has no axe violations', async () => {
    const { container } = renderInMain(<ForMakersPage />);
    expect(await axeAA(container)).toHaveNoViolations();
  });

  it('/vop has no axe violations', async () => {
    const { container } = renderInMain(<TermsPage />);
    expect(await axeAA(container)).toHaveNoViolations();
  });

  it('/gdpr has no axe violations', async () => {
    const { container } = renderInMain(<GdprPage />);
    expect(await axeAA(container)).toHaveNoViolations();
  });
});
