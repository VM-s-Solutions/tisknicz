import Link from 'next/link';
import type { Metadata } from 'next';
import { Alert } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Icon } from '@/components/ui/icon';
import {
  type AdminMakerListItem,
  getAdminMakers,
} from '@/lib/api-client-helpers/admin-makers';
import { t } from '@/lib/i18n';
import { OpsPagination } from '../ops-pagination';
import { MakerSearchForm } from './maker-search-form';

/**
 * Admin makers list (T-0119b / US-admin-0003..0005). Server Component,
 * `force-dynamic`, URL-state search + pagination (T-0087a precedent).
 * Replaces the T-0140 id-lookup panel — the real cross-tenant list read
 * exists now, so the admin browses makers (including deactivated ones)
 * and drills into `/dashboard/admin/makers/{id}` for the verify /
 * deactivate / refresh-ARES / fee-override actions.
 */

export function generateMetadata(): Metadata {
  return {
    title: t('dashboard.admin.ops.makers.metadata.title'),
    description: t('dashboard.admin.ops.makers.metadata.description'),
  };
}

export const dynamic = 'force-dynamic';

const ROUTE_PATH = '/dashboard/admin/makers';

interface PageProps {
  readonly searchParams: Promise<Record<string, string | string[] | undefined>>;
}

function readString(value: string | string[] | undefined): string {
  if (Array.isArray(value)) return value[0] ?? '';
  return value ?? '';
}

function parsePositiveInt(raw: string, fallback: number): number {
  const parsed = Number.parseInt(raw, 10);
  if (!Number.isFinite(parsed) || parsed < 1) return fallback;
  return parsed;
}

export default async function AdminMakersPage({ searchParams }: PageProps) {
  const sp = await searchParams;
  const search = readString(sp.search).trim();
  const page = parsePositiveInt(readString(sp.page), 1);

  const result = await getAdminMakers({ page, search: search || undefined });

  const extraParams: Record<string, string> = {};
  if (search) extraParams.search = search;

  return (
    <section className="py-12 lg:py-16">
      <div className="mx-auto flex max-w-5xl flex-col gap-6 px-4 sm:px-6 lg:px-8">
        <header>
          <div className="flex items-center gap-3">
            <span className="icon-tile h-10 w-10 shrink-0" aria-hidden="true">
              <Icon name="building" size={18} />
            </span>
            <h1 className="text-3xl font-bold tracking-tight text-white sm:text-4xl">
              {t('dashboard.admin.ops.makers.lookup.title')}
            </h1>
          </div>
          <p className="mt-3 max-w-2xl text-base text-zinc-400">
            {t('dashboard.admin.ops.makers.list.subtitle')}
          </p>
        </header>

        <MakerSearchForm initialSearch={search} />

        {result.success ? (
          result.value.makers.items.length === 0 ? (
            <div className="flex flex-col items-center gap-3 rounded-xl border border-dashed border-zinc-800 bg-surface-card px-6 py-12 text-center">
              <span className="icon-tile h-12 w-12" aria-hidden="true">
                <Icon name="search" size={20} />
              </span>
              <p className="text-sm text-zinc-400">{t('dashboard.admin.ops.makers.list.empty')}</p>
            </div>
          ) : (
            <>
              <div className="rounded-xl border border-zinc-800 bg-surface-card">
                <div className="flex items-center justify-between gap-3 rounded-t-xl border-b border-zinc-800 bg-surface-secondary/60 px-4 py-3">
                  <div className="flex items-center gap-2.5">
                    <h2 className="text-sm font-semibold text-zinc-100">
                      {t('dashboard.admin.ops.makers.lookup.title')}
                    </h2>
                    <Badge dot={false}>{result.value.makers.totalCount}</Badge>
                  </div>
                </div>
                <ul className="divide-y divide-zinc-800">
                  {result.value.makers.items.map((item) => (
                    <li
                      key={item.makerId}
                      className="transition-colors last:rounded-b-xl hover:bg-zinc-800/40"
                    >
                      <MakerRow item={item} />
                    </li>
                  ))}
                </ul>
              </div>
              <OpsPagination
                routePath={ROUTE_PATH}
                page={result.value.makers.page}
                totalPages={result.value.makers.totalPages}
                hasNext={result.value.makers.hasNextPage}
                hasPrevious={result.value.makers.hasPreviousPage}
                extraParams={extraParams}
              />
            </>
          )
        ) : (
          <Alert variant="error">{t('dashboard.admin.ops.makers.list.error')}</Alert>
        )}
      </div>
    </section>
  );
}

function MakerRow({ item }: { readonly item: AdminMakerListItem }) {
  return (
    <Link
      href={`/dashboard/admin/makers/${encodeURIComponent(item.makerId)}`}
      className={`flex flex-wrap items-center justify-between gap-3 p-4 ${item.isActive ? '' : 'opacity-60'}`}
    >
      <div className="min-w-0">
        <div className="flex flex-wrap items-center gap-2">
          <p className="truncate text-base font-semibold text-white">{item.companyName}</p>
          {item.isVerified ? (
            <Badge variant="success">{t('dashboard.admin.ops.makers.badge.verified')}</Badge>
          ) : (
            <Badge variant="warning">{t('dashboard.admin.ops.makers.badge.unverified')}</Badge>
          )}
          {!item.isActive ? (
            <Badge variant="error">{t('dashboard.admin.ops.makers.badge.inactive')}</Badge>
          ) : null}
        </div>
        <p className="mt-1 text-xs text-zinc-500">
          {t('dashboard.admin.ops.makers.list.rowMeta', {
            ico: item.registrationNumber,
            city: item.city,
            email: item.userEmail,
          })}
        </p>
      </div>
      <div className="shrink-0 text-right text-xs text-zinc-400">
        <p>{t('dashboard.admin.ops.makers.list.rowOrders', { count: item.totalOrders })}</p>
        {item.feeRateOverrideBp !== null ? (
          <p className="mt-1 text-brand-300">
            {t('dashboard.admin.ops.makers.list.rowOverride', {
              percent: (item.feeRateOverrideBp / 100).toString().replace('.', ','),
            })}
          </p>
        ) : null}
      </div>
    </Link>
  );
}
