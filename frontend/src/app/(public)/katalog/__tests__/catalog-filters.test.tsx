import { fireEvent, render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { axeAA } from '@/lib/testing/axe';
import { CatalogFilters } from '../filters-client';

/**
 * The catalog category filter is MULTI-select: each pick appends a
 * repeated `category` query param and the backend OR-s them
 * (`GetPagedMakers` / `CatalogQueries`). These tests pin the URL
 * contract, because a record-shaped param builder silently collapses
 * repeats to the last value and the bug only shows as "the filter
 * ignores everything but my newest pick".
 */

const replace = vi.fn();
let currentSearch = '';

vi.mock('next/navigation', () => ({
  useRouter: () => ({ replace }),
  usePathname: () => '/katalog',
  useSearchParams: () => new URLSearchParams(currentSearch),
}));

const CATEGORIES = [
  { slug: '3d-tisk', label: '3D tisk' },
  { slug: 'laser-cnc', label: 'Laser a CNC' },
  { slug: 'handmade', label: 'Handmade' },
];

function renderFilters(initialCategories: readonly string[] = [], initialCity = '') {
  return render(
    <CatalogFilters
      categories={CATEGORIES}
      initialCategories={initialCategories}
      initialCity={initialCity}
      initialMinRating=""
    />,
  );
}

/** Query params of the most recent router.replace() call. */
function lastPushedParams(): URLSearchParams {
  const lastUrl = replace.mock.calls.at(-1)?.[0] as string;
  return new URLSearchParams(lastUrl.split('?')[1] ?? '');
}

beforeEach(() => {
  replace.mockClear();
  currentSearch = '';
});

describe('CatalogFilters categories', () => {
  it('emits one repeated `category` param per selection', () => {
    renderFilters();

    fireEvent.click(screen.getByLabelText('3D tisk'));
    fireEvent.click(screen.getByLabelText('Laser a CNC'));

    expect(lastPushedParams().getAll('category')).toEqual(['3d-tisk', 'laser-cnc']);
  });

  it('deselects an already-selected category', () => {
    renderFilters(['3d-tisk', 'laser-cnc']);

    fireEvent.click(screen.getByLabelText('3D tisk'));

    expect(lastPushedParams().getAll('category')).toEqual(['laser-cnc']);
  });

  it('drops stale category params instead of accumulating them', () => {
    // The URL already carries a category the component does not hold in
    // state — a naive `append` would keep it forever.
    currentSearch = 'category=stale&page=3';
    renderFilters();

    fireEvent.click(screen.getByLabelText('Handmade'));

    const params = lastPushedParams();
    expect(params.getAll('category')).toEqual(['handmade']);
    // Paging resets on every filter change.
    expect(params.get('page')).toBeNull();
  });

  it('reflects the selection as checked boxes', () => {
    renderFilters(['3d-tisk']);

    expect(screen.getByLabelText('3D tisk')).toBeChecked();
    expect(screen.getByLabelText('Handmade')).not.toBeChecked();
  });

  it('clears only the categories from the clear-selection control', () => {
    currentSearch = 'category=3d-tisk&city=Brno';
    renderFilters(['3d-tisk'], 'Brno');

    fireEvent.click(screen.getByRole('button', { name: 'Zrušit výběr' }));

    const params = lastPushedParams();
    expect(params.getAll('category')).toEqual([]);
    expect(params.get('city')).toBe('Brno');
  });

  it('hides the search field below the threshold', () => {
    renderFilters();
    expect(screen.queryByLabelText('Hledat kategorii')).not.toBeInTheDocument();
  });

  it('clears every filter on reset', () => {
    renderFilters(['3d-tisk']);

    fireEvent.click(screen.getByRole('button', { name: 'Vymazat filtry' }));

    expect(replace).toHaveBeenCalledWith('/katalog', { scroll: false });
  });

  it('has no axe violations', async () => {
    const { container } = renderFilters(['3d-tisk']);

    expect(await axeAA(container)).toHaveNoViolations();
  });
});

describe('CatalogFilters category search', () => {
  const MANY = Array.from({ length: 12 }, (_, i) => ({
    slug: `kategorie-${i}`,
    label: `Kategorie ${i}`,
  }));

  function renderMany() {
    return render(
      <CatalogFilters
        categories={[...MANY, { slug: 'laser-cnc', label: 'Laser a CNC' }]}
        initialCategories={[]}
        initialCity=""
        initialMinRating=""
      />,
    );
  }

  it('appears past the threshold and narrows the list', () => {
    renderMany();

    const search = screen.getByLabelText('Hledat kategorii');
    fireEvent.change(search, { target: { value: 'laser' } });

    expect(screen.getByLabelText('Laser a CNC')).toBeInTheDocument();
    expect(screen.queryByLabelText('Kategorie 0')).not.toBeInTheDocument();
  });

  it('reports when nothing matches', () => {
    renderMany();

    fireEvent.change(screen.getByLabelText('Hledat kategorii'), {
      target: { value: 'zzz' },
    });

    expect(screen.getByText('Žádná kategorie neodpovídá')).toBeInTheDocument();
  });
});

describe('CatalogFilters minimum rating', () => {
  it('pushes the slider value after the debounce', () => {
    vi.useFakeTimers();
    try {
      renderFilters();

      fireEvent.change(screen.getByLabelText('Minimální hodnocení'), {
        target: { value: '4' },
      });
      vi.runAllTimers();

      expect(lastPushedParams().get('minRating')).toBe('4');
    } finally {
      vi.useRealTimers();
    }
  });

  it('clears the param at 0 rather than sending minRating=0', () => {
    // The backend validator only accepts 1..5, so the slider's zero
    // position has to mean "no constraint", not "zero stars".
    vi.useFakeTimers();
    try {
      renderFilters();
      const slider = screen.getByLabelText('Minimální hodnocení');

      fireEvent.change(slider, { target: { value: '3' } });
      vi.runAllTimers();
      expect(lastPushedParams().get('minRating')).toBe('3');

      fireEvent.change(slider, { target: { value: '0' } });
      vi.runAllTimers();
      expect(lastPushedParams().get('minRating')).toBeNull();
    } finally {
      vi.useRealTimers();
    }
  });
});

describe('CatalogFilters legal type', () => {
  it('pushes the selected type immediately', () => {
    // A discrete pick, so it is not debounced the way the slider and the
    // city field are.
    renderFilters();

    fireEvent.click(screen.getByLabelText('Živnostník'));

    expect(lastPushedParams().get('legalType')).toBe('NaturalPerson');
  });

  it('emits the company value under the same param', () => {
    renderFilters();

    fireEvent.click(screen.getByLabelText('Firma'));

    expect(lastPushedParams().get('legalType')).toBe('LegalEntity');
  });

  it('clears the param on "Vše" rather than sending an empty value', () => {
    // An empty legalType= would fail the backend's enum binding; absence
    // is how "no constraint" is expressed.
    currentSearch = 'legalType=LegalEntity';
    render(
      <CatalogFilters
        categories={CATEGORIES}
        initialCategories={[]}
        initialCity=""
        initialMinRating=""
        initialLegalType="LegalEntity"
      />,
    );

    fireEvent.click(screen.getByLabelText('Vše'));

    expect(lastPushedParams().get('legalType')).toBeNull();
  });

  it('reflects the initial selection', () => {
    render(
      <CatalogFilters
        categories={CATEGORIES}
        initialCategories={[]}
        initialCity=""
        initialMinRating=""
        initialLegalType="NaturalPerson"
      />,
    );

    expect(screen.getByLabelText('Živnostník')).toBeChecked();
    expect(screen.getByLabelText('Firma')).not.toBeChecked();
    expect(screen.getByLabelText('Vše')).not.toBeChecked();
  });

  it('resets to "Vše" and drops the param on clear-all', () => {
    render(
      <CatalogFilters
        categories={CATEGORIES}
        initialCategories={[]}
        initialCity=""
        initialMinRating=""
        initialLegalType="NaturalPerson"
      />,
    );

    fireEvent.click(screen.getByRole('button', { name: 'Vymazat filtry' }));

    expect(replace).toHaveBeenCalledWith('/katalog', { scroll: false });
    expect(screen.getByLabelText('Vše')).toBeChecked();
  });

  it('keeps the other filters when the type changes', () => {
    currentSearch = 'category=3d-tisk&city=Brno&page=4';
    renderFilters(['3d-tisk'], 'Brno');

    fireEvent.click(screen.getByLabelText('Firma'));

    const params = lastPushedParams();
    expect(params.get('legalType')).toBe('LegalEntity');
    expect(params.getAll('category')).toEqual(['3d-tisk']);
    expect(params.get('city')).toBe('Brno');
    // Paging resets, same as every other filter change.
    expect(params.get('page')).toBeNull();
  });
});
