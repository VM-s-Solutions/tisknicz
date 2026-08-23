'use client';

import { usePathname, useSearchParams } from 'next/navigation';
import { type FormEvent, useMemo, useRef, useState } from 'react';
import { useNavigationTransition } from '@/components/shared/navigation-transition';
import { Button } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/checkbox';
import { Icon } from '@/components/ui/icon';
import { Input } from '@/components/ui/input';
import { Radio } from '@/components/ui/radio';
import { RangeSlider } from '@/components/ui/range-slider';
import { Spinner } from '@/components/ui/spinner';
import type { MakerLegalType } from '@/lib/api-client-helpers/catalog';
import { t } from '@/lib/i18n';
import type { MessageKey } from '@/lib/i18n/cs-CZ';

interface CatalogFiltersProps {
  /** Category options resolved server-side (data-driven since T-0119, static fallback). */
  readonly categories: readonly { readonly slug: string; readonly label: string }[];
  /** Slugs the server accepted for this render (already canonicalised). */
  readonly initialCategories: readonly string[];
  readonly initialCity: string;
  readonly initialMinRating: string;
  /** Undefined = no constraint (the "Vše" option). */
  readonly initialLegalType?: MakerLegalType;
}

/**
 * Radio, not checkboxes: a maker is a company or an individual trader,
 * never both, and the backend takes a single optional value. Two
 * checkboxes would let a customer tick a combination ("Firma" +
 * "Živnostník") that has no representation in the query and means the
 * same as ticking neither.
 */
/** Every filter value the panel owns; `legalType` is '' for "Vše". */
interface FilterState {
  readonly categories: readonly string[];
  readonly city: string;
  readonly minRating: string;
  readonly legalType: string;
}

const LEGAL_TYPE_OPTIONS: readonly { readonly value: string; readonly labelKey: MessageKey }[] = [
  { value: '', labelKey: 'catalog.filter.legal_type_any' },
  { value: 'LegalEntity', labelKey: 'catalog.filter.legal_type_company' },
  { value: 'NaturalPerson', labelKey: 'catalog.filter.legal_type_sole_trader' },
];

const PUSH_DEBOUNCE_MS = 300;
const FILTER_PANEL_ID = 'catalog-filter-panel';
const MAX_RATING_STARS = 5;

/**
 * Category count above which the list grows a search field. Below it,
 * scanning is faster than typing.
 */
const CATEGORY_SEARCH_THRESHOLD = 10;

/**
 * URL-state filter sidebar for the catalog page. Pushes
 * <c>category</c> (repeatable) / <c>city</c> / <c>minRating</c> into the
 * search params and resets <c>page</c> to 1 on every change so the
 * server-rendered list re-fetches with fresh paging.
 *
 * - Categories are MULTI-SELECT and stay visible in the panel as a
 *   checkbox list; the backend OR-s the selection. The taxonomy is
 *   admin-managed and expected to outgrow the six launch rows, so the
 *   list gains a search field past
 *   {@link CATEGORY_SEARCH_THRESHOLD} entries and scrolls within its
 *   own bounded area — the panel height stays the same at 6 or 60
 *   categories.
 * - Minimum rating is a 0–5 slider (0 = no constraint) rather than a row
 *   of buttons: one control-height row, and the space goes to the
 *   category list.
 * - Every change applies itself — there is no "apply" button. Category
 *   toggles push immediately; the city field and the rating slider are
 *   debounced {@link PUSH_DEBOUNCE_MS} after the last change so a fetch
 *   doesn't fire per keystroke / per slider step (AC-3). The only
 *   footer control is "clear filters".
 * - The `<form>` and its submit handler stay even though nothing renders
 *   a submit button: pressing Enter in the city or category-search field
 *   still implicitly submits, and the handler turns that into an
 *   immediate flush of the pending debounce instead of a full page
 *   reload. Do not delete it as dead code.
 * - Below lg the panel collapses behind a toggle (the sidebar stacks
 *   above the results there); the toggle badge shows the active count.
 *
 * T-0170 (audit PUB-H1/H2/M7): every change navigates with `push`
 * through {@link useNavigationTransition} — back undoes filter changes
 * step-by-step, and the surrounding provider dims the results while the
 * SSR round trip is in flight. The panel itself is remounted by the
 * page via a `key` derived from the canonical URL state, so
 * back/forward and reset links can never leave the controls stale.
 */
