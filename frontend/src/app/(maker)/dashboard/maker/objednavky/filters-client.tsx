'use client';

import { usePathname, useRouter } from 'next/navigation';
import { useState } from 'react';
import { Button } from '@/components/ui/button';
import { DatePicker } from '@/components/ui/date-picker';
import { Dropdown } from '@/components/ui/dropdown';
import { OrderSort } from '@/lib/api-client-helpers/maker-orders';
import { t } from '@/lib/i18n';

/**
 * URL-state date-range + sort bar for the maker order list (T-0087a §C
 * "date-range + sort mirror T-0086a" — customer `filters-client`
 * precedent). Every change pushes a new URL (`router.push` — history
 * entries keep the back button meaningful) with `page` reset to 1; the
 * server re-renders with fresh data. The active tab is preserved (it
 * owns the `state` dimension — A.1 lock — so unlike the customer bar
 * there is no state select here). No client store — the URL is the
 * single source of truth (Q5 lock).
 */

interface OrdersFiltersProps {
  /** URL tab value to preserve — empty string for the default Nové tab (never emitted). */
  readonly tabParam: string;
  /**
   * URL pageSize to preserve — empty string for the default (never
   * emitted). The window size is not a filter, so filter pushes and
   * reset both keep it, matching the page's tab/pagination links
   * (review NEW-4).
   */
  readonly pageSizeParam: string;
  readonly initialDateFrom: string;
  readonly initialDateTo: string;
  /** Empty string = default `CreatedAtDesc` (never emitted to the URL). */
  readonly initialSort: string;
}

interface FilterValues {
  readonly dateFrom: string;
  readonly dateTo: string;
  readonly sort: string;
}

const SORT_OPTIONS = [
  { value: OrderSort.CreatedAtDesc, label: t('dashboard.maker.orders.sort.CreatedAtDesc') },
  { value: OrderSort.CreatedAtAsc, label: t('dashboard.maker.orders.sort.CreatedAtAsc') },
  { value: OrderSort.TotalAmountDesc, label: t('dashboard.maker.orders.sort.TotalAmountDesc') },
  { value: OrderSort.TotalAmountAsc, label: t('dashboard.maker.orders.sort.TotalAmountAsc') },
  { value: OrderSort.StateAsc, label: t('dashboard.maker.orders.sort.StateAsc') },
];

export function OrdersFilters({
  tabParam,
  pageSizeParam,
  initialDateFrom,
  initialDateTo,
  initialSort,
}: OrdersFiltersProps) {
  const router = useRouter();
  const pathname = usePathname();

  const [dateFrom, setDateFrom] = useState(initialDateFrom);
  const [dateTo, setDateTo] = useState(initialDateTo);
  const [sort, setSort] = useState(initialSort || OrderSort.CreatedAtDesc);

  const pushFilters = (next: FilterValues): void => {
    const params = new URLSearchParams();
    // Only non-default params are emitted — canonical URLs per B.8.
    if (tabParam) params.set('tab', tabParam);
    if (pageSizeParam) params.set('pageSize', pageSizeParam);
    if (next.dateFrom) params.set('dateFrom', next.dateFrom);
    if (next.dateTo) params.set('dateTo', next.dateTo);
    if (next.sort && next.sort !== OrderSort.CreatedAtDesc) params.set('sort', next.sort);
    const query = params.toString();
    router.push(query ? `${pathname}?${query}` : pathname, { scroll: false });
  };

  const handleDateFromChange = (value: string): void => {
    setDateFrom(value);
    pushFilters({ dateFrom: value, dateTo, sort });
  };

  const handleDateToChange = (value: string): void => {
    setDateTo(value);
    pushFilters({ dateFrom, dateTo: value, sort });
  };

  const handleSortChange = (value: string): void => {
    setSort(value);
    pushFilters({ dateFrom, dateTo, sort: value });
  };

  const handleReset = (): void => {
    setDateFrom('');
    setDateTo('');
    setSort(OrderSort.CreatedAtDesc);
    const params = new URLSearchParams();
    if (tabParam) params.set('tab', tabParam);
    if (pageSizeParam) params.set('pageSize', pageSizeParam);
    const query = params.toString();
    router.push(query ? `${pathname}?${query}` : pathname, { scroll: false });
  };

  return (
    <div className="panel grid grid-cols-1 items-end gap-4 rounded-xl border border-zinc-800 p-5 sm:grid-cols-2 sm:p-6 lg:grid-cols-4">
      <DatePicker
        label={t('dashboard.maker.orders.filter.dateFrom')}
        value={dateFrom}
        onChange={handleDateFromChange}
      />

      <DatePicker
        label={t('dashboard.maker.orders.filter.dateTo')}
        value={dateTo}
        onChange={handleDateToChange}
      />

      <Dropdown
        label={t('dashboard.maker.orders.filter.sort')}
        value={sort}
        onChange={handleSortChange}
        options={SORT_OPTIONS}
      />

      <Button type="button" variant="ghost" onClick={handleReset}>
        {t('dashboard.maker.orders.filter.reset')}
      </Button>
    </div>
  );
}
