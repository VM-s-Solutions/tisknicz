import Link from 'next/link';
import type { Metadata } from 'next';
import { notFound, redirect } from 'next/navigation';
import { Alert } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Card } from '@/components/ui/card';
import { Icon } from '@/components/ui/icon';
import { ADMIN_LIST_MAX_PAGE_SIZE } from '@/lib/api-client-helpers/admin-client';
import {
  type AdminAuditPage,
  type AdminOrderDetail,
  getAdminOrderAuditTrail,
  getAdminOrderDetail,
} from '@/lib/api-client-helpers/admin-orders';
import { t } from '@/lib/i18n';
import { formatCzk } from '@/lib/money/formatter';
import { orderStateBadgeVariant, orderStateLabelKey } from '@/lib/orders/state-labels';
import { formatDateTime } from '@/lib/utils/dates';
import { OrderActions } from './order-actions';
import { DisputeForm } from './dispute-form';
import { ReturnLabelForm } from './return-label-form';

/**
 * Admin order detail (T-0118b / T-0127 re-wire, US-admin-0009 AC-2 + the
 * three money/state action surfaces). Server Component throughout — the
 * order header + the order's audit trail are SSR-fetched (cookie forwarding
 * per patterns.md B.14 / ADR 0024); only the refund/state modals
 * (`order-actions.tsx`) and the dispute form (`dispute-form.tsx`) are
 * `'use client'` islands, re-syncing via `router.refresh()` (no optimistic
 * UI).
 *
 * T-0127 closed the Q-0024 detail-boundary gap: the header now consumes the
 * real privileged `GetAdminOrderDetail` DTO (full header — number, state,
 * amounts/breakdown, country, maker, `customerEmail`, contact snapshot,
 * lifecycle timestamps; Unscoped, no GDPR redaction) instead of scanning the
 * all-orders list for the id. A `404 OrderNotFound` → `notFound()`. The
 * audit trail still reads from the global audit-log query filtered to
 * `targetEntity:"order"` + this id (§C.2). Line items + the message thread
 * stay out of scope (ticket §Out of scope).
 */

export const dynamic = 'force-dynamic';

const ROUTE_BASE = '/dashboard/admin/orders';
const AUDIT_PAGE_SIZE = ADMIN_LIST_MAX_PAGE_SIZE;

