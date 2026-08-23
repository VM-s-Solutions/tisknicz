import { render, screen, within } from '@testing-library/react';
import { axe } from 'jest-axe';
import { describe, expect, it, vi } from 'vitest';
import { MONTH_PARAM, EarningsPanel } from '../dashboard/admin/earnings-panel';
import { AdminShellNav } from '../shell-nav';

/**
 * T-0186 admin shell redesign + the T-0192 earnings panel.
 *
 * The reported defects were all layout — a wrapped brand, raggedly wrapped
 * nav, a clipped identity, a shouting logout — so these tests pin the
 * structural decisions that fix them and would silently regress: that the
 * identity reaches the DOM whole (a truncation is a CSS lie the DOM cannot
 * show, so we assert the full string is present as one node), that the
 * overview link does not claim every admin route, and that logout is not
 * rendered with the destructive weight. The visual result itself was
 * verified in a browser at 375 / 768 / 1280 — that part is not testable here.
 *
 * T-0192 re-cut the earnings panel from a rolling window to a calendar month,
 * so the panel tests now pin the month navigator: that it names its period,
 * that it steps whole months (rolling the year), that it carries the chart's
 * state along, and that it refuses to page into a month that has not started.
 */

vi.mock('next/navigation', () => ({
  usePathname: () => mockPathname,
  useRouter: () => ({ push: vi.fn(), refresh: vi.fn() }),
}));

let mockPathname = '/dashboard/admin';

const ADMIN_EMAIL = 'velmi.dlouhy.administrator@makables.cz';

function revenue(overrides: Partial<Parameters<typeof EarningsPanel>[0]['revenue']> = {}) {
  return {
    year: 2026,
    month: 8,
    fromInclusive: '2026-07-31T22:00:00+00:00',
    toExclusive: '2026-08-31T22:00:00+00:00',
    paidOrderCount: 12,
    grossVolumeMinor: 694_800,
    platformFeeMinor: 90_000,
    makerPayoutMinor: 604_800,
    refundedMinor: 57_900,
    currency: 'CZK',
    isCurrentMonth: false,
    ...overrides,
  };
}

describe('AdminShellNav', () => {
  it('renders the full admin identity as one node, not a clipped fragment', () => {
    mockPathname = '/dashboard/admin';
    render(<AdminShellNav identity={ADMIN_EMAIL} />);

    expect(screen.getByText(ADMIN_EMAIL)).toBeInTheDocument();
  });

  it('keeps the brand on one line as a single non-breaking link', () => {
    mockPathname = '/dashboard/admin';
    render(<AdminShellNav identity={ADMIN_EMAIL} />);

    const brand = screen.getByRole('link', { name: /Makables\s+Admin/ });
    expect(brand).toHaveClass('whitespace-nowrap');
    expect(brand).toHaveClass('shrink-0');
  });

  it('puts every section in one scrollable rail instead of a wrapping row', () => {
    mockPathname = '/dashboard/admin';
    render(<AdminShellNav identity={ADMIN_EMAIL} />);

    const rail = screen.getByRole('navigation', { name: 'Sekce administrace' });
    expect(within(rail).getAllByRole('link')).toHaveLength(10);
    // The rail scrolls; nothing inside it is allowed to wrap.
    for (const link of within(rail).getAllByRole('link')) {
      expect(link).toHaveClass('whitespace-nowrap');
    }
  });

  it('marks only the overview active on the overview route', () => {
    mockPathname = '/dashboard/admin';
    render(<AdminShellNav identity={ADMIN_EMAIL} />);

    const current = screen.getAllByRole('link').filter((l) => l.getAttribute('aria-current') === 'page');
    expect(current).toHaveLength(1);
    expect(current[0]).toHaveAccessibleName('Přehled');
  });

  it('keeps a section highlighted on its detail route', () => {
    mockPathname = '/dashboard/admin/orders/ord-42';
    render(<AdminShellNav identity={ADMIN_EMAIL} />);

    const current = screen.getAllByRole('link').filter((l) => l.getAttribute('aria-current') === 'page');
    expect(current).toHaveLength(1);
    expect(current[0]).toHaveAccessibleName('Objednávky');
  });

  it('renders logout as a neutral action, not the destructive weight', () => {
    mockPathname = '/dashboard/admin';
    render(<AdminShellNav identity={ADMIN_EMAIL} />);

    const logout = screen.getByRole('button', { name: /Odhlásit se/ });
    // `danger` / `dangerGhost` both key off the error token; a routine,
    // reversible sign-out must not be the loudest control on the page.
    expect(logout.className).not.toMatch(/error/);
  });

  it('has no accessibility violations', async () => {
    mockPathname = '/dashboard/admin';
    const { container } = render(<AdminShellNav identity={ADMIN_EMAIL} />);

    expect(await axe(container)).toHaveNoViolations();
  });
});

