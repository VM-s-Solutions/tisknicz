import Link from 'next/link';
import type { Metadata } from 'next';
import { notFound, redirect } from 'next/navigation';
import { Badge } from '@/components/ui/badge';
import { Card } from '@/components/ui/card';
import { Icon } from '@/components/ui/icon';
import {
  getMakerPayoutDetail,
  type MakerPayoutDetail,
  PayoutBatchState,
} from '@/lib/api-client-helpers/payouts-client';
import { t } from '@/lib/i18n';
import type { MessageKey } from '@/lib/i18n';
import { formatCzk } from '@/lib/money/formatter';
import { formatDate } from '@/lib/utils/dates';
import { FeeInvoiceDownload } from './fee-invoice-download';

/**
 * Maker payout-batch detail (T-0116, US-maker-0012 AC-3). Server Component:
 * fetches the per-order breakdown with SSR cookie forwarding; a missing /
 * foreign batchId renders `notFound()` (one shape, no IDOR oracle). Renders
 * the batch summary + the breakdown table (order #, product price, shipping
 * if present, platform fee, net payout — all via formatCzk, no client math)
 * + the Fee-invoice download button (the only client island) when
 * `feeInvoiceId` is set. NO CSV anywhere.
 */

const ROUTE_PATH = '/dashboard/maker/vyplaty';

export const dynamic = 'force-dynamic';

export function generateMetadata(): Metadata {
  return {
    title: t('dashboard.maker.payoutDetail.metadata.title'),
    description: t('dashboard.maker.payoutDetail.metadata.description'),
  };
}

interface PageProps {
  readonly params: Promise<{ readonly batchId: string }>;
}

function payoutStateBadgeVariant(state: PayoutBatchState): 'success' | 'warning' {
  return state === PayoutBatchState.Completed ? 'success' : 'warning';
}

function payoutStateLabelKey(state: PayoutBatchState): MessageKey {
  return state === PayoutBatchState.Completed
    ? 'dashboard.maker.payouts.state.completed'
    : 'dashboard.maker.payouts.state.processing';
}

export default async function MakerPayoutDetailPage({ params }: PageProps) {
  const { batchId } = await params;
  const result = await getMakerPayoutDetail(batchId);

  if (!result.success) {
    if (result.error.type === 'Unauthorized') {
      redirect(`/login?redirect=${encodeURIComponent(`${ROUTE_PATH}/${batchId}`)}`);
    }
    // NotFound (foreign + unknown — one shape) → Next.js 404.
    notFound();
  }

  const detail = result.value;

  return (
    <section className="bg-surface-primary py-12 lg:py-16">
      <div className="mx-auto flex max-w-4xl flex-col gap-8 px-4 sm:px-6 lg:px-8">
        <Link
          href={ROUTE_PATH}
          className="inline-flex w-fit items-center gap-2 text-sm font-medium text-zinc-400 transition-colors hover:text-white"
        >
          <Icon name="arrowLeft" size={16} />
          {t('dashboard.maker.payoutDetail.backToList')}
        </Link>

        <PayoutSummary detail={detail} />
        <PayoutBreakdown detail={detail} />
      </div>
    </section>
  );
}

function PayoutSummary({ detail }: { readonly detail: MakerPayoutDetail }) {
  const dateLabel = detail.completedAt
    ? formatDate(detail.completedAt)
    : t('dashboard.maker.payouts.datePlaceholder');

  return (
    <Card padding="lg" className="flex flex-col gap-5">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-2xl font-bold text-white">
          {t('dashboard.maker.payoutDetail.summary.heading')}
        </h1>
        <Badge variant={payoutStateBadgeVariant(detail.state)}>
          {t(payoutStateLabelKey(detail.state))}
        </Badge>
      </div>
      <dl className="grid grid-cols-1 gap-4 sm:grid-cols-2">
        <SummaryItem labelKey="dashboard.maker.payoutDetail.summary.number" value={detail.batchNumber} />
        <SummaryItem
          labelKey="dashboard.maker.payoutDetail.summary.total"
          value={formatCzk(detail.makerTotalPaidMinor, detail.currency)}
        />
        <SummaryItem
          labelKey="dashboard.maker.payoutDetail.summary.orders"
          value={String(detail.orders.length)}
        />
        <SummaryItem labelKey="dashboard.maker.payoutDetail.summary.date" value={dateLabel} />
      </dl>
      {detail.feeInvoiceId ? (
        <FeeInvoiceDownload feeInvoiceId={detail.feeInvoiceId} batchNumber={detail.batchNumber} />
      ) : null}
    </Card>
  );
}

