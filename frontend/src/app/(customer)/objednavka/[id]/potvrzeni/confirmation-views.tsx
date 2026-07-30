import Link from 'next/link';
import { Card } from '@/components/ui/card';
import { Icon } from '@/components/ui/icon';
import { Spinner } from '@/components/ui/spinner';
import { t } from '@/lib/i18n';

/**
 * Presentational frames for the payment confirmation page (T-0085).
 * Each frame is a pure function of props, shared by the page's SSR
 * short-circuits and the poller's client transitions so both paths
 * render pixel-identical views. No data fetching, no state.
 */

const PRIMARY_CTA_CLASSES =
  'inline-flex items-center justify-center gap-2 rounded-xl bg-brand-400 px-5 py-2.5 text-sm font-semibold text-zinc-950 transition-all duration-200 hover:bg-brand-300';
const SECONDARY_CTA_CLASSES =
  'inline-flex items-center justify-center gap-2 rounded-xl border border-zinc-700 px-5 py-2.5 text-sm font-semibold text-zinc-300 transition-colors hover:bg-zinc-800';

function orderDetailHref(orderId: string): string {
  return `/objednavka/${encodeURIComponent(orderId)}`;
}

/** Optimistic "Děkujeme — ověřujeme platbu" frame (poll in progress). */
export function VerifyingView() {
  return (
    <Card variant="elevated" padding="lg" className="flex flex-col items-center gap-5 text-center">
      <span className="icon-tile h-16 w-16" aria-hidden="true">
        <Spinner size="lg" />
      </span>
      <h1 className="text-shine text-2xl font-semibold">
        {t('checkout.confirm.verifying.title')}
      </h1>
      <p className="max-w-md text-sm leading-relaxed text-zinc-400">
        {t('checkout.confirm.verifying.subtitle')}
      </p>
    </Card>
  );
}

/** Success frame — granted exclusively by the backend-read Paid state. */
export function SuccessView({
  orderId,
  orderNumber,
}: {
  readonly orderId: string;
  readonly orderNumber: string;
}) {
  return (
    <Card variant="accent" padding="lg" className="flex flex-col items-center gap-6 text-center">
      <span className="icon-tile h-16 w-16" aria-hidden="true">
        <Icon name="checkCircle" size={30} className="text-emerald-400" />
      </span>
      <div className="flex flex-col gap-1.5">
        <h1 className="text-shine text-2xl font-semibold">
          {t('checkout.confirm.success.title')}
        </h1>
        <p className="text-sm text-zinc-400">
          {t('checkout.confirm.success.orderNumber', { orderNumber })}
        </p>
      </div>
      <div className="flex w-full max-w-sm flex-col gap-3 rounded-xl border border-zinc-800 bg-zinc-900/60 p-4 text-left">
        <h2 className="text-xs font-semibold tracking-widest text-zinc-500 uppercase">
          {t('checkout.confirm.success.whatNext')}
        </h2>
        <ol className="flex list-decimal flex-col gap-1.5 pl-5 text-sm text-zinc-300">
          <li>{t('checkout.confirm.success.step1')}</li>
          <li>{t('checkout.confirm.success.step2')}</li>
          <li>{t('checkout.confirm.success.step3')}</li>
        </ol>
      </div>
      <div className="flex flex-col gap-3 sm:flex-row">
        <Link href={orderDetailHref(orderId)} className={PRIMARY_CTA_CLASSES}>
          {t('checkout.confirm.success.detailCta')}
          <Icon name="arrowRight" size={16} />
        </Link>
        <Link href="/katalog" className={SECONDARY_CTA_CLASSES}>
          {t('checkout.confirm.success.catalogCta')}
        </Link>
      </div>
    </Card>
  );
}

/** ~30s cap reached — verification continues; the T-0067 email confirms. */
export function CapReachedView({ orderId }: { readonly orderId: string }) {
  return (
    <Card variant="elevated" padding="lg" className="flex flex-col items-center gap-5 text-center">
      <span className="icon-tile h-16 w-16" aria-hidden="true">
        <Icon name="clock" size={30} className="text-amber-400" />
      </span>
      <h1 className="text-shine text-2xl font-semibold">{t('checkout.confirm.pendingTitle')}</h1>
      <p className="max-w-md text-sm leading-relaxed text-zinc-400">
        {t('checkout.confirm.pendingEmailNote')}
      </p>
      <Link href={orderDetailHref(orderId)} className={SECONDARY_CTA_CLASSES}>
        {t('checkout.confirm.pendingDetailLink')}
      </Link>
    </Card>
  );
}

/** Cancelled/failed gateway flow — retry lives on the order page (T-0084b). */
export function FailureView({ orderId }: { readonly orderId: string }) {
  return (
    <Card variant="elevated" padding="lg" className="flex flex-col items-center gap-5 text-center">
      <span className="icon-tile h-16 w-16" aria-hidden="true">
        <Icon name="xCircle" size={30} className="text-red-400" />
      </span>
      <h1 className="text-shine text-2xl font-semibold">{t('checkout.confirm.failed.title')}</h1>
      <p className="max-w-md text-sm leading-relaxed text-zinc-400">
        {t('checkout.confirm.failed.heldNote')}
      </p>
      <div className="flex flex-col gap-3 sm:flex-row">
        <Link href={orderDetailHref(orderId)} className={PRIMARY_CTA_CLASSES}>
          {t('checkout.confirm.failed.retryCta')}
          <Icon name="arrowRight" size={16} />
        </Link>
        <Link href="/katalog" className={SECONDARY_CTA_CLASSES}>
          {t('checkout.confirm.success.catalogCta')}
        </Link>
      </div>
    </Card>
  );
}