describe('EarningsPanel', () => {
  it('renders every amount from minor units in whole CZK', () => {
    render(<EarningsPanel revenue={revenue()} />);

    expect(screen.getByText('900 Kč')).toBeInTheDocument(); // platform fee
    expect(screen.getByText('6 948 Kč')).toBeInTheDocument(); // gross volume
    expect(screen.getByText('6 048 Kč')).toBeInTheDocument(); // maker payout
    expect(screen.getByText('579 Kč')).toBeInTheDocument(); // refunded
    expect(screen.getByText('12')).toBeInTheDocument(); // paid order count
  });

  it('does not net the refund into the commission', () => {
    // 900 Kč fee with 579 Kč refunded: a netted panel would print 321 Kč.
    render(<EarningsPanel revenue={revenue()} />);

    expect(screen.getByText('900 Kč')).toBeInTheDocument();
    expect(screen.queryByText('321 Kč')).not.toBeInTheDocument();
  });

  it('names the month it is reporting, declined the Czech way', () => {
    // The number is meaningless without the period it covers — this is the
    // whole reason T-0192 dropped the rolling window.
    render(<EarningsPanel revenue={revenue()} />);

    expect(screen.getByText('srpen 2026')).toBeInTheDocument();
  });

  it('steps a month at a time through the URL, rolling the year backwards', () => {
    render(<EarningsPanel revenue={revenue({ year: 2026, month: 1 })} />);

    const nav = screen.getByRole('navigation', { name: 'Vybraný měsíc' });
    expect(within(nav).getByRole('link', { name: 'Předchozí měsíc' })).toHaveAttribute(
      'href',
      `?${MONTH_PARAM}=2025-12`,
    );
    expect(within(nav).getByRole('link', { name: 'Následující měsíc' })).toHaveAttribute(
      'href',
      `?${MONTH_PARAM}=2026-02`,
    );
  });

  it('carries the chart state across a month step', () => {
    // Changing month must not reset the range the operator was looking at.
    render(<EarningsPanel revenue={revenue()} extraParams={{ range: 'Year', metric: 'gross' }} />);

    const previous = screen.getByRole('link', { name: 'Předchozí měsíc' });
    expect(previous.getAttribute('href')).toContain('range=Year');
    expect(previous.getAttribute('href')).toContain('metric=gross');
    expect(previous.getAttribute('href')).toContain(`${MONTH_PARAM}=2026-07`);
  });

  it('offers no next month while the current one is still running', () => {
    // A month that has not started has nothing to report, so the control is
    // absent rather than a link that lands on a page of zeros.
    render(<EarningsPanel revenue={revenue({ isCurrentMonth: true })} />);

    expect(screen.queryByRole('link', { name: 'Následující měsíc' })).not.toBeInTheDocument();
    expect(screen.getByLabelText('Novější měsíc zatím nezačal')).toHaveAttribute(
      'aria-disabled',
      'true',
    );
    expect(screen.getByText(/probíhá/)).toBeInTheDocument();
  });

  it('says the read failed rather than showing zeros as if they were real', () => {
    render(<EarningsPanel revenue={null} />);

    expect(screen.getByText('Výdělky se nepodařilo načíst.')).toBeInTheDocument();
    expect(screen.queryByText('0 Kč')).not.toBeInTheDocument();
  });

  it('renders a genuinely empty month as zeros', () => {
    render(
      <EarningsPanel
        revenue={revenue({
          paidOrderCount: 0,
          grossVolumeMinor: 0,
          platformFeeMinor: 0,
          makerPayoutMinor: 0,
          refundedMinor: 0,
        })}
      />,
    );

    expect(screen.getAllByText('0 Kč').length).toBeGreaterThan(0);
  });

  it('has no accessibility violations', async () => {
    const { container } = render(<EarningsPanel revenue={revenue()} />);

    expect(await axe(container)).toHaveNoViolations();
  });
});
