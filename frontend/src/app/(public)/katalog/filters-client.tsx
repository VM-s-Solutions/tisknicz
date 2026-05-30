'use client';

import { usePathname, useRouter, useSearchParams } from 'next/navigation';
import { type FormEvent, useRef, useState } from 'react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Select } from '@/components/ui/select';
import { CATALOG_CATEGORIES } from '@/lib/catalog/categories';
import { t } from '@/lib/i18n';

interface CatalogFiltersProps {
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

  const categoryOptions = CATALOG_CATEGORIES.map((c) => ({
    value: c.slug,
    label: t(c.labelKey),
  }));
  const ratingOptions = RATING_OPTIONS.map((stars) => ({
    value: stars,
    label: t('catalog.filter.min_rating_stars', { stars }),
  }));

  return (
    <form
      onSubmit={handleSubmit}
      className="flex flex-col gap-4 rounded-2xl border border-zinc-800 bg-surface-card p-6"
    >
      <h2 className="text-sm font-semibold uppercase tracking-widest text-brand-400">
        {t('catalog.filter.heading')}
      </h2>

      <Select
        label={t('catalog.filter.category')}
        value={category}
        onChange={(e) => handleCategoryChange(e.target.value)}
        options={categoryOptions}
        placeholder={t('catalog.filter.category_any')}
      />

      <Input
        label={t('catalog.filter.city')}
        type="text"
        value={city}
        onChange={(e) => handleCityChange(e.target.value)}
        placeholder={t('catalog.filter.city_placeholder')}
        autoComplete="off"
      />

      <Select
        label={t('catalog.filter.min_rating')}
        value={minRating}
        onChange={(e) => handleMinRatingChange(e.target.value)}
        options={ratingOptions}
        placeholder={t('catalog.filter.min_rating_any')}
      />

      <div className="flex flex-col gap-2 pt-2">
        <Button type="submit" variant="primary">
          {t('catalog.filter.apply')}
        </Button>
        <Button type="button" variant="ghost" onClick={handleReset}>
          {t('catalog.filter.reset')}
        </Button>
      </div>
    </form>
  );
}
