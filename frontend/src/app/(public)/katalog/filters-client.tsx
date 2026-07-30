'use client';

import { usePathname, useRouter, useSearchParams } from 'next/navigation';
import { type FormEvent, useRef, useState } from 'react';
import { Button } from '@/components/ui/button';
import { Dropdown } from '@/components/ui/dropdown';
import { Icon } from '@/components/ui/icon';
import { Input } from '@/components/ui/input';
import { t } from '@/lib/i18n';

interface CatalogFiltersProps {
  /** Category options resolved server-side (data-driven since T-0119, static fallback). */
  readonly categories: readonly { readonly slug: string; readonly label: string }[];
  readonly initialCategory: string;
  readonly initialCity: string;
  readonly initialMinRating: string;
}

const RATING_OPTIONS = ['1', '2', '3', '4', '5'] as const;
const CITY_DEBOUNCE_MS = 300;

/**
 * URL-state filter form for the catalog page. Pushes
 * <c>category</c> / <c>city</c> / <c>minRating</c> into the search
 * params and resets <c>page</c> to 1 on every change so the server-
 * rendered list re-fetches with fresh paging.
 *
 * - Category select + min-rating select push immediately on change.
 * - City input is debounced 300 ms after the last keystroke, OR pushed
 *   immediately on submit. Avoids one fetch per keystroke (AC-3).
 *
 * Uses <see cref="useRouter().replace"/> for in-place navigation; the
 * browser back button still restores prior URL state (AC-2).
 */
export function CatalogFilters({
  categories,
  initialCategory,
  initialCity,
  initialMinRating,
}: CatalogFiltersProps) {
  const router = useRouter();
  const pathname = usePathname();
  const searchParams = useSearchParams();

  const [category, setCategory] = useState(initialCategory);
  const [city, setCity] = useState(initialCity);
  const [minRating, setMinRating] = useState(initialMinRating);

  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  const pushFilters = (next: { category: string; city: string; minRating: string }): void => {
    const params = new URLSearchParams(searchParams.toString());
    if (next.category) params.set('category', next.category); else params.delete('category');
    if (next.city.trim()) params.set('city', next.city.trim()); else params.delete('city');
    if (next.minRating) params.set('minRating', next.minRating); else params.delete('minRating');
    params.delete('page');
    router.replace(`${pathname}?${params.toString()}`, { scroll: false });
  };

  const handleCategoryChange = (value: string): void => {
    setCategory(value);
    pushFilters({ category: value, city, minRating });
  };

  const handleMinRatingChange = (value: string): void => {
    setMinRating(value);
    pushFilters({ category, city, minRating: value });
  };

  const handleCityChange = (value: string): void => {
    setCity(value);
    if (debounceRef.current) clearTimeout(debounceRef.current);
    debounceRef.current = setTimeout(() => {
      pushFilters({ category, city: value, minRating });
    }, CITY_DEBOUNCE_MS);
  };

  const handleSubmit = (event: FormEvent<HTMLFormElement>): void => {
    event.preventDefault();
    if (debounceRef.current) clearTimeout(debounceRef.current);
    pushFilters({ category, city, minRating });
  };

  const handleReset = (): void => {
    if (debounceRef.current) clearTimeout(debounceRef.current);
    setCategory('');
    setCity('');
    setMinRating('');
    router.replace(pathname, { scroll: false });
  };

  const categoryOptions = categories.map((c) => ({
    value: c.slug,
    label: c.label,
  }));
  const ratingOptions = RATING_OPTIONS.map((stars) => ({
    value: stars,
    label: t('catalog.filter.min_rating_stars', { stars }),
  }));

  return (
    <form onSubmit={handleSubmit}>
      <div className="flex items-center gap-2.5">
        <span aria-hidden="true" className="icon-tile h-8 w-8">
          <Icon name="filter" size={15} />
        </span>
        <h2 className="text-sm font-semibold uppercase tracking-widest text-brand-400">
          {t('catalog.filter.heading')}
        </h2>
      </div>

      <div className="mt-4 grid grid-cols-1 gap-4 lg:grid-cols-[repeat(3,minmax(0,1fr))_auto] lg:items-end">
        <Dropdown
          label={t('catalog.filter.category')}
          value={category}
          onChange={handleCategoryChange}
          options={categoryOptions}
          placeholder={t('catalog.filter.category_any')}
          className="h-11"
        />

        <Input
          label={t('catalog.filter.city')}
          type="text"
          icon="search"
          value={city}
          onChange={(e) => handleCityChange(e.target.value)}
          placeholder={t('catalog.filter.city_placeholder')}
          autoComplete="off"
          className="h-11"
        />

        <Dropdown
          label={t('catalog.filter.min_rating')}
          value={minRating}
          onChange={handleMinRatingChange}
          options={ratingOptions}
          placeholder={t('catalog.filter.min_rating_any')}
          className="h-11"
        />

        <div className="flex flex-row gap-2 pt-1 lg:justify-end lg:pt-0">
          <Button type="submit" variant="primary" className="w-full lg:w-auto">
            {t('catalog.filter.apply')}
          </Button>
          <Button type="button" variant="ghost" onClick={handleReset} className="w-full lg:w-auto">
            {t('catalog.filter.reset')}
          </Button>
        </div>
      </div>
    </form>
  );
}
