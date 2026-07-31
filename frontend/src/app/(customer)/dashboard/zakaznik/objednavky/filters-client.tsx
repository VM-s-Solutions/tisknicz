'use client';

import { usePathname, useRouter } from 'next/navigation';
import { useState } from 'react';
import { Button } from '@/components/ui/button';
import { DatePicker } from '@/components/ui/date-picker';
import { Dropdown } from '@/components/ui/dropdown';
import { Icon } from '@/components/ui/icon';
import { OrderSort, OrderState } from '@/lib/api-client-helpers/orders-client';
import { t } from '@/lib/i18n';
import { orderStateLabelKey } from '@/lib/orders/state-labels';

/**
 * URL-state filter + sort bar for the customer order list (T-0086a,
 * katalog `filters-client` precedent). Every change pushes a new URL
 * (`router.push` — history entries keep the back button meaningful per
 * AC-7) with `page` reset to 1; the server re-renders with fresh data.
 * Initial values come from the page's canonicalised params so the bar
 * never desyncs from what the server actually used. No client store —
 * the URL is the single source of truth (Q5 lock).
 */

interface OrdersFiltersProps {
  readonly initialState: string;
  readonly initialDateFrom: string;
  readonly initialDateTo: string;
  /** Empty string = default `CreatedAtDesc` (never emitted to the URL). */
  readonly initialSort: string;
}

interface FilterValues {
  readonly state: string;
  readonly dateFrom: string;
  readonly dateTo: string;
  readonly sort: string;
}

const STATE_OPTIONS = Object.values(OrderState).map((state) => ({
  value: state,
  label: t(orderStateLabelKey(state)),
}));

const SORT_OPTIONS = [
  { value: OrderSort.CreatedAtDesc, label: t('customer.orders.sort.CreatedAtDesc') },
  { value: OrderSort.CreatedAtAsc, label: t('customer.orders.sort.CreatedAtAsc') },
  { value: OrderSort.TotalAmountDesc, label: t('customer.orders.sort.TotalAmountDesc') },
  { value: OrderSort.TotalAmountAsc, label: t('customer.orders.sort.TotalAmountAsc') },
  { value: OrderSort.StateAsc, label: t('customer.orders.sort.StateAsc') },
];

export function OrdersFilters({
  initialState,
  initialDateFrom,
  initialDateTo,
  initialSort,
}: OrdersFiltersProps) {
  const router = useRouter();
  const pathname = usePathname();

  const [state, setState] = useState(initialState);
  const [dateFrom, setDateFrom] = useState(initialDateFrom);
  const [dateTo, setDateTo] = useState(initialDateTo);
  const [sort, setSort] = useState(initialSort || OrderSort.CreatedAtDesc);

  const pushFilters = (next: FilterValues): void => {
    const params = new URLSearchParams();
    // Only non-default params are emitted — canonical URLs per B.8.
    if (next.state) params.set('state', next.state);
    if (next.dateFrom) params.set('dateFrom', next.dateFrom);
    if (next.dateTo) params.set('dateTo', next.dateTo);
    if (next.sort && next.sort !== OrderSort.CreatedAtDesc) params.set('sort', next.sort);
    const query = params.toString();
    router.push(query ? `${pathname}?${query}` : pathname, { scroll: false });
  };

  const handleStateChange = (value: string): void => {
    setState(value);
    pushFilters({ state: value, dateFrom, dateTo, sort });
  };

  const handleDateFromChange = (value: string): void => {
    setDateFrom(value);
    pushFilters({ state, dateFrom: value, dateTo, sort });
  };

  const handleDateToChange = (value: string): void => {
    setDateTo(value);
    pushFilters({ state, dateFrom, dateTo: value, sort });
  };

  const handleSortChange = (value: string): void => {
    setSort(value);
    pushFilters({ state, dateFrom, dateTo, sort: value });
  };

  const handleReset = (): void => {
    setState('');
    setDateFrom('');
    setDateTo('');
    setSort(OrderSort.CreatedAtDesc);
    router.push(pathname, { scroll: false });
  };

  return (
    <div className="panel flex flex-col gap-4 rounded-xl border border-zinc-800 p-4 sm:p-5">
      <div className="flex items-center justify-between gap-3">
        <div className="flex items-center gap-2.5">
          <span className="icon-tile h-8 w-8" aria-hidden="true">
            <Icon name="filter" size={15} />
          </span>
          <h2 className="text-xs font-semibold tracking-widest text-zinc-500 uppercase">
            {t('catalog.filter.heading')}
          </h2>
        </div>
        <Button type="button" variant="ghost" size="sm" onClick={handleReset}>
          {t('customer.orders.filter.reset')}
        </Button>
      </div>

      <div className="grid grid-cols-1 items-end gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <Dropdown
          label={t('customer.orders.filter.state')}
          value={state}
          onChange={handleStateChange}
          options={STATE_OPTIONS}
          placeholder={t('customer.orders.filter.state_any')}
        />

        <DatePicker
          label={t('customer.orders.filter.dateFrom')}
          value={dateFrom}
          onChange={handleDateFromChange}
        />

        <DatePicker
          label={t('customer.orders.filter.dateTo')}
          value={dateTo}
          onChange={handleDateToChange}
        />

        <Dropdown
          label={t('customer.orders.filter.sort')}
          value={sort}
          onChange={handleSortChange}
          options={SORT_OPTIONS}
        />
      </div>
    </div>
  );
}