function SummaryItem({ labelKey, value }: { readonly labelKey: MessageKey; readonly value: string }) {
  return (
    <div className="flex flex-col gap-1">
      <dt className="text-xs font-semibold uppercase tracking-widest text-zinc-500">{t(labelKey)}</dt>
      <dd className="text-sm font-medium text-zinc-100">{value}</dd>
    </div>
  );
}

const BREAKDOWN_GRID =
  'md:grid md:grid-cols-[minmax(0,1fr)_7rem_7rem_7rem_7rem] md:items-center md:gap-4';

function PayoutBreakdown({ detail }: { readonly detail: MakerPayoutDetail }) {
  return (
    <div className="flex flex-col gap-4">
      <h2 className="text-lg font-semibold text-white">
        {t('dashboard.maker.payoutDetail.breakdown.heading')}
      </h2>
      <div className="flex flex-col gap-3 md:gap-0">
        <div
          className={`hidden border-b border-zinc-800 px-4 pb-3 text-xs font-semibold uppercase tracking-widest text-zinc-500 ${BREAKDOWN_GRID}`}
        >
          <span>{t('dashboard.maker.payoutDetail.breakdown.order')}</span>
          <span className="md:text-right">
            {t('dashboard.maker.payoutDetail.breakdown.productPrice')}
          </span>
          <span className="md:text-right">
            {t('dashboard.maker.payoutDetail.breakdown.shipping')}
          </span>
          <span className="md:text-right">
            {t('dashboard.maker.payoutDetail.breakdown.platformFee')}
          </span>
          <span className="md:text-right">
            {t('dashboard.maker.payoutDetail.breakdown.netPayout')}
          </span>
        </div>
        {detail.orders.map((line) => (
          <div
            key={line.orderId}
            className={`flex flex-col gap-2 rounded-2xl border border-zinc-800 bg-surface-card p-4 md:rounded-none md:border-x-0 md:border-t-0 md:border-b md:bg-transparent ${BREAKDOWN_GRID}`}
          >
            <span className="text-sm font-semibold text-zinc-100">{line.orderNumber}</span>
            <BreakdownCell
              labelKey="dashboard.maker.payoutDetail.breakdown.productPrice"
              value={formatCzk(line.productPriceMinor, line.currency)}
            />
            <BreakdownCell
              labelKey="dashboard.maker.payoutDetail.breakdown.shipping"
              value={formatCzk(line.shippingPriceMinor, line.currency)}
            />
            <BreakdownCell
              labelKey="dashboard.maker.payoutDetail.breakdown.platformFee"
              value={formatCzk(line.platformFeeAmountMinor, line.currency)}
            />
            <BreakdownCell
              labelKey="dashboard.maker.payoutDetail.breakdown.netPayout"
              value={formatCzk(line.makerPayoutAmountMinor, line.currency)}
              emphasised
            />
          </div>
        ))}
      </div>
    </div>
  );
}

function BreakdownCell({
  labelKey,
  value,
  emphasised = false,
}: {
  readonly labelKey: MessageKey;
  readonly value: string;
  readonly emphasised?: boolean;
}) {
  return (
    <span
      className={`flex items-center justify-between gap-3 text-sm md:justify-end md:text-right ${
        emphasised ? 'font-semibold text-zinc-100' : 'text-zinc-300'
      }`}
    >
      <span className="text-xs text-zinc-500 md:hidden">{t(labelKey)}</span>
      <span>{value}</span>
    </span>
  );
}
