import Link from 'next/link';
import type { Metadata } from 'next';
import { notFound, redirect } from 'next/navigation';
import { PageHeader } from '@/components/shared/page-header';
import { Alert } from '@/components/ui/alert';
import { EmptyState } from '@/components/ui/empty-state';
import { Icon } from '@/components/ui/icon';
import { getMakerBySlug, getProductById } from '@/lib/api-client-helpers/catalog';
import { getMyProfile } from '@/lib/api-client-helpers/profile';
import { getWidgetConfig } from '@/lib/api-client-helpers/shipping';
import { getDisplaySession } from '@/lib/auth/display-session';
import { t } from '@/lib/i18n';
import { OrderFormClient } from './order-form-client';
import { OrderSummary } from './order-summary';

/**
 * Checkout order form at /objednavka?productId=… (T-0084a,
 * US-customer-0010/0011). Server Component: SSR-fetches the customer
 * profile (auth gate — the cookie-forwarded fetch per patterns.md B.14
 * detects the unauthenticated case), the public product detail, the
 * maker profile (personal-pickup gate) and the Packeta widget config,
 * then renders the sticky summary + the client form. No client-side
 * data fetch fires on initial render (AC-1).
 */

export const metadata: Metadata = {
  title: t('checkout.metadata.title'),
};

// Always render fresh — the form prefills from the session profile and
// the product/maker state gates ordering.
export const dynamic = 'force-dynamic';

interface PageProps {
  readonly searchParams: Promise<Record<string, string | string[] | undefined>>;
}

function readString(value: string | string[] | undefined): string {
  if (Array.isArray(value)) return value[0] ?? '';
  return value ?? '';
}

export default async function CheckoutPage({ searchParams }: PageProps) {
  const sp = await searchParams;
  const productId = readString(sp.productId).trim();

  // Entry guard 1 — missing/blank productId → invalid-link state.
  if (productId === '') {
    return <InvalidLinkState />;
  }

  // SSR fetch batch 1 (Gate 8 fold MEDIUM-1) — profile, product and
  // widget config are mutually independent, so they fire in parallel;
  // only the maker fetch (batch 2 below) needs the product's makerSlug.
  // Trade-off: an unauthenticated visitor wastes the product/widget
  // fetches before the redirect — cheap, both endpoints are anonymous
  // and widget-config is Cache-Control 1h. The guards below run in the
  // original order, so the auth redirect still fires before anything
  // sensitive renders.
  const [profileResult, productResult, widgetResult] = await Promise.all([
    getMyProfile('customer'),
    getProductById(productId),
    getWidgetConfig(),
  ]);

  // Entry guard 2 — no customer session.
  //
  // A signed-in MAKER is not an anonymous visitor: `User.MatchesAudience`
  // binds their account to the maker audience, so their credentials can
  // never mint a customer JWT. Bouncing them to /login produced an
  // endless login screen (reported bug). They get an explanation here
  // instead; only genuinely anonymous visitors are redirected.
  if (!profileResult.success) {
    if (profileResult.error.type === 'Unauthorized') {
      const session = await getDisplaySession();
      // T-0171 (audit PUB-L6): widened from maker-only. ANY signed-in
      // non-customer audience (admin included) can never satisfy this
      // guard, so bouncing them to /login is the endless-login bug.
      if (session !== null && session.audience !== 'customer') {
        return <MakerAccountState email={session.email} productId={productId} />;
      }
      const target = `/objednavka?productId=${encodeURIComponent(productId)}`;
      redirect(`/login?redirect=${encodeURIComponent(target)}`);
    }
    return <LoadErrorState />;
  }

  // Entry guard 3 — unknown/inactive product → 404.
  if (!productResult.success) {
    if (productResult.error.type === 'NotFound') {
      notFound();
    }
    return <LoadErrorState />;
  }
  const product = productResult.value;

  // Entry guard 4 — on-request products have no order CTA
  // (US-customer-0009 AC-4): back to the product page.
  if (product.priceType === 'OnRequest') {
    redirect(`/produkt/${encodeURIComponent(productId)}`);
  }

  // Batch 2 — the maker profile drives the personal-pickup gate
  // (US-customer-0011 AC-3) and needs product.makerSlug. A failed
  // widget fetch degrades to a disabled Zásilkovna option (AC-6) —
  // pass null.
  const makerResult = await getMakerBySlug(product.makerSlug);
  if (!makerResult.success) {
    return <LoadErrorState />;
  }
  const maker = makerResult.value;
  const widgetConfig = widgetResult.success ? widgetResult.value : null;

  return (
    <section className="mx-auto flex max-w-5xl flex-col gap-8 px-4 py-10 sm:px-6 lg:px-8">
      <PageHeader title={t('checkout.title')} subtitle={t('checkout.subtitle')} />

      <div className="flex flex-col gap-8 lg:grid lg:grid-cols-[minmax(0,1fr)_22rem] lg:items-start">
        {/* Mobile/tablet: summary above the form (AC-2); desktop: sticky right column. */}
        <div className="lg:order-2">
          <OrderSummary product={product} />
        </div>
        <div className="lg:order-1">
          <OrderFormClient
            productId={product.productId}
            defaultName={profileResult.value.fullName ?? ''}
            defaultEmail={profileResult.value.email}
            defaultPhone={profileResult.value.phone ?? ''}
            personalPickupEnabled={maker.personalPickupEnabled}
            pickupNote={maker.pickupNote}
            pickupCity={maker.city}
            widgetConfig={widgetConfig}
            fulfillmentType={product.fulfillmentType}
          />
        </div>
      </div>
    </section>
  );
}