interface PageProps {
  readonly params: Promise<{ orderId: string }>;
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

export function generateMetadata(): Metadata {
  return {
    title: t('dashboard.admin.orderActions.metadata.title'),
    description: t('dashboard.admin.orderActions.metadata.description'),
  };
}

export default async function AdminOrderDetailPage({ params, searchParams }: PageProps) {
  const { orderId } = await params;
  const sp = await searchParams;
  const auditPage = parsePositiveInt(readString(sp.auditPage), 1);

  // The real privileged header (T-0127) + the audit trail are independent
  // reads — neither blocks the other.
  const [detailResult, auditResult] = await Promise.all([
    getAdminOrderDetail(orderId),
    getAdminOrderAuditTrail(orderId, auditPage, AUDIT_PAGE_SIZE),
  ]);

  const detailUnauthorized =
    !detailResult.success && detailResult.error.type === 'Unauthorized';
  const auditUnauthorized = !auditResult.success && auditResult.error.type === 'Unauthorized';
  if (detailUnauthorized || auditUnauthorized) {
    redirect(
      `/admin/login?redirect=${encodeURIComponent(`${ROUTE_BASE}/${encodeURIComponent(orderId)}`)}`,
    );
  }

  // An unknown / inactive id is an authoritative 404 from the detail read
  // (OrderNotFound) — render the not-found page, not a degraded shell.
  if (!detailResult.success && detailResult.error.code === 'order.notFound') {
    notFound();
  }

  const order = detailResult.success ? detailResult.value : undefined;
  const auditTrail = auditResult.success ? auditResult.value : undefined;

  return (
    <section className="py-12 lg:py-16">
      <div className="mx-auto flex max-w-4xl flex-col gap-8 px-4 sm:px-6 lg:px-8">
        <div>
          <Link
            href={ROUTE_BASE}
            className="inline-flex items-center gap-2 text-sm font-medium text-zinc-400 transition-colors hover:text-white"
          >
            <Icon name="arrowLeft" size={16} />
            {t('dashboard.admin.orderActions.backToList')}
          </Link>
        </div>

        <OrderHeader orderId={orderId} order={order} />

        {order ? (
          <>
            <section className="flex flex-col gap-4">
              <h2 className="text-sm font-semibold uppercase tracking-widest text-zinc-500">
                {t('dashboard.admin.orderActions.section.actions')}
              </h2>
              <OrderActions
                orderId={order.orderId}
                orderNumber={order.orderNumber}
                state={order.state}
                totalAmountMinor={order.totalAmountMinor}
                currency={order.currency}
              />
            </section>

            <section className="flex flex-col gap-4">
              <h2 className="text-sm font-semibold uppercase tracking-widest text-zinc-500">
                {t('dashboard.admin.orderActions.section.dispute')}
              </h2>
              <DisputeForm orderId={order.orderId} state={order.state} />
              <ReturnLabelForm
                disputeId={order.openDisputeId}
                disputeCategory={order.openDisputeCategory}
                returnLabelGenerated={order.returnLabelGenerated}
              />
            </section>
          </>
        ) : (
          <Alert variant="warning">
            <p className="font-semibold">
              {t('dashboard.admin.orderActions.header.degraded.title')}
            </p>
            <p className="mt-1 opacity-90">
              {t('dashboard.admin.orderActions.header.degraded.body')}
            </p>
          </Alert>
        )}

        <AuditTrailSection
          orderId={orderId}
          auditTrail={auditTrail}
          page={auditPage}
        />
      </div>
    </section>
  );
}

function OrderHeader({
  orderId,
  order,
}: {
  readonly orderId: string;
  readonly order: AdminOrderDetail | undefined;
}) {
  if (!order) {
    return (
      <header className="flex flex-col gap-2">
        <h1 className="font-mono text-xl font-bold tracking-tight text-white sm:text-2xl">
          {orderId}
        </h1>
        <p className="text-sm text-zinc-400">
          {t('dashboard.admin.orderActions.header.idLabel')}
        </p>
      </header>
    );
  }

  return (
    <header className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="text-3xl font-bold tracking-tight text-white sm:text-4xl">
          {order.orderNumber}
        </h1>
        <Badge variant={orderStateBadgeVariant(order.state)}>
          {t(orderStateLabelKey(order.state))}
        </Badge>
      </div>

      <Card padding="md">
        <dl className="grid grid-cols-1 gap-x-8 gap-y-4 sm:grid-cols-2">
          <HeaderField
            label={t('dashboard.admin.orderActions.header.total')}
            value={formatCzk(order.totalAmountMinor, order.currency)}
          />
          <HeaderField
            label={t('dashboard.admin.orderActions.header.country')}
            value={order.countryCode}
          />
          <HeaderField
            label={t('dashboard.admin.orderActions.header.maker')}
            value={order.makerName}
          />
          <HeaderField
            label={t('dashboard.admin.orderActions.header.customer')}
            value={order.customerEmail}
          />
          <HeaderField
            label={t('dashboard.admin.orderActions.header.contactName')}
            value={order.contactName}
          />
          <HeaderField
            label={t('dashboard.admin.orderActions.header.contactPhone')}
            value={order.contactPhone}
          />
          <HeaderField
            label={t('dashboard.admin.orderActions.header.createdAt')}
            value={formatDateTime(order.createdAt)}
          />
          {order.productTitle ? (
            <HeaderField
              label={t('dashboard.admin.orderActions.header.product')}
              value={order.productTitle}
            />
          ) : null}
        </dl>
      </Card>

      <Card padding="md">
        <h2 className="mb-4 text-xs font-semibold uppercase tracking-widest text-zinc-500">
          {t('dashboard.admin.orderActions.header.breakdown.heading')}
        </h2>
        <dl className="grid grid-cols-1 gap-x-8 gap-y-4 sm:grid-cols-2">
          <HeaderField
            label={t('dashboard.admin.orderActions.header.breakdown.productPrice')}
            value={formatCzk(order.productPriceAmountMinor, order.currency)}
          />
          <HeaderField
            label={t('dashboard.admin.orderActions.header.breakdown.shipping')}
            value={formatCzk(order.shippingPriceAmountMinor, order.currency)}
          />
          <HeaderField
            label={t('dashboard.admin.orderActions.header.breakdown.platformFee')}
            value={formatCzk(order.platformFeeAmountMinor, order.currency)}
          />
          <HeaderField
            label={t('dashboard.admin.orderActions.header.breakdown.makerPayout')}
            value={formatCzk(order.makerPayoutAmountMinor, order.currency)}
          />
          {order.refundedAmountMinor > 0 ? (
            <HeaderField
              label={t('dashboard.admin.orderActions.header.breakdown.refunded')}
              value={formatCzk(order.refundedAmountMinor, order.currency)}
            />
          ) : null}
        </dl>
      </Card>
    </header>
  );
}

function HeaderField({ label, value }: { readonly label: string; readonly value: string }) {
  return (
    <div className="flex flex-col gap-1">
      <dt className="text-xs font-semibold uppercase tracking-widest text-zinc-500">{label}</dt>
      <dd className="break-words text-sm font-medium text-zinc-100">{value}</dd>
    </div>
  );
}

function AuditTrailSection({
  orderId,
  auditTrail,
  page,
}: {
  readonly orderId: string;
  readonly auditTrail: AdminAuditPage | undefined;
  readonly page: number;
}) {
  return (
    <section className="flex flex-col gap-4">
      <h2 className="text-sm font-semibold uppercase tracking-widest text-zinc-500">
        {t('dashboard.admin.orderActions.audit.heading')}
      </h2>

      {!auditTrail ? (
        <Alert variant="error">
          <p className="font-semibold">{t('dashboard.admin.orderActions.audit.error.title')}</p>
          <p className="mt-1 opacity-90">{t('dashboard.admin.orderActions.audit.error.body')}</p>
        </Alert>
      ) : auditTrail.items.length === 0 ? (
        <div className="rounded-2xl border border-dashed border-zinc-800 bg-surface-card px-6 py-12 text-center">
          <p className="text-sm text-zinc-400">{t('dashboard.admin.orderActions.audit.empty')}</p>
        </div>
      ) : (
        <ul className="flex flex-col gap-3">
          {auditTrail.items.map((entry) => (
            <li
              key={entry.id}
              className="flex flex-col gap-1 rounded-2xl border border-zinc-800 bg-surface-card p-4"
            >
              <div className="flex flex-wrap items-center justify-between gap-2">
                <span className="text-sm font-semibold text-zinc-100">{entry.actionCode}</span>
                <span className="text-xs text-zinc-500">{formatDateTime(entry.createdAt)}</span>
              </div>
              <p className="text-sm text-zinc-400">
                {t('dashboard.admin.orderActions.audit.byAdmin', { adminUserId: entry.adminUserId })}
              </p>
              {entry.notes && entry.notes.trim() !== '' ? (
                <p className="text-sm text-zinc-300">{entry.notes}</p>
              ) : null}
            </li>
          ))}
        </ul>
      )}

      <AuditPagination
        orderId={orderId}
        page={page}
        hasNext={auditTrail?.hasNextPage ?? false}
        hasPrevious={auditTrail?.hasPreviousPage ?? false}
      />
    </section>
  );
}

function AuditPagination({
  orderId,
  page,
  hasNext,
  hasPrevious,
}: {
  readonly orderId: string;
  readonly page: number;
  readonly hasNext: boolean;
  readonly hasPrevious: boolean;
}) {
  if (!hasNext && !hasPrevious) {
    return null;
  }

  const hrefFor = (target: number): string => {
    const base = `${ROUTE_BASE}/${encodeURIComponent(orderId)}`;
    return target > 1 ? `${base}?auditPage=${target}` : base;
  };

  return (
    <nav className="flex items-center justify-between gap-4" aria-label={t('dashboard.admin.orderActions.audit.heading')}>
      {hasPrevious ? (
        <Link
          href={hrefFor(page - 1)}
          className="inline-flex items-center gap-2 rounded-xl border border-zinc-700 px-4 py-2.5 text-sm font-semibold text-zinc-300 transition-colors hover:border-zinc-600 hover:bg-zinc-800"
        >
          <Icon name="arrowLeft" size={16} />
          {t('dashboard.admin.orderActions.audit.previous')}
        </Link>
      ) : (
        <span
          aria-disabled="true"
          className="inline-flex cursor-not-allowed items-center gap-2 rounded-xl border border-zinc-800 px-4 py-2.5 text-sm font-semibold text-zinc-600"
        >
          <Icon name="arrowLeft" size={16} />
          {t('dashboard.admin.orderActions.audit.previous')}
        </span>
      )}
      {hasNext ? (
        <Link
          href={hrefFor(page + 1)}
          className="inline-flex items-center gap-2 rounded-xl border border-zinc-700 px-4 py-2.5 text-sm font-semibold text-zinc-300 transition-colors hover:border-zinc-600 hover:bg-zinc-800"
        >
          {t('dashboard.admin.orderActions.audit.next')}
          <Icon name="arrowRight" size={16} />
        </Link>
      ) : (
        <span
          aria-disabled="true"
          className="inline-flex cursor-not-allowed items-center gap-2 rounded-xl border border-zinc-800 px-4 py-2.5 text-sm font-semibold text-zinc-600"
        >
          {t('dashboard.admin.orderActions.audit.next')}
          <Icon name="arrowRight" size={16} />
        </span>
      )}
    </nav>
  );
}
