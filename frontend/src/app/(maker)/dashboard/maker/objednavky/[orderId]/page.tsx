import { cache } from 'react';
import Link from 'next/link';
import type { Metadata } from 'next';
import { notFound, redirect } from 'next/navigation';
import { Alert } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Card } from '@/components/ui/card';
import { Icon } from '@/components/ui/icon';
import type { OrderMessagesPage } from '@/components/shared/order-message-thread';
import {
  getMakerOrderDetail,
  getMakerOrderMessages,
  type MakerOrderDetail,
  OrderState,
  ShippingMethod,
} from '@/lib/api-client-helpers/maker-orders';
import { formatFileSize } from '@/lib/format/file-size';
import { t } from '@/lib/i18n';
import { formatCzk } from '@/lib/money/formatter';
import { orderStateBadgeVariant, orderStateLabelKey } from '@/lib/orders/state-labels';
import { formatDate } from '@/lib/utils/dates';
import { FileDownloadButton, OrderActions } from './order-actions';
import { OrderThreadClient } from './order-thread-client';
import { OrderTimeline } from './order-timeline';
import { toThreadMessagesPage } from './thread-mapping';

/**
 * Maker order detail at /dashboard/maker/objednavky/[orderId] (T-0087b,
 * US-maker-0006..0011) — the page where the maker runs their workflow:
 * read, accept, ship / hand over, download the label, message the
 * customer. Server Component throughout: the detail + thread-page-1
 * fetches forward the maker audience cookie (patterns.md B.14 /
 * ADR 0024); only the action bar and the shared message thread are
 * client islands, re-syncing via `router.refresh()` (Q5 lock).
 *
 * GDPR surface (T-0082 AC-4 compile-time lock): the DTO has no customer
 * email field — the page renders contact name + phone only, and no
 * `mailto:` exists anywhere in the DOM (AC-9).
 */

// Always render fresh — the order state changes underneath this page
// (webhook, auto-deliver, customer confirmation) and the action matrix
// must reflect it.
export const dynamic = 'force-dynamic';

/** Display scaling only — VAT rates travel as basis points (CLAUDE.md money rules). */
const BASIS_POINTS_PER_PERCENT = 100;

/**
 * Per-request memo (Gate 8 fold): `generateMetadata` and the page body
 * both need the detail, and `apiFetch` composes a fresh
 * `AbortSignal.timeout` per call which defeats Next's fetch
 * memoization — without `cache()` every detail view issues two
 * identical backend GETs. Scope is one server request; a
 * `router.refresh()` is a new request and re-fetches (Q5 lock intact).
 */
const getOrderDetail = cache(getMakerOrderDetail);

interface PageProps {
  readonly params: Promise<{ orderId: string }>;
}

export async function generateMetadata({ params }: PageProps): Promise<Metadata> {
  const { orderId } = await params;
  const result = await getOrderDetail(orderId);
  if (!result.success) {
    // §B.9: branch the title ONLY on NotFound — transient errors keep
    // the brand title so a backend blip never signals "gone".
    const title =
      result.error.type === 'NotFound'
        ? `${t('dashboard.maker.orderDetail.notFound.title')} — ${t('common.app_name')}`
        : t('dashboard.maker.orderDetail.metadata.title');
    return { title };
  }
  return { title: t('dashboard.maker.orderDetail.metadata.title') };
}

/**
 * Wire-honest presence check for nullable URL/id fields: the generated
 * types declare `string | undefined`, but ASP.NET serialises absent
 * values as JSON `null` — `typeof` covers both shapes.
 */
function hasValue(value: string | undefined): value is string {
  return typeof value === 'string' && value !== '';
}

