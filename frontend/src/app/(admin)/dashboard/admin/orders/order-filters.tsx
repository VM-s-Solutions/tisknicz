'use client';

import Link from 'next/link';
import { useState } from 'react';
import { Button } from '@/components/ui/button';
import { Dropdown } from '@/components/ui/dropdown';
import { Icon } from '@/components/ui/icon';
import { Input } from '@/components/ui/input';
import { OrderState } from '@/lib/api-client-helpers/admin-client';
import { t } from '@/lib/i18n';
import { orderStateLabelKey } from '@/lib/orders/state-labels';

/**
 * Admin all-orders filter bar (T-0118a, AC-5/AC-6). A native
 * `<form method="get">` that writes the URL `searchParams` on submit —
 * the URL stays the single source of truth (Option G rejected:
 * deep-links + back/forward round-trip). The custom `Dropdown` primitive
 * is controlled, so the state value bridges into the native GET submit
 * via a hidden input (same wire shape as the previous native
 * `<select>`). `page` is intentionally NOT a field, so a new filter
 * submit resets to page 1.
 *
 * No date-range fields here (unlike the invoices/audit bars): the
 * generated `adminOrders(page,pageSize,state,country,makerId,customerEmail)`
 * contract carries NO dateFrom/dateTo params (T-0118a §C "passed iff the
 * generated signature accepts them … omits cleanly otherwise and logs the
 * gap"). The gap is logged as a backend follow-up — a dead date filter is
 * NOT shipped here.
 *
 * "Vymazat filtry" is a `<Link>` to the bare route (drops every param).
 */

const ROUTE_PATH = '/dashboard/admin/orders';

const STATE_OPTIONS = Object.values(OrderState).map((state) => ({
  value: state,
  label: t(orderStateLabelKey(state)),
}));

interface OrderFiltersProps {
  readonly state: string;
  readonly country: string;
  readonly makerId: string;
  readonly customerEmail: string;
}

export function OrderFilters({ state, country, makerId, customerEmail }: OrderFiltersProps) {
  const [stateValue, setStateValue] = useState(state);

  return (
    <form
      method="get"
      action={ROUTE_PATH}
      className="grid grid-cols-1 items-end gap-4 rounded-xl border border-zinc-800 bg-surface-card p-6 sm:grid-cols-2 lg:grid-cols-3"
    >
      <div className="flex items-center gap-2.5 sm:col-span-2 lg:col-span-3">
        <span aria-hidden="true" className="icon-tile h-8 w-8">
          <Icon name="filter" size={15} />
        </span>
        <h2 className="text-xs font-semibold uppercase tracking-widest text-zinc-500">
          {t('dashboard.admin.common.filterHeading')}
        </h2>
      </div>
      <input type="hidden" name="state" value={stateValue} />
      <Dropdown
        label={t('dashboard.admin.orders.filter.state')}
        value={stateValue}
        onChange={setStateValue}
        options={STATE_OPTIONS}
        placeholder={t('dashboard.admin.orders.filter.stateAll')}
      />
      <Input
        name="country"
        label={t('dashboard.admin.orders.filter.country')}
        defaultValue={country}
        maxLength={2}
        autoCapitalize="characters"
      />
      <Input
        name="customerEmail"
        type="email"
        label={t('dashboard.admin.orders.filter.customer')}
        defaultValue={customerEmail}
      />
      <Input
        name="makerId"
        label={t('dashboard.admin.orders.filter.maker')}
        defaultValue={makerId}
      />
      <div className="flex items-center gap-3 sm:col-span-2 lg:col-span-3">
        <Button type="submit" variant="primary">
          {t('dashboard.admin.orders.filter.apply')}
        </Button>
        <Link
          href={ROUTE_PATH}
          className="text-sm font-medium text-zinc-400 transition-colors hover:text-white"
        >
          {t('dashboard.admin.orders.filter.reset')}
        </Link>
      </div>
    </form>
  );
}
