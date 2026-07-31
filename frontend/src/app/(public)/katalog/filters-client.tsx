'use client';

import { usePathname, useRouter, useSearchParams } from 'next/navigation';
import { type FormEvent, useRef, useState } from 'react';
import { Button } from '@/components/ui/button';
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
const FILTER_PANEL_ID = 'catalog-filter-panel';

/**
 * URL-state filter sidebar for the catalog page. Pushes
 * <c>category</c> / <c>city</c> / <c>minRating</c> into the search
 * params and resets <c>page</c> to 1 on every change so the server-
 * rendered list re-fetches with fresh paging.
 *
 * - Category option list + rating pills push immediately on click;
 *   clicking the active rating pill clears it.
 * - City input is debounced 300 ms after the last keystroke, OR pushed
 *   immediately on submit. Avoids one fetch per keystroke (AC-3).
 * - Below lg the panel collapses behind a toggle (the sidebar stacks
 *   above the results there); the toggle badge shows the active count.
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
  const [mobileOpen, setMobileOpen] = useState(false);

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
    const next = value === minRating ? '' : value;
    setMinRating(next);
    pushFilters({ category, city, minRating: next });
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

  const activeCount = [category, city.trim(), minRating].filter(Boolean).length;

  const categoryOptions: readonly { readonly value: string; readonly label: string }[] = [
    { value: '', label: t('catalog.filter.category_any') },
    ...categories.map((c) => ({ value: c.slug, label: c.label })),
  ];

  return (
    <form onSubmit={handleSubmit}>
      <div className="flex items-center justify-between gap-3">
        <div className="flex items-center gap-2.5">
          <span aria-hidden="true" className="icon-tile h-8 w-8">
            <Icon name="filter" size={15} />
          </span>
          <h2 className="text-xs font-semibold uppercase tracking-widest text-zinc-500">
            {t('catalog.filter.heading')}
          </h2>
          {activeCount > 0 ? (
            <span className="inline-flex h-5 min-w-5 items-center justify-center rounded-md bg-brand-400/15 px-1.5 text-xs font-semibold text-brand-300">
              {activeCount}
            </span>
          ) : null}
        </div>
        <button
          type="button"
          onClick={() => setMobileOpen((open) => !open)}
          aria-expanded={mobileOpen}
          aria-controls={FILTER_PANEL_ID}
          aria-label={t('catalog.filter.heading')}
          className="rounded-lg p-1.5 text-zinc-400 transition-colors hover:bg-zinc-800/60 hover:text-zinc-200 lg:hidden"
        >
          <Icon name="chevronDown" size={18} className={mobileOpen ? 'rotate-180' : ''} />
        </button>
      </div>

      <div
        id={FILTER_PANEL_ID}
        className={`${mobileOpen ? 'flex' : 'hidden'} mt-5 flex-col gap-6 lg:flex`}
      >
        <fieldset className="flex flex-col gap-2">
          <legend className="mb-2 text-xs font-semibold uppercase tracking-widest text-zinc-500">
            {t('catalog.filter.category')}
          </legend>
          <ul className="flex max-h-64 flex-col gap-1 overflow-y-auto pr-1">
            {categoryOptions.map((option) => {
              const isActive = option.value === category;
              return (
                <li key={option.value}>
                  <button
                    type="button"
                    onClick={() => handleCategoryChange(option.value)}
                    aria-pressed={isActive}
                    className={`w-full rounded-lg border px-3 py-2 text-left text-sm transition-colors ${
                      isActive
                        ? 'border-brand-400/30 bg-brand-400/10 font-medium text-brand-300'
                        : 'border-transparent text-zinc-400 hover:bg-zinc-800/60 hover:text-zinc-200'
                    }`}
                  >
                    {option.label}
                  </button>
                </li>
              );
            })}
          </ul>
        </fieldset>

        <fieldset className="flex flex-col">
          <legend className="mb-3 text-xs font-semibold uppercase tracking-widest text-zinc-500">
            {t('catalog.filter.min_rating')}
          </legend>
          <div className="flex flex-wrap gap-2">
            {RATING_OPTIONS.map((stars) => {
              const isActive = stars === minRating;
              return (
                <button
                  key={stars}
                  type="button"
                  onClick={() => handleMinRatingChange(stars)}
                  aria-pressed={isActive}
                  aria-label={t('catalog.filter.min_rating_stars', { stars })}
                  className={`inline-flex items-center gap-1 rounded-lg border px-3 py-1.5 text-xs font-medium transition-colors ${
                    isActive
                      ? 'border-brand-400/40 bg-brand-400/10 text-brand-300'
                      : 'border-zinc-700 text-zinc-400 hover:border-zinc-500 hover:text-zinc-200'
                  }`}
                >
                  <Icon
                    name="star"
                    size={12}
                    className={isActive ? 'text-amber-400' : 'text-zinc-500'}
                  />
                  {stars}+
                </button>
              );
            })}
          </div>
        </fieldset>

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

        <div className="flex flex-col gap-2 border-t border-zinc-800/80 pt-4">
          <Button type="submit" variant="primary" className="w-full">
            {t('catalog.filter.apply')}
          </Button>
          <Button type="button" variant="ghost" onClick={handleReset} className="w-full">
            {t('catalog.filter.reset')}
          </Button>
        </div>
      </div>
    </form>
  );
}