function InvalidLinkState() {
  return (
    <section className="mx-auto flex max-w-2xl flex-col gap-6 px-4 py-16 sm:px-6 lg:px-8">
      <EmptyState
        icon="alertCircle"
        title={t('checkout.invalidLink.title')}
        action={
          <Link
            href="/katalog"
            className="inline-flex items-center gap-2 rounded-lg border border-brand-line px-5 py-2.5 text-sm font-semibold text-brand-ink transition-colors duration-150 hover:border-brand-500 hover:bg-tint-brand hover:text-on-tint-brand"
          >
            {t('checkout.invalidLink.cta')}
            <Icon name="arrowRight" size={16} />
          </Link>
        }
      />
    </section>
  );
}

/**
 * A maker pressed "Objednat". Their account cannot hold a customer
 * session, so the only ways forward are switching accounts or
 * registering a customer one — both offered here rather than behind a
 * login form that would reject them.
 */
function MakerAccountState({
  email,
  productId,
}: {
  readonly email: string;
  readonly productId: string;
}) {
  return (
    <section className="mx-auto flex max-w-2xl flex-col gap-6 px-4 py-16 sm:px-6 lg:px-8">
      <EmptyState
        icon="user"
        title={t('checkout.makerAccount.title')}
        description={t('checkout.makerAccount.body', { email })}
        action={
          <div className="flex flex-wrap items-center justify-center gap-3">
            <Link
              href={`/produkt/${encodeURIComponent(productId)}`}
              className="inline-flex items-center gap-2 rounded-lg border border-zinc-700 px-5 py-2.5 text-sm font-medium text-zinc-300 transition-colors duration-150 hover:border-brand-line hover:text-brand-300"
            >
              <Icon name="arrowLeft" size={16} />
              {t('checkout.makerAccount.backToProduct')}
            </Link>
            <Link
              href="/register?type=customer"
              className="inline-flex items-center gap-2 rounded-lg border border-brand-line px-5 py-2.5 text-sm font-semibold text-brand-ink transition-colors duration-150 hover:border-brand-500 hover:bg-tint-brand hover:text-on-tint-brand"
            >
              {t('checkout.makerAccount.register')}
              <Icon name="arrowRight" size={16} />
            </Link>
          </div>
        }
      />
    </section>
  );
}

function LoadErrorState() {
  return (
    <section className="mx-auto flex max-w-2xl flex-col gap-6 px-4 py-16 sm:px-6 lg:px-8">
      <Alert variant="error">
        <p className="font-semibold">{t('checkout.loadError.title')}</p>
        <p className="mt-1">{t('checkout.loadError.body')}</p>
      </Alert>
      <div>
        <Link
          href="/katalog"
          className="inline-flex items-center gap-2 text-sm font-medium text-zinc-400 transition-colors hover:text-zinc-200"
        >
          <Icon name="arrowLeft" size={16} />
          {t('catalog.maker.back_to_catalog')}
        </Link>
      </div>
    </section>
  );
}
