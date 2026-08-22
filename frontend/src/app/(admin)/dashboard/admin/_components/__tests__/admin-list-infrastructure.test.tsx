import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { AdminPagination } from '../admin-pagination';
import {
  ADMIN_MAX_PAGE,
  ADMIN_MAX_PAGE_SIZE,
  parsePage,
  parsePageSize,
  retryHref,
} from '../list-params';

/**
 * T-0175: one pagination replaces five drifted copies (ADM-M1), page and
 * pageSize are clamped everywhere (ADM-L5), and error retries keep the
 * admin's filters (ADM-M3).
 */

describe('AdminPagination', () => {
  it('renders the page indicator every consumer now shares', () => {
    render(
      <AdminPagination
        routePath="/dashboard/admin/orders"
        page={2}
        totalPages={5}
        hasNext
        hasPrevious
      />,
    );

    expect(screen.getByText('Stránka 2 z 5')).toBeInTheDocument();
  });

  it('preserves filters and drops page=1 from the query', () => {
    render(
      <AdminPagination
        routePath="/dashboard/admin/orders"
        page={2}
        totalPages={5}
        hasNext
        hasPrevious
        baseParams={{ state: 'Disputed', country: 'CZ' }}
      />,
    );

    expect(screen.getByRole('link', { name: /Předchozí/ })).toHaveAttribute(
      'href',
      '/dashboard/admin/orders?state=Disputed&country=CZ',
    );
    expect(screen.getByRole('link', { name: /Další/ })).toHaveAttribute(
      'href',
      '/dashboard/admin/orders?state=Disputed&country=CZ&page=3',
    );
  });

  it('supports a custom page param for the order-detail audit pager', () => {
    render(
      <AdminPagination
        routePath="/dashboard/admin/orders/o-1"
        pageParam="auditPage"
        page={1}
        totalPages={3}
        hasNext
        hasPrevious={false}
      />,
    );

    expect(screen.getByRole('link', { name: /Další/ })).toHaveAttribute(
      'href',
      '/dashboard/admin/orders/o-1?auditPage=2',
    );
  });

  it('renders nothing for a single page', () => {
    const { container } = render(
      <AdminPagination
        routePath="/dashboard/admin/orders"
        page={1}
        totalPages={1}
        hasNext={false}
        hasPrevious={false}
      />,
    );

    expect(container).toBeEmptyDOMElement();
  });
});

describe('list params', () => {
  it('clamps an absurd deep-linked page instead of passing it to the backend', () => {
    expect(parsePage('9007199254740991')).toBe(ADMIN_MAX_PAGE);
    expect(parsePage('0')).toBe(1);
    expect(parsePage('-3')).toBe(1);
    expect(parsePage('abc')).toBe(1);
    expect(parsePage(undefined)).toBe(1);
    expect(parsePage('4')).toBe(4);
  });

  it('clamps pageSize to the shared ceiling and falls back on junk', () => {
    expect(parsePageSize('5000', 20)).toBe(ADMIN_MAX_PAGE_SIZE);
    expect(parsePageSize('junk', 20)).toBe(20);
    expect(parsePageSize('30', 20)).toBe(30);
    expect(parsePageSize('30', 20, 25)).toBe(25);
  });

  it('rebuilds a retry href carrying the active filters and page', () => {
    expect(retryHref('/dashboard/admin/orders', { state: 'Paid' }, 3)).toBe(
      '/dashboard/admin/orders?state=Paid&page=3',
    );
    expect(retryHref('/dashboard/admin/orders', {}, 1)).toBe('/dashboard/admin/orders');
  });
});
