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
import {
  getReviewableOrders,
  getSubmittedReviews,
  type SubmittedReview as SubmittedReviewDto,
} from '@/lib/api-client-helpers/reviews-client';
import { formatFileSize } from '@/lib/format/file-size';
import { t } from '@/lib/i18n';
import { orderStateBadgeVariant, orderStateLabelKey } from '@/lib/orders/state-labels';
import { ORDER_ATTACHMENT_MAX_FILES } from '@/lib/utils/validation';
import { AttachmentManagerClient } from './attachment-manager-client';
import { DisputeEscalationClient } from './dispute-escalation-client';
import { FileDownloadButton, MarkDeliveredButton } from './order-actions-client';
import { OrderBreakdown, OrderPriceCards } from './order-breakdown';
import { OrderThreadClient } from './order-thread-client';
import { PayButtonClient } from './pay-button-client';
import { ReviewFormClient } from './review-form-client';
import { SubmittedReview } from './submitted-review';
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

/**
 * Quiet back affordance to the customer order list — the first element
 * on both order surfaces (PendingPayment and tracking), above the
 * title/status row.
 */
function BackToOrdersLink() {
  return (
    <Link
      href="/dashboard/zakaznik/objednavky"
      className="inline-flex items-center gap-1.5 text-sm text-zinc-400 transition-colors hover:text-zinc-200"
    >
      <Icon name="chevronLeft" size={16} />
      {t('common.back')}
    </Link>
  );
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
      <div>
        <BackToOrdersLink />
      </div>

      {Number.isFinite(attachmentsFailed) && attachmentsFailed > 0 ? (
        <Alert variant="warning">
          {t('order.page.attachments.failedHandoffAlert', { count: attachmentsFailed })}
        </Alert>
      ) : null}

      <OrderBreakdown detail={detail} />

      <PayButtonClient orderId={detail.orderId} />

      <Card variant="elevated" padding="md">
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

/**
 * T-0145: mirrors the backend's Disputable allow-list (`Order.OpenDispute`
 * — Paid | Accepted | Shipped | Delivered per dispute.md §"Parenthesis-
 * state mechanics") plus `Disputed` itself, so the escalation surface
 * renders the read-only "already open" note instead of disappearing.
 */
const DISPUTABLE_STATES: ReadonlySet<OrderState> = new Set([
  OrderState.Paid,
  OrderState.Accepted,
  OrderState.Shipped,
  OrderState.Delivered,
  OrderState.Disputed,
]);

type ReviewState =
  | { readonly kind: 'submitted'; readonly review: SubmittedReviewDto }
  | { readonly kind: 'canReview' }
  | { readonly kind: 'none' };

/**
 * Resolve the review surface for this order from the T-0100 dashboard
 * endpoints (T-0115 §C fallback path — the contract exposes reviews via
 * `IReviewQueries`-backed reads, not a detail-DTO fold). Both SSR sibling
 * reads (submitted reviews + reviewable orders) are fetched in parallel by
 * the caller and passed in already-resolved; this stays a pure branch over
 * the two `Result`s. A fetch failure degrades to "no review block" (loudly
 * recoverable on the next `router.refresh()`, no mock). Eligibility stays
 * backend-authoritative — the page only reads the signals. The submitted
 * signal wins (a submitted review takes precedence over a stale reviewable
 * row).
 */
function resolveReviewState(
  orderId: string,
  submittedResult: Awaited<ReturnType<typeof getSubmittedReviews>>,
  reviewableResult: Awaited<ReturnType<typeof getReviewableOrders>>,
): ReviewState {
  if (submittedResult.success) {
    const mine = submittedResult.value.find((r) => r.orderId === orderId);
    if (mine) {
      return { kind: 'submitted', review: mine };
    }
  }

  if (reviewableResult.success && reviewableResult.value.some((o) => o.orderId === orderId)) {
    return { kind: 'canReview' };
  }

  return { kind: 'none' };
}

async function TrackingDetail({ detail }: { readonly detail: CustomerOrderDetail }) {
  // The three reads are mutually independent (messages, submitted reviews,
  // reviewable orders); run them in parallel so the SSR round-trips overlap
  // (Gate 8 — no serial waterfall) while keeping per-read degrade semantics.
  const [messagesResult, submittedResult, reviewableResult] = await Promise.all([
    getOrderMessages(detail.orderId, 1),
    getSubmittedReviews(),
    getReviewableOrders(),
  ]);

  const initialThreadPage: OrderMessagesPage = messagesResult.success
    ? toThreadMessagesPage(messagesResult.value)
    : { items: [], page: 1, totalCount: 0, hasNextPage: false };

  const reviewState = resolveReviewState(detail.orderId, submittedResult, reviewableResult);

  const shippingMethodLabel =
    detail.shippingMethod === ShippingMethod.PersonalPickup
      ? t('order.page.shippingMethod.personalPickup')
      : t('order.page.shippingMethod.zasilkovna');

  return (
    <section className="mx-auto max-w-6xl px-4 py-10 sm:px-6 lg:px-8">
      <div>
        <BackToOrdersLink />
      </div>

      <header className="mt-6 flex flex-col gap-2">
        <div className="flex flex-wrap items-center gap-3">
          <h1 className="text-shine text-3xl font-bold tracking-tight sm:text-4xl">
            {t('order.page.title', { orderNumber: detail.orderNumber })}
          </h1>
          <Badge variant={orderStateBadgeVariant(detail.state)}>
            {t(orderStateLabelKey(detail.state))}
          </Badge>
        </div>
        <p className="flex items-center gap-2 text-sm text-zinc-400">
          <Icon name="user" size={14} className="shrink-0 text-zinc-500" />
          {t('customer.orderDetail.makerLine', { name: detail.makerName })}
        </p>
        <p className="flex items-center gap-2 text-sm text-zinc-400">
          <Icon name="package" size={14} className="shrink-0 text-zinc-500" />
          {t('customer.orderDetail.productLine', {
            title: detail.productTitle ?? t('order.page.breakdown.customOrderFallback'),
          })}
        </p>
      </header>

      {/* Two lanes on desktop: status + communication left, money +
          shipping + documents in the right rail. Single stack on mobile. */}
      <div className="mt-8 flex flex-col gap-6 lg:grid lg:grid-cols-[minmax(0,1fr)_22rem] lg:items-start lg:gap-8">
        <div className="flex min-w-0 flex-col gap-6">
          <Card variant="elevated" padding="md">
            <OrderTimeline detail={detail} />
          </Card>

          {detail.state === OrderState.Shipped ? (
            <Card variant="elevated" padding="md">
              <MarkDeliveredButton orderId={detail.orderId} />
            </Card>
          ) : null}

          <Card variant="elevated" padding="md" className="flex flex-col gap-4">
            <OrderThreadClient
              orderId={detail.orderId}
              initialPage={initialThreadPage}
              canPost={detail.state !== OrderState.PendingPayment}
            />

            {/* T-0145: "Reklamovat" lands HERE (in the thread), not in a
                standalone form — the escalate action + category selector are
                scoped to the Disputable allow-list mirrored from
                Order.OpenDispute (Paid | Accepted | Shipped | Delivered),
                plus Disputed itself (renders the read-only note). */}
            {DISPUTABLE_STATES.has(detail.state) ? (
              <DisputeEscalationClient
                orderId={detail.orderId}
                state={detail.state}
                deliveredAt={detail.deliveredAt}
              />
            ) : null}
          </Card>

          {/* Terminal post-delivery action — renders last, after the thread.
              Three states from the backend signals: form / read-only / nothing. */}
          {reviewState.kind === 'canReview' ? (
            <ReviewFormClient orderId={detail.orderId} />
          ) : reviewState.kind === 'submitted' ? (
            <SubmittedReview review={reviewState.review} />
          ) : null}
        </div>

        <div className="flex min-w-0 flex-col gap-6 lg:sticky lg:top-24">
          <OrderPriceCards detail={detail} />

          <Card variant="elevated" padding="md" className="flex flex-col gap-2">
            <h2 className="flex items-center gap-2 text-xs font-semibold tracking-widest text-zinc-500 uppercase">
              <Icon name="truck" size={14} className="shrink-0" />
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
            <Card variant="elevated" padding="md" className="flex flex-col gap-3">
              <h2 className="flex items-center gap-2 text-xs font-semibold tracking-widest text-zinc-500 uppercase">
                <Icon name="file" size={14} className="shrink-0" />
                {t('customer.orderDetail.attachments.heading')}
              </h2>
              <ul className="flex flex-col gap-2">
                {detail.attachments.map((attachment) => (
                  <li
                    key={attachment.id}
                    className="flex items-center justify-between gap-3 rounded-xl border border-zinc-800 bg-zinc-900/60 px-4 py-2.5"
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
            <Card
              variant="elevated"
              padding="md"
              className="flex flex-wrap items-center justify-between gap-3"
            >
              <h2 className="flex items-center gap-2 text-xs font-semibold tracking-widest text-zinc-500 uppercase">
                <Icon name="receipt" size={14} className="shrink-0" />
                {t('customer.orderDetail.invoice.heading')}
              </h2>
              <FileDownloadButton
                path={detail.invoicePdfUrl}
                filename={`faktura-${detail.orderNumber}.pdf`}
                label={t('customer.orderDetail.invoice.download')}
              />
            </Card>
          ) : null}

          {hasUrl(detail.returnLabelUrl) ? (
            <Card
              variant="elevated"
              padding="md"
              className="flex flex-wrap items-center justify-between gap-3"
            >
              <h2 className="flex items-center gap-2 text-xs font-semibold tracking-widest text-zinc-500 uppercase">
                <Icon name="package" size={14} className="shrink-0" />
                {t('customer.orderDetail.returnLabel.heading')}
              </h2>
              <FileDownloadButton
                path={detail.returnLabelUrl}
                filename={`vratkovy-stitek-${detail.orderNumber}.pdf`}
                label={t('customer.orderDetail.returnLabel.download')}
              />
            </Card>
          ) : null}
        </div>
      </div>
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
          className="inline-flex items-center gap-2 text-sm font-medium text-zinc-400 transition-colors hover:text-zinc-200"
        >
          <Icon name="refresh" size={14} className="shrink-0" />
          {t('order.page.loadErrorRetry')}
        </Link>
      </div>
    </section>
  );
}