export function CatalogFilters({
  categories,
  initialCategories,
  initialCity,
  initialMinRating,
  initialLegalType,
}: CatalogFiltersProps) {
  const pathname = usePathname();
  const searchParams = useSearchParams();
  const { pending, navigate } = useNavigationTransition();

  const [selectedCategories, setSelectedCategories] = useState<readonly string[]>(initialCategories);
  const [city, setCity] = useState(initialCity);
  const [minRating, setMinRating] = useState(initialMinRating);
  // '' is the "Vše" option — kept as a string so it maps 1:1 to the radio
  // group's value and to the query param's presence/absence.
  const [legalType, setLegalType] = useState<string>(initialLegalType ?? '');
  const [categoryQuery, setCategoryQuery] = useState('');
  const [mobileOpen, setMobileOpen] = useState(false);

  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  const cancelPending = (): void => {
    if (debounceRef.current) clearTimeout(debounceRef.current);
  };

  const pushFilters = (next: FilterState): void => {
    const params = new URLSearchParams(searchParams.toString());
    // `category` repeats — delete every existing value before re-adding
    // the current selection, or toggling would accumulate stale slugs.
    params.delete('category');
    for (const slug of next.categories) params.append('category', slug);
    if (next.city.trim()) params.set('city', next.city.trim()); else params.delete('city');
    if (next.minRating) params.set('minRating', next.minRating); else params.delete('minRating');
    if (next.legalType) params.set('legalType', next.legalType); else params.delete('legalType');
    params.delete('page');
    const query = params.toString();
    // Push, not replace — a filter change is a meaningful state the back
    // button must be able to undo (T-0170, PUB-M7).
    navigate(query ? `${pathname}?${query}` : pathname);
  };

  /** Re-push after {@link PUSH_DEBOUNCE_MS} of quiet — for continuous inputs. */
  const pushDebounced = (next: FilterState): void => {
    cancelPending();
    debounceRef.current = setTimeout(() => pushFilters(next), PUSH_DEBOUNCE_MS);
  };

  /** The current state, with one field overridden by whatever just changed. */
  const withCurrent = (override: Partial<FilterState>): FilterState => ({
    categories: selectedCategories,
    city,
    minRating,
    legalType,
    ...override,
  });

  const handleCategoryToggle = (slug: string): void => {
    const next = selectedCategories.includes(slug)
      ? selectedCategories.filter((s) => s !== slug)
      // Append rather than rebuild from the option order, so the URL
      // reflects the click sequence and stays stable across re-renders.
      : [...selectedCategories, slug];
    setSelectedCategories(next);
    cancelPending();
    pushFilters(withCurrent({ categories: next }));
  };

  const handleClearCategories = (): void => {
    setSelectedCategories([]);
    cancelPending();
    pushFilters(withCurrent({ categories: [] }));
  };

  const handleMinRatingChange = (value: string): void => {
    // 0 on the slider means "no minimum" — clear the param rather than
    // sending minRating=0, which the backend validator rejects (1..5).
    const next = value === '0' ? '' : value;
    setMinRating(next);
    pushDebounced(withCurrent({ minRating: next }));
  };

  const handleCityChange = (value: string): void => {
    setCity(value);
    pushDebounced(withCurrent({ city: value }));
  };

  /** Discrete pick — pushes immediately, like a category toggle. */
  const handleLegalTypeChange = (value: string): void => {
    setLegalType(value);
    cancelPending();
    pushFilters(withCurrent({ legalType: value }));
  };

  const handleSubmit = (event: FormEvent<HTMLFormElement>): void => {
    event.preventDefault();
    cancelPending();
    pushFilters(withCurrent({}));
  };

  const handleReset = (): void => {
    cancelPending();
    setSelectedCategories([]);
    setCity('');
    setMinRating('');
    setLegalType('');
    setCategoryQuery('');
    navigate(pathname);
  };

  const activeCount =
    selectedCategories.length + [city.trim(), minRating, legalType].filter(Boolean).length;

  const showCategorySearch = categories.length > CATEGORY_SEARCH_THRESHOLD;

  const visibleCategories = useMemo(() => {
    const needle = categoryQuery.trim().toLocaleLowerCase('cs-CZ');
    if (!needle) return categories;
    return categories.filter((c) => c.label.toLocaleLowerCase('cs-CZ').includes(needle));
  }, [categories, categoryQuery]);

  const ratingValue = minRating === '' ? 0 : Number(minRating);
  const ratingLabel =
    ratingValue === 0
      ? t('catalog.filter.min_rating_any')
      : t('catalog.filter.min_rating_stars', { stars: ratingValue });

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
            <span className="inline-flex h-5 min-w-5 items-center justify-center rounded-md bg-tint-brand px-1.5 text-xs font-semibold text-on-tint-brand">
              {activeCount}
            </span>
          ) : null}
          {pending ? (
            <span role="status" className="inline-flex items-center">
              <Spinner size="sm" />
              <span className="sr-only">{t('catalog.results.loading')}</span>
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
        className={`${mobileOpen ? 'flex' : 'hidden'} mt-4 flex-col gap-5 lg:flex`}
      >
        <div>
          <div className="mb-2 flex items-baseline justify-between gap-2">
            <h3 className="text-sm font-medium text-zinc-300">{t('catalog.filter.category')}</h3>
            {selectedCategories.length > 0 ? (
              <button
                type="button"
                onClick={handleClearCategories}
                className="text-xs font-medium text-zinc-500 transition-colors hover:text-brand-300"
              >
                {t('catalog.filter.category_clear')}
              </button>
            ) : null}
          </div>

          {showCategorySearch ? (
            <div className="mb-2">
              <Input
                type="text"
                icon="search"
                value={categoryQuery}
                onChange={(e) => setCategoryQuery(e.target.value)}
                placeholder={t('catalog.filter.category_search')}
                aria-label={t('catalog.filter.category_search')}
                autoComplete="off"
              />
            </div>
          ) : null}

          {/* The list scrolls inside its own bounded area rather than
              growing the panel: the category taxonomy is admin-managed
              and open-ended, and an unbounded list is what pushed the
              sticky sidebar past the viewport before. */}
          <div
            role="group"
            aria-label={t('catalog.filter.category')}
            className="flex max-h-48 flex-col gap-1.5 overflow-y-auto pr-1"
          >
            {visibleCategories.length === 0 ? (
              <p className="py-1 text-sm text-zinc-500">{t('catalog.filter.category_no_match')}</p>
            ) : (
              visibleCategories.map((option) => (
                <Checkbox
                  key={option.slug}
                  label={option.label}
                  checked={selectedCategories.includes(option.slug)}
                  onChange={() => handleCategoryToggle(option.slug)}
                />
              ))
            )}
          </div>
        </div>

        <RangeSlider
          id="catalog-min-rating"
          min={0}
          max={MAX_RATING_STARS}
          value={ratingValue}
          onChange={(next) => handleMinRatingChange(String(next))}
          label={t('catalog.filter.min_rating')}
          valueLabel={ratingLabel}
          ariaValueText={ratingLabel}
        />

        <fieldset>
          <legend className="mb-2 text-sm font-medium text-zinc-300">
            {t('catalog.filter.legal_type')}
          </legend>
          <div className="flex flex-col gap-1.5">
            {LEGAL_TYPE_OPTIONS.map((option) => (
              <Radio
                key={option.value || 'any'}
                name="catalog-legal-type"
                value={option.value}
                label={t(option.labelKey)}
                checked={legalType === option.value}
                onChange={() => handleLegalTypeChange(option.value)}
              />
            ))}
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
        />

        {activeCount > 0 ? (
          // Only rendered while something is filtered — a reset that has
          // nothing to reset was panel noise (unified with the maker-
          // profile panel's behavior, T-0170).
          <div className="border-t border-zinc-800/80 pt-4">
            <Button type="button" variant="ghost" onClick={handleReset} className="w-full">
              {t('catalog.filter.reset')}
            </Button>
          </div>
        ) : null}
      </div>
    </form>
  );
}
