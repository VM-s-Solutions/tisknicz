'use client';

import { usePathname, useSearchParams } from 'next/navigation';
import { type FormEvent, useRef, useState } from 'react';
import { useNavigationTransition } from '@/components/shared/navigation-transition';
import { Button } from '@/components/ui/button';
import { Dropdown } from '@/components/ui/dropdown';
import { Icon } from '@/components/ui/icon';
import { Input } from '@/components/ui/input';
import { Spinner } from '@/components/ui/spinner';
import { t } from '@/lib/i18n';

interface ProductFiltersProps {
  readonly initialMinPrice: string;
  readonly initialMaxPrice: string;
  readonly initialMinRating: string;
}

const RATING_OPTIONS = ['1', '2', '3', '4', '5'] as const;
// Unified with the catalog panel's debounce (T-0170 — the two panels had
// drifted to 300 vs 400 ms for no reason).
const PRICE_DEBOUNCE_MS = 300;

/**
 * URL-state filter form for the product grid on the maker profile.
 * Same pattern as the catalog's <c>CatalogFilters</c>: pushes
 * <c>minPrice</c> / <c>maxPrice</c> (whole Kč) and <c>minRating</c>
 * (stars) into the search params; the Server Component re-renders the
 * grid with the filtered list. Price inputs are debounced after the
 * last keystroke OR pushed immediately on submit; the rating select
 * pushes immediately on change.
 *
 * T-0170 (PUB-H1/H2/M7): navigates with `push` through
 * {@link useNavigationTransition} (back undoes filter steps; the grid
 * dims while the round trip is in flight), and the page remounts the
 * panel via a canonical-state `key` so back/forward never leaves stale
 * inputs.
 */
export function ProductFilters({
  initialMinPrice,
  initialMaxPrice,
  initialMinRating,
}: ProductFiltersProps) {
  const pathname = usePathname();
  const searchParams = useSearchParams();
  const { pending, navigate } = useNavigationTransition();

  const [minPrice, setMinPrice] = useState(initialMinPrice);
  const [maxPrice, setMaxPrice] = useState(initialMaxPrice);
  const [minRating, setMinRating] = useState(initialMinRating);

  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  const pushFilters = (next: { minPrice: string; maxPrice: string; minRating: string }): void => {
    const params = new URLSearchParams(searchParams.toString());
    if (next.minPrice.trim()) params.set('minPrice', next.minPrice.trim());
    else params.delete('minPrice');
    if (next.maxPrice.trim()) params.set('maxPrice', next.maxPrice.trim());
    else params.delete('maxPrice');
    if (next.minRating) params.set('minRating', next.minRating);
    else params.delete('minRating');
    const query = params.toString();
    navigate(query ? `${pathname}?${query}` : pathname);
  };

  const handlePriceChange = (field: 'min' | 'max', value: string): void => {
    // Digits only — the field is a whole-Kč amount; everything else is
    // dropped before it can reach the URL.
    const cleaned = value.replace(/[^\d]/g, '');
    const next = {
      minPrice: field === 'min' ? cleaned : minPrice,
      maxPrice: field === 'max' ? cleaned : maxPrice,
      minRating,
    };
    if (field === 'min') setMinPrice(cleaned);
    else setMaxPrice(cleaned);
    if (debounceRef.current) clearTimeout(debounceRef.current);
    debounceRef.current = setTimeout(() => pushFilters(next), PRICE_DEBOUNCE_MS);
  };

  const handleMinRatingChange = (value: string): void => {
    setMinRating(value);
    if (debounceRef.current) clearTimeout(debounceRef.current);
    pushFilters({ minPrice, maxPrice, minRating: value });
  };

  const handleSubmit = (event: FormEvent<HTMLFormElement>): void => {
    event.preventDefault();
    if (debounceRef.current) clearTimeout(debounceRef.current);
    pushFilters({ minPrice, maxPrice, minRating });
  };

  const handleReset = (): void => {
    if (debounceRef.current) clearTimeout(debounceRef.current);
    setMinPrice('');
    setMaxPrice('');
    setMinRating('');
    const params = new URLSearchParams(searchParams.toString());
    params.delete('minPrice');
    params.delete('maxPrice');
    params.delete('minRating');
    const query = params.toString();
    navigate(query ? `${pathname}?${query}` : pathname);
  };

  const ratingOptions = RATING_OPTIONS.map((stars) => ({
    value: stars,
    label: t('catalog.filter.min_rating_stars', { stars }),
  }));

  const hasActiveFilter = Boolean(minPrice.trim() || maxPrice.trim() || minRating);

  return (
    <form onSubmit={handleSubmit} className="flex flex-col gap-4">
      <div className="flex items-center gap-2.5">
        <span aria-hidden="true" className="icon-tile h-8 w-8">
          <Icon name="filter" size={15} />
        </span>
        <h2 className="text-xs font-semibold uppercase tracking-widest text-zinc-500">
          {t('catalog.maker.filter.heading')}
        </h2>
        {pending ? (
          <span role="status" className="inline-flex items-center">
            <Spinner size="sm" />
            <span className="sr-only">{t('catalog.results.loading')}</span>
          </span>
        ) : null}
      </div>

      <div className="grid grid-cols-2 gap-3">
        <Input
          label={t('catalog.filter.price_min')}
          type="text"
          inputMode="numeric"
          value={minPrice}
          onChange={(e) => handlePriceChange('min', e.target.value)}
          placeholder="0"
          autoComplete="off"
          className="h-11"
        />
        <Input
          label={t('catalog.filter.price_max')}
          type="text"
          inputMode="numeric"
          value={maxPrice}
          onChange={(e) => handlePriceChange('max', e.target.value)}
          placeholder="5 000"
          autoComplete="off"
          className="h-11"
        />
      </div>

      <Dropdown
        label={t('catalog.filter.min_rating')}
        value={minRating}
        onChange={handleMinRatingChange}
        options={ratingOptions}
        placeholder={t('catalog.filter.min_rating_any')}
        className="h-11"
      />

      {hasActiveFilter ? (
        <Button type="button" variant="ghost" onClick={handleReset} className="w-full">
          {t('catalog.filter.reset')}
        </Button>
      ) : null}
    </form>
  );
}
