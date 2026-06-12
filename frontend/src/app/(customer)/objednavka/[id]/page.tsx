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
  type CustomerOrderDetail,
  getCustomerOrderDetail,
  getOrderMessages,
  OrderState,
  ShippingMethod,
} from '@/lib/api-client-helpers/orders-client';
import { formatFileSize } from '@/lib/format/file-size';
import { t } from '@/lib/i18n';
import { orderStateBadgeVariant, orderStateLabelKey } from '@/lib/orders/state-labels';
import { ORDER_ATTACHMENT_MAX_FILES } from '@/lib/utils/validation';
import { AttachmentManagerClient } from './attachment-manager-client';
import { FileDownloadButton, MarkDeliveredButton } from './order-actions-client';
import { OrderBreakdown, OrderPriceCards } from './order-breakdown';
import { OrderThreadClient } from './order-thread-client';
import { PayButtonClient } from './pay-button-client';
import { toThreadMessagesPage } from './thread-mapping';
import { OrderTimeline } from './timeline';

/**
 * Order page at /objednavka/[id] — ONE route for the order's whole life
 * (T-0067/T-0076 emails pre-bake this URL). One state branch:
 * `PendingPayment` renders T-0084b's payment-retry surface unchanged;
 * every later state renders the T-0086b tracking detail (header,
 * timeline, breakdown, shipping/tracking, attachments, invoice,
 * message thread). Server Component throughout — the SSR fetches
 * forward the customer audience cookie (patterns.md B.14); mutations
 * re-sync via `router.refresh()` in the client islands (Q5 lock).
 */

// Always render fresh — the order state changes underneath this page
// (webhook, auto-cancel, maker transitions) and both surfaces must
// reflect it.
export const dynamic = 'force-dynamic';

/**
 * Per-request memo (Gate 8 fold): `generateMetadata` and the page body
 * both need the detail, and `apiFetch` composes a fresh
 * `AbortSignal.timeout` per call which defeats Next's fetch
 * memoization — without `cache()` every detail view issues two
 * identical backend GETs. Scope is one server request; a
 * `router.refresh()` is a new request and re-fetches (Q5 lock intact).
 */
const getOrderDetail = cache(getCustomerOrderDetail);

interface PageProps {
  readonly params: Promise<{ id: string }>;
  readonly searchParams: Promise<Record<string, string | string[] | undefined>>;
}

export async function generateMetadata({ params }: PageProps): Promise<Metadata> {
  const { id } = await params;
  const result = await getOrderDetail(id);
  if (!result.success) {
    // §B.9: branch the title ONLY on NotFound — transient errors keep
    // the brand title so a backend blip never signals "gone".
    const title =
      result.error.type === 'NotFound'
        ? `${t('order.page.notFound.title')} — ${t('common.app_name')}`
        : t('order.page.metadata.title');
    return { title };
  }
  return { title: t('order.page.metadata.title') };
}

function readString(value: string | string[] | undefined): string {
  if (Array.isArray(value)) return value[0] ?? '';
  return value ?? '';
}

export default async function OrderPage({ params, searchParams }: PageProps) {
  const { id } = await params;
  const sp = await searchParams;

  const result = await getOrderDetail(id);
  if (!result.success) {
    // Foreign order = 404 from the backend (IDOR-resistant,
    // US-customer-0012 AC-3) — same render as unknown id.
    if (result.error.type === 'NotFound') {
      notFound();
    }
    if (result.error.type === 'Unauthorized') {
      // The login page serves at /login — the (auth) route group adds
      // no URL segment.
      redirect(
        `/login?redirect=${encodeURIComponent(`/objednavka/${encodeURIComponent(id)}`)}`,
      );
    }
    return <LoadErrorState orderId={id} />;
  }
  const detail = result.value;

  if (detail.state !== OrderState.PendingPayment) {
    return <TrackingDetail detail={detail} />;
  }

  // ?attachmentsFailed=<n> — presentational handoff from the T-0084a
  // form (uploads that failed after a successful create). Clamped to
  // the T-0064 per-order cap as a defensive upper bound for crafted
  // URLs (review N-2); Math.min propagates NaN, so the render guard
  // below still rejects non-numeric input.
  const attachmentsFailed = Math.min(
    Number.parseInt(readString(sp.attachmentsFailed), 10),
    ORDER_ATTACHMENT_MAX_FILES,
  );

  return (
    <section className="mx-auto flex max-w-3xl flex-col gap-6 px-4 py-10 sm:px-6 lg:px-8">
      {Number.isFinite(attachmentsFailed) && attachmentsFailed > 0 ? (
        <Alert variant="warning">
          {t('order.page.attachments.failedHandoffAlert', { count: attachmentsFailed })}
        </Alert>
      ) : null}

      <OrderBreakdown detail={detail} />

      <PayButtonClient orderId={detail.orderId} />

      <Card padding="md">
        <AttachmentManagerClient
          orderId={detail.orderId}
          initialAttachments={detail.attachments}
        />
      </Card>
    </section>
  );
}

/**
 * Post-payment tracking surface (T-0086b, Paid/Accepted/Shipped/
 * Delivered/Completed/Cancelled/Refunded/Disputed). Messages page 1 is
 * SSR-prefetched alongside the detail so the thread paints
 * data-complete (B.1); a prefetch failure degrades to an empty initial
 * page — the thread's first poll recovers (loudly visible, no mock).
 */
