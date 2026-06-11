import Link from 'next/link';
import type { Metadata } from 'next';
import { notFound, redirect } from 'next/navigation';
import { Alert } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Card } from '@/components/ui/card';
import { Icon } from '@/components/ui/icon';
import {
  getCustomerOrderDetail,
  OrderState,
  type CustomerOrderDetail,
} from '@/lib/api-client-helpers/orders-client';
import { t } from '@/lib/i18n';
import { orderStateLabelKey } from '@/lib/orders/state-labels';
import { ORDER_ATTACHMENT_MAX_FILES } from '@/lib/utils/validation';
import { AttachmentManagerClient } from './attachment-manager-client';
import { OrderBreakdown } from './order-breakdown';
import { PayButtonClient } from './pay-button-client';

/**
 * Pre-payment order page at /objednavka/[id] (T-0084b,
 * US-customer-0010 AC-2/AC-3). Server Component: the SSR detail fetch
 * forwards the customer audience cookie (patterns.md B.14). The page is
 * intentionally PendingPayment-shaped — any other state renders a
 * minimal banner until T-0086b ships the full tracking view (loudly
 * incomplete per the CLAUDE.md no-mocks rule, by design). No
 * payment-session request fires on render — the session is created
 * exclusively by the "Zaplatit" click (Q3 lock).
 */

export const metadata: Metadata = {
  title: t('order.page.metadata.title'),
};

// Always render fresh — the order state changes underneath this page
// (webhook, auto-cancel) and the pay CTA must reflect it.
export const dynamic = 'force-dynamic';

interface PageProps {
  readonly params: Promise<{ id: string }>;
  readonly searchParams: Promise<Record<string, string | string[] | undefined>>;
}

function readString(value: string | string[] | undefined): string {
  if (Array.isArray(value)) return value[0] ?? '';
  return value ?? '';
}

export default async function OrderPage({ params, searchParams }: PageProps) {
  const { id } = await params;
  const sp = await searchParams;

  const result = await getCustomerOrderDetail(id);
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
    return <StateBanner detail={detail} />;
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
 * Minimal non-PendingPayment banner — replaced by T-0086b's tracking
 * view in the order-dashboards bundle. No pay CTA, no upload UI (even
 * though T-0064 allows uploads in Paid/Accepted, that surface belongs
 * to the tracking view).
 */
function StateBanner({ detail }: { readonly detail: CustomerOrderDetail }) {
  return (
    <section className="mx-auto flex max-w-3xl flex-col gap-6 px-4 py-10 sm:px-6 lg:px-8">
      <header className="flex flex-wrap items-center gap-3">
        <h1 className="text-3xl font-bold tracking-tight text-white sm:text-4xl">
          {t('order.page.title', { orderNumber: detail.orderNumber })}
        </h1>
        <Badge variant="default">{t(orderStateLabelKey(detail.state))}</Badge>
      </header>
      <Card padding="lg" className="flex flex-col gap-4">
        <p className="text-sm text-zinc-300">{t('order.page.banner.detailComing')}</p>
        <div>
          <Link
            href="/katalog"
            className="inline-flex items-center gap-2 text-sm font-medium text-zinc-400 transition-colors hover:text-white"
          >
            <Icon name="arrowLeft" size={16} />
            {t('order.page.banner.backToCatalog')}
          </Link>
        </div>
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
