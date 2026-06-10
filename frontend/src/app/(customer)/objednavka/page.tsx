import Link from 'next/link';
import type { Metadata } from 'next';
import { notFound, redirect } from 'next/navigation';
import { Alert } from '@/components/ui/alert';
import { Card } from '@/components/ui/card';
import { Icon } from '@/components/ui/icon';
import { getMakerBySlug, getProductById } from '@/lib/api-client-helpers/catalog';
import { getMyProfile } from '@/lib/api-client-helpers/profile';
import { getWidgetConfig } from '@/lib/api-client-helpers/shipping';
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

  // Entry guard 2 — unauthenticated → login redirect with the original
  // URL (the login page consumes `?redirect=`).
  const profileResult = await getMyProfile('customer');
  if (!profileResult.success) {
    if (profileResult.error.type === 'Unauthorized') {
      const target = `/objednavka?productId=${encodeURIComponent(productId)}`;
      redirect(`/auth/login?redirect=${encodeURIComponent(target)}`);
    }
    return <LoadErrorState />;
  }

  // Entry guard 3 — unknown/inactive product → 404.
  const productResult = await getProductById(productId);
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

  // Maker profile drives the personal-pickup gate (US-customer-0011
  // AC-3); the widget config feeds the Packeta widget. A failed widget
  // fetch degrades to a disabled Zásilkovna option (AC-6) — pass null.
  const [makerResult, widgetResult] = await Promise.all([
    getMakerBySlug(product.makerSlug),
    getWidgetConfig(),
  ]);
  if (!makerResult.success) {
    return <LoadErrorState />;
  }
  const maker = makerResult.value;
  const widgetConfig = widgetResult.success ? widgetResult.value : null;

  return (
    <section className="mx-auto flex max-w-5xl flex-col gap-8 px-4 py-10 sm:px-6 lg:px-8">
      <header className="flex flex-col gap-2">
        <h1 className="text-3xl font-bold tracking-tight text-white sm:text-4xl">
          {t('checkout.title')}
        </h1>
        <p className="max-w-2xl text-base text-zinc-400">{t('checkout.subtitle')}</p>
      </header>

      <div className="flex flex-col gap-8 lg:grid lg:grid-cols-[minmax(0,1fr)_22rem] lg:items-start">
        {/* Mobile/tablet: summary above the form (AC-2); desktop: sticky right column. */}
        <div className="lg:order-2">
          <OrderSummary product={product} />
        </div>
        <div className="lg:order-1">
          <OrderFormClient
            productId={product.productId}
            defaultEmail={profileResult.value.email}
            personalPickupEnabled={maker.personalPickupEnabled}
            pickupNote={maker.pickupNote}
            pickupCity={maker.city}
            widgetConfig={widgetConfig}
          />
        </div>
      </div>
    </section>
  );
}

function InvalidLinkState() {
  return (
    <section className="mx-auto flex max-w-2xl flex-col gap-6 px-4 py-16 sm:px-6 lg:px-8">
      <Card padding="lg" className="flex flex-col items-center gap-4 text-center">
        <h1 className="text-2xl font-semibold text-white">{t('checkout.invalidLink.title')}</h1>
        <Link
          href="/katalog"
          className="inline-flex items-center gap-2 rounded-xl bg-brand-400 px-5 py-2.5 text-sm font-semibold text-zinc-950 transition-all duration-200 hover:bg-brand-300"
        >
          {t('checkout.invalidLink.cta')}
          <Icon name="arrowRight" size={16} />
        </Link>
      </Card>
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
          className="inline-flex items-center gap-2 text-sm font-medium text-zinc-400 transition-colors hover:text-white"
        >
          <Icon name="arrowLeft" size={16} />
          {t('catalog.maker.back_to_catalog')}
        </Link>
      </div>
    </section>
  );
}