export default async function MakerOrderDetailPage({ params }: PageProps) {
  const { orderId } = await params;

  const result = await getOrderDetail(orderId);
  if (!result.success) {
    // Foreign order = 404 from the backend (one `order.notFound` shape
    // for "missing" and "not yours" — T-0082 §B, no IDOR oracle).
    if (result.error.type === 'NotFound') {
      notFound();
    }
    if (result.error.type === 'Unauthorized') {
      redirect(
        `/login?redirect=${encodeURIComponent(
          `/dashboard/maker/objednavky/${encodeURIComponent(orderId)}`,
        )}`,
      );
    }
    return <LoadErrorState orderId={orderId} />;
  }
  const detail = result.value;

  // Thread page 1 is SSR-prefetched so the shared component paints
  // data-complete (B.1); a prefetch failure degrades to an empty initial
  // page — the thread's first poll recovers (loudly visible, no mock).
  const messagesResult = await getMakerOrderMessages(detail.orderId, 1);
  const initialThreadPage: OrderMessagesPage = messagesResult.success
    ? toThreadMessagesPage(messagesResult.value)
    : { items: [], page: 1, totalCount: 0, hasNextPage: false };

  const shippingMethodLabel =
    detail.shippingMethod === ShippingMethod.PersonalPickup
      ? t('order.page.shippingMethod.personalPickup')
      : t('order.page.shippingMethod.zasilkovna');

  return (
    <section className="mx-auto max-w-6xl px-4 py-10 sm:px-6 lg:px-8">
      <Link
        href="/dashboard/maker/objednavky"
        className="inline-flex items-center gap-1.5 text-sm text-zinc-400 transition-colors hover:text-zinc-200"
      >
        <Icon name="chevronLeft" size={16} />
        {t('dashboard.maker.orderDetail.backToList')}
      </Link>

      <header className="mt-6 flex flex-col gap-2">
        <div className="flex flex-wrap items-center gap-3">
          <h1 className="text-shine text-3xl font-bold tracking-tight sm:text-4xl">
            {t('order.page.title', { orderNumber: detail.orderNumber })}
          </h1>
          <Badge variant={orderStateBadgeVariant(detail.state)}>
            {t(orderStateLabelKey(detail.state))}
          </Badge>
        </div>
        <p className="text-sm text-zinc-400">
          {t('dashboard.maker.orderDetail.createdLine', { date: formatDate(detail.createdAt) })}
        </p>
        <p className="text-sm text-zinc-400">
          {t('dashboard.maker.orderDetail.productLine', {
            title: detail.productTitle ?? t('dashboard.maker.orders.customOrder'),
          })}
        </p>
      </header>

      {/* Two lanes on desktop: workflow (actions + timeline + thread)
          left, money + shipping + contact + documents in the right
          rail. Single stack on mobile — side-by-side only at lg:
          (customer order-detail precedent). */}
      <div className="mt-8 flex flex-col gap-6 lg:grid lg:grid-cols-[minmax(0,1fr)_22rem] lg:items-start lg:gap-8">
        <div className="flex min-w-0 flex-col gap-6">
          <OrderActions
            orderId={detail.orderId}
            orderNumber={detail.orderNumber}
            state={detail.state}
            shippingMethod={detail.shippingMethod}
            shippingCarrierRef={detail.shippingCarrierRef}
          />

          <Card variant="elevated" padding="md">
            <OrderTimeline detail={detail} />
          </Card>

          <Card variant="elevated" padding="md">
            <OrderThreadClient
              orderId={detail.orderId}
              initialPage={initialThreadPage}
              canPost={detail.state !== OrderState.PendingPayment}
            />
          </Card>
        </div>

        <div className="flex min-w-0 flex-col gap-6 lg:sticky lg:top-24">
          <PayoutBreakdown detail={detail} />

          <Card variant="elevated" padding="md" className="flex flex-col gap-1.5">
            <h2 className="text-xs font-semibold uppercase tracking-widest text-zinc-500">
              {t('dashboard.maker.orderDetail.shipping.heading')}
            </h2>
            <div className="divide-y divide-zinc-800">
              <p className="py-2.5 text-sm text-zinc-100">{shippingMethodLabel}</p>
              {hasValue(detail.zasilkovnaPickupPointId) ? (
                <p className="py-2.5 text-sm text-zinc-400">
                  {t('dashboard.maker.orderDetail.shipping.pickupPoint', {
                    id: detail.zasilkovnaPickupPointId,
                  })}
                </p>
              ) : null}
              {hasValue(detail.shippingCarrierTrackingUrl) ? (
                <div className="py-2.5">
                  <a
                    href={detail.shippingCarrierTrackingUrl}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="inline-flex items-center gap-1.5 text-sm font-medium text-brand-400 transition-colors hover:text-brand-300"
                  >
                    <Icon name="truck" size={16} />
                    {t('dashboard.maker.orderDetail.shipping.trackingLink')}
                  </a>
                </div>
              ) : null}
            </div>
          </Card>

          {/* Contact card — name + phone ONLY; the DTO has no email field
              (T-0082 AC-4) and no mailto: is rendered (AC-9). */}
          <Card variant="elevated" padding="md" className="flex flex-col gap-1.5">
            <h2 className="text-xs font-semibold uppercase tracking-widest text-zinc-500">
              {t('dashboard.maker.orderDetail.contact.heading')}
            </h2>
            <div className="divide-y divide-zinc-800">
              <p className="py-2.5 text-sm text-zinc-100">{detail.customerContactName}</p>
              <div className="py-2.5">
                <a
                  href={`tel:${detail.customerContactPhone}`}
                  className="text-sm font-medium text-brand-400 transition-colors hover:text-brand-300"
                >
                  {detail.customerContactPhone}
                </a>
              </div>
            </div>
          </Card>

          {detail.attachments.length > 0 ? (
            <Card variant="elevated" padding="md" className="flex flex-col gap-1.5">
              <h2 className="text-xs font-semibold uppercase tracking-widest text-zinc-500">
                {t('dashboard.maker.orderDetail.attachments.heading')}
              </h2>
              <ul className="divide-y divide-zinc-800">
                {detail.attachments.map((attachment) => (
                  <li
                    key={attachment.id}
                    className="flex flex-wrap items-center justify-between gap-3 py-2.5"
                  >
                    <div className="flex min-w-0 items-center gap-2">
                      <Icon name="file" size={16} className="shrink-0 text-zinc-500" />
                      <span className="truncate text-sm text-zinc-100">{attachment.filename}</span>
                      <span className="shrink-0 text-xs text-zinc-500">
                        {formatFileSize(attachment.sizeBytes)}
                      </span>
                    </div>
                    <FileDownloadButton
                      path={attachment.downloadUrl}
                      filename={attachment.filename}
                      label={t('dashboard.maker.orderDetail.attachments.download')}
                    />
                  </li>
                ))}
              </ul>
            </Card>
          ) : null}

          {hasValue(detail.invoicePdfUrl) ? (
            <Card
              variant="elevated"
              padding="md"
              className="flex flex-wrap items-center justify-between gap-3"
            >
              <h2 className="text-xs font-semibold uppercase tracking-widest text-zinc-500">
                {t('dashboard.maker.orderDetail.invoice.heading')}
              </h2>
              <FileDownloadButton
                path={detail.invoicePdfUrl}
                filename={`faktura-${detail.orderNumber}.pdf`}
                label={t('dashboard.maker.orderDetail.invoice.download')}
              />
            </Card>
          ) : null}
        </div>
      </div>
    </section>
  );
}

