import Link from 'next/link';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { t } from '@/lib/i18n';

const ROUTE_PATH = '/dashboard/admin/makers';

/**
 * URL-state search box for the admin makers list (T-0119b; reworked in
 * T-0175, audit ADM-M2). It used to be a client component calling
 * `router.replace`, which — unlike the native GET forms on
 * orders/faktury/audit — left NO history entry, so Back skipped past the
 * pre-search state instead of returning to it. It is now the same plain
 * `<form method="get">` as its three siblings (and therefore a Server
 * Component), plus the reset affordance the others already had.
 */
export function MakerSearchForm({ initialSearch }: { readonly initialSearch: string }) {
  return (
    <form
      method="get"
      action={ROUTE_PATH}
      className="flex flex-col gap-3 sm:flex-row sm:items-end"
    >
      <div className="min-w-0 flex-1">
        <Input
          name="search"
          icon="search"
          label={t('dashboard.admin.ops.makers.list.searchLabel')}
          defaultValue={initialSearch}
          placeholder={t('dashboard.admin.ops.makers.list.searchPlaceholder')}
          autoComplete="off"
        />
      </div>
      <div className="flex items-center gap-3">
        <Button type="submit" variant="secondary" className="w-full sm:w-auto">
          {t('dashboard.admin.ops.makers.list.searchSubmit')}
        </Button>
        {initialSearch !== '' ? (
          <Link
            href={ROUTE_PATH}
            className="whitespace-nowrap text-sm font-medium text-zinc-400 transition-colors hover:text-white"
          >
            {t('dashboard.admin.makers.search.reset')}
          </Link>
        ) : null}
      </div>
    </form>
  );
}
