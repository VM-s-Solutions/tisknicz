import { Alert } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Card } from '@/components/ui/card';
import { Icon } from '@/components/ui/icon';
import {
  type CustomerOrderDetail,
  OrderState,
  ShippingMethod,
} from '@/lib/api-client-helpers/orders-client';
import { t } from '@/lib/i18n';
import { formatCzk } from '@/lib/money/formatter';
import { orderStateLabelKey } from '@/lib/orders/state-labels';
import { formatDateTime } from '@/lib/utils/dates';

/**
 * Order summary + money breakdown for /objednavka/[id] (T-0084b).
 * Server Component rendered purely from the T-0082 detail DTO — every
 * number comes from the backend; the only client-side arithmetic is
 * display scaling (`vatRateBp / 100`) and the 24h auto-cancel deadline,
 * a display-only mirror of the documented T-0083 rule (the backend job
 * stays authoritative).
 *
 * T-0086b split: <see cref="OrderPriceCards"/> (price + contact cards
 * only) is reused by the post-payment tracking branch, which composes
 * its own header so the timeline can sit between header and breakdown;
 * <see cref="OrderBreakdown"/> keeps the original header + cards shape
 * for the PendingPayment surface unchanged.
 */

const AUTO_CANCEL_WINDOW_MS = 24 * 60 * 60 * 1000;

/** Display scaling only — VAT rates travel as basis points (CLAUDE.md money rules). */
const BASIS_POINTS_PER_PERCENT = 100;

export function OrderBreakdown({ detail }: { readonly detail: CustomerOrderDetail }) {
  return (
    <div className="flex flex-col gap-6">
      <header className="flex flex-wrap items-center gap-3">
        <h1 className="text-shine text-3xl font-bold tracking-tight sm:text-4xl">
          {t('order.page.title', { orderNumber: detail.orderNumber })}
        </h1>
        <Badge variant={detail.state === OrderState.PendingPayment ? 'warning' : 'default'}>
          {t(orderStateLabelKey(detail.state))}
        </Badge>
      </header>

      {detail.state === OrderState.PendingPayment ? (
        <Alert variant="warning">
          {t('order.page.expiresNotice', {
            deadline: formatDateTime(
              new Date(new Date(detail.createdAt).getTime() + AUTO_CANCEL_WINDOW_MS),
            ),
          })}
        </Alert>
      ) : null}

      <OrderPriceCards detail={detail} />
    </div>
  );
}

/** Price-breakdown + contact cards only — shared by the PendingPayment and tracking surfaces. */
export function OrderPriceCards({ detail }: { readonly detail: CustomerOrderDetail }) {
  const vatRate = new Intl.NumberFormat('cs-CZ', { maximumFractionDigits: 2 }).format(
    detail.vatRateBp / BASIS_POINTS_PER_PERCENT,
  );
  const shippingMethodLabel =
    detail.shippingMethod === ShippingMethod.PersonalPickup
      ? t('order.page.shippingMethod.personalPickup')
      : t('order.page.shippingMethod.zasilkovna');

  return (
    <div className="flex flex-col gap-6">
      <Card variant="elevated" padding="md">
        {/* iOS grouped list — hairline-divided key-value rows, total as
            the emphasised last row. */}
        <dl className="divide-y divide-zinc-800">
          <div className="flex items-center justify-between gap-3 pb-2.5">
            <dt className="text-sm text-zinc-400">
              {t('order.page.breakdown.product')}
              <span className="mt-0.5 block text-zinc-200">
                {detail.productTitle ?? t('order.page.breakdown.customOrderFallback')}
              </span>
            </dt>
            <dd className="shrink-0 text-right text-sm text-zinc-100">
              {formatCzk(detail.productPriceMinor, detail.currency)}
            </dd>
          </div>
          <div className="flex items-center justify-between gap-3 py-2.5">
            <dt className="text-sm text-zinc-400">
              {t('order.page.breakdown.shipping')}
              <span className="mt-0.5 block text-zinc-200">{shippingMethodLabel}</span>
            </dt>
            <dd className="shrink-0 text-right text-sm text-zinc-100">
              {formatCzk(detail.shippingPriceMinor, detail.currency)}
            </dd>
          </div>
          <div className="flex items-center justify-between gap-3 py-2.5">
            <dt className="text-sm text-zinc-400">
              {t('order.page.breakdown.vat', { rate: vatRate })}
            </dt>
            <dd className="shrink-0 text-right text-sm text-zinc-100">
              {formatCzk(detail.vatAmountMinor, detail.currency)}
            </dd>
          </div>
          <div className="flex items-center justify-between gap-3 pt-3">
            <dt className="text-base font-semibold text-zinc-50">
              {t('order.page.breakdown.total')}
            </dt>
            <dd className="shrink-0 text-right text-xl font-bold text-brand-400">
              {formatCzk(detail.totalAmountMinor, detail.currency)}
            </dd>
          </div>
        </dl>
      </Card>

      <Card variant="elevated" padding="md" className="flex flex-col gap-2">
        <h2 className="flex items-center gap-2 text-xs font-semibold tracking-widest text-zinc-500 uppercase">
          <Icon name="user" size={14} className="shrink-0" />
          {t('order.page.breakdown.contact')}
        </h2>
        <p className="text-sm text-zinc-200">{detail.contactName}</p>
        <p className="text-sm text-zinc-400">{detail.contactPhone}</p>
      </Card>
    </div>
  );
}