/**
 * Payout-prominent money block (T-0087b §C): the maker's payout is the
 * headline figure; the customer-facing gross + components are the
 * supporting breakdown below it. All figures via `formatCzk` — no money
 * math client-side (the only arithmetic is the basis-point display
 * scaling, CLAUDE.md money rules).
 */
function PayoutBreakdown({ detail }: { readonly detail: MakerOrderDetail }) {
  const vatRate = new Intl.NumberFormat('cs-CZ', { maximumFractionDigits: 2 }).format(
    detail.vatRateBp / BASIS_POINTS_PER_PERCENT,
  );

  return (
    <Card variant="elevated" padding="md" className="flex flex-col gap-3">
      <div className="flex items-start justify-between gap-4">
        <h2 className="pt-1 text-xs font-semibold uppercase tracking-widest text-zinc-500">
          {t('dashboard.maker.orderDetail.payout.heading')}
        </h2>
        <p className="text-2xl font-bold text-brand-400 sm:text-3xl">
          {formatCzk(detail.makerPayoutAmountMinor, detail.currency)}
        </p>
      </div>
      <dl className="divide-y divide-zinc-800 border-t border-zinc-800">
        <div className="flex items-center justify-between gap-3 py-2.5">
          <dt className="text-sm text-zinc-400">{t('dashboard.maker.orderDetail.payout.product')}</dt>
          <dd className="text-right text-sm text-zinc-100">
            {formatCzk(detail.productPriceMinor, detail.currency)}
          </dd>
        </div>
        <div className="flex items-center justify-between gap-3 py-2.5">
          <dt className="text-sm text-zinc-400">{t('dashboard.maker.orderDetail.payout.shipping')}</dt>
          <dd className="text-right text-sm text-zinc-100">
            {formatCzk(detail.shippingPriceMinor, detail.currency)}
          </dd>
        </div>
        <div className="flex items-center justify-between gap-3 py-2.5">
          <dt className="text-sm text-zinc-400">
            {t('dashboard.maker.orderDetail.payout.vat', { rate: vatRate })}
          </dt>
          <dd className="text-right text-sm text-zinc-100">
            {formatCzk(detail.vatAmountMinor, detail.currency)}
          </dd>
        </div>
        <div className="flex items-center justify-between gap-3 py-2.5">
          <dt className="text-sm font-medium text-zinc-200">
            {t('dashboard.maker.orderDetail.payout.total')}
          </dt>
          <dd className="text-right text-sm font-semibold text-zinc-100">
            {formatCzk(detail.totalAmountMinor, detail.currency)}
          </dd>
        </div>
      </dl>
    </Card>
  );
}

function LoadErrorState({ orderId }: { readonly orderId: string }) {
  return (
    <section className="mx-auto flex max-w-3xl flex-col gap-6 px-4 py-10 sm:px-6 lg:px-8">
      <Alert variant="error">
        <p className="font-semibold">{t('dashboard.maker.orderDetail.loadError.title')}</p>
        <p className="mt-1">{t('dashboard.maker.orderDetail.loadError.body')}</p>
      </Alert>
      <div>
        <Link
          href={`/dashboard/maker/objednavky/${encodeURIComponent(orderId)}`}
          className="inline-flex items-center gap-1.5 text-sm text-zinc-400 transition-colors hover:text-zinc-200"
        >
          {t('dashboard.maker.orderDetail.loadError.retry')}
        </Link>
      </div>
    </section>
  );
}