/**
 * Wire-honest presence check for the nullable URL fields: the generated
 * types declare `string | undefined`, but ASP.NET serialises absent
 * values as JSON `null` — `typeof` covers both shapes.
 */
function hasUrl(value: string | undefined): value is string {
  return typeof value === 'string' && value !== '';
}

async function TrackingDetail({ detail }: { readonly detail: CustomerOrderDetail }) {
  const messagesResult = await getOrderMessages(detail.orderId, 1);
  const initialThreadPage: OrderMessagesPage = messagesResult.success
    ? toThreadMessagesPage(messagesResult.value)
    : { items: [], page: 1, totalCount: 0, hasNextPage: false };

  const shippingMethodLabel =
    detail.shippingMethod === ShippingMethod.PersonalPickup
      ? t('order.page.shippingMethod.personalPickup')
      : t('order.page.shippingMethod.zasilkovna');

  return (
    <section className="mx-auto flex max-w-3xl flex-col gap-6 px-4 py-10 sm:px-6 lg:px-8">
      <header className="flex flex-col gap-2">
        <div className="flex flex-wrap items-center gap-3">
          <h1 className="text-3xl font-bold tracking-tight text-white sm:text-4xl">
            {t('order.page.title', { orderNumber: detail.orderNumber })}
          </h1>
          <Badge variant={orderStateBadgeVariant(detail.state)}>
            {t(orderStateLabelKey(detail.state))}
          </Badge>
        </div>
        <p className="text-sm text-zinc-400">
          {t('customer.orderDetail.makerLine', { name: detail.makerName })}
        </p>
        <p className="text-sm text-zinc-400">
          {t('customer.orderDetail.productLine', {
            title: detail.productTitle ?? t('order.page.breakdown.customOrderFallback'),
          })}
        </p>
      </header>

      <Card padding="md">
        <OrderTimeline detail={detail} />
      </Card>

      {detail.state === OrderState.Shipped ? (
        <MarkDeliveredButton orderId={detail.orderId} />
      ) : null}

      <OrderPriceCards detail={detail} />

      <Card padding="md" className="flex flex-col gap-2">
        <h2 className="text-sm font-semibold text-zinc-400">
          {t('customer.orderDetail.shipping.heading')}
        </h2>
        <p className="text-sm text-zinc-200">{shippingMethodLabel}</p>
        {hasUrl(detail.shippingCarrierTrackingUrl) ? (
          <a
            href={detail.shippingCarrierTrackingUrl}
            target="_blank"
            rel="noopener noreferrer"
            className="inline-flex w-fit items-center gap-2 text-sm font-medium text-brand-400 transition-colors hover:text-brand-300"
          >
            <Icon name="truck" size={16} />
            {t('customer.orderDetail.shipping.trackingLink')}
          </a>
        ) : null}
      </Card>

      {detail.attachments.length > 0 ? (
        <Card padding="md" className="flex flex-col gap-3">
          <h2 className="text-sm font-semibold text-zinc-400">
            {t('customer.orderDetail.attachments.heading')}
          </h2>
          <ul className="flex flex-col gap-2">
            {detail.attachments.map((attachment) => (
              <li
                key={attachment.id}
                className="flex items-center justify-between gap-3 rounded-xl border border-zinc-800 bg-surface-elevated px-4 py-2.5"
              >
                <div className="flex min-w-0 items-center gap-2">
                  <Icon name="file" size={16} className="shrink-0 text-zinc-500" />
                  <span className="truncate text-sm text-zinc-200">{attachment.filename}</span>
                  <span className="shrink-0 text-xs text-zinc-500">
                    {formatFileSize(attachment.sizeBytes)}
                  </span>
                </div>
                <FileDownloadButton
                  path={attachment.downloadUrl}
                  filename={attachment.filename}
                  label={t('customer.orderDetail.attachments.download')}
                />
              </li>
            ))}
          </ul>
        </Card>
      ) : null}

      {hasUrl(detail.invoicePdfUrl) ? (
        <Card padding="md" className="flex items-center justify-between gap-3">
          <h2 className="text-sm font-semibold text-zinc-400">
            {t('customer.orderDetail.invoice.heading')}
          </h2>
          <FileDownloadButton
            path={detail.invoicePdfUrl}
            filename={`faktura-${detail.orderNumber}.pdf`}
            label={t('customer.orderDetail.invoice.download')}
          />
        </Card>
      ) : null}

      <Card padding="md">
        <OrderThreadClient
          orderId={detail.orderId}
          initialPage={initialThreadPage}
          canPost={detail.state !== OrderState.PendingPayment}
        />
      </Card>
    </section>
  );
}

function LoadErrorState({ orderId }: { readonly orderId: string }) {
  return (
    <section className="mx-auto flex max-w-3xl flex-col gap-6 px-4 py-10 sm:px-6 lg:px-8">
      <Alert variant="error">
        <p className="font-semibold">{t('order.page.loadError')}</p>
        <p className="mt-1">{t('order.page.loadErrorBody')}</p>
      </Alert>
      <div>
        <Link
          href={`/objednavka/${encodeURIComponent(orderId)}`}
          className="inline-flex items-center gap-2 text-sm font-medium text-zinc-400 transition-colors hover:text-white"
        >
          {t('order.page.loadErrorRetry')}
        </Link>
      </div>
    </section>
  );
}
