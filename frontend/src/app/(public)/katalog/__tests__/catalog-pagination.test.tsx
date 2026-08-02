import { fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { Pagination } from '../pagination';

/**
 * Paging moves the reader from the bottom of one result set to the top
 * of the next. The router's own scroll restoration does that as an
 * instant jump, so the links turn it off and scroll themselves — these
 * tests pin that, because losing either half is invisible in a unit run
 * and only shows up as a teleport (or as a page stuck at the bottom).
 */

const scrollTo = vi.fn();
let reduceMotion = false;

/**
 * jsdom follows a real <a> href and then logs "navigation not
 * implemented". The click handler is what's under test, not the
 * browser's default action, so swallow it.
 */
const swallowNavigation = (event: Event): void => event.preventDefault();

beforeEach(() => {
  scrollTo.mockClear();
  reduceMotion = false;
  vi.stubGlobal('scrollTo', scrollTo);
  vi.stubGlobal(
    'matchMedia',
    (query: string) => ({ matches: reduceMotion && query.includes('reduce') }) as MediaQueryList,
  );
  document.addEventListener('click', swallowNavigation);
});

afterEach(() => {
  document.removeEventListener('click', swallowNavigation);
});

function renderPagination(page = 2) {
  return render(
    <Pagination page={page} totalPages={5} hasNext hasPrevious baseQuery="category=3d-tisk" />,
  );
}

describe('catalog Pagination', () => {
  it('scrolls the page back to the top smoothly when paging', () => {
    renderPagination();

    fireEvent.click(screen.getByRole('link', { name: /další/i }));

    expect(scrollTo).toHaveBeenCalledWith({ top: 0, behavior: 'smooth' });
  });

  it('jumps instantly when the user prefers reduced motion', () => {
    reduceMotion = true;
    renderPagination();

    fireEvent.click(screen.getByRole('link', { name: /předchozí/i }));

    expect(scrollTo).toHaveBeenCalledWith({ top: 0, behavior: 'auto' });
  });

  it('leaves the page alone when the link opens in a new tab', () => {
    renderPagination();

    fireEvent.click(screen.getByRole('link', { name: /další/i }), { metaKey: true });

    expect(scrollTo).not.toHaveBeenCalled();
  });

  it('keeps the filter query on both page links', () => {
    renderPagination();

    expect(screen.getByRole('link', { name: /předchozí/i })).toHaveAttribute(
      'href',
      '/katalog?category=3d-tisk&page=1',
    );
    expect(screen.getByRole('link', { name: /další/i })).toHaveAttribute(
      'href',
      '/katalog?category=3d-tisk&page=3',
    );
  });
});
