import { cache } from 'react';
import Link from 'next/link';
import { notFound } from 'next/navigation';
import type { Metadata } from 'next';
import { Alert } from '@/components/ui/alert';
import { Icon } from '@/components/ui/icon';
import { getMyProductById } from '@/lib/api-client-helpers/maker-products';
import { loadProductCategoryOptions } from '@/lib/catalog/load-category-options';
import { t } from '@/lib/i18n';
import { DeleteProductButton } from '../_components/delete-product-button';
import { ImageManager } from '../_components/image-manager';
import { ProductForm } from '../_components/product-form';
/**
 * Per-request memo: `generateMetadata` and the page body both need the
 * detail, and `apiFetch` composes a fresh `AbortSignal.timeout` per call
 * — Next's fetch memoization opts OUT whenever an `init.signal` is
 * present (`next/dist/server/lib/dedupe-fetch.js`), so without `cache()`
 * every edit view issues two identical backend GETs. Scope is one server
 * request; `router.refresh()` is a new request and re-fetches.
 */
const loadProduct = cache(getMyProductById);


interface PageProps {
  readonly params: Promise<{ productId: string }>;
}

// The dashboard always reflects the latest backend state.
export const dynamic = 'force-dynamic';

export async function generateMetadata({ params }: PageProps): Promise<Metadata> {
  const { productId } = await params;
  const result = await loadProduct(productId);
  if (!result.success) {
    // Don't branch on `NotFound` here — the page itself renders the
    // not-found shell via the framework, and the metadata fallback is
    // the same regardless of error type.
    return { title: t('dashboard.maker.products.edit.metadata.title') };
  }
  return {
    title: `${result.value.title} — ${t('dashboard.maker.products.edit.metadata.title')}`,
  };
}

/**
 * Maker product edit page (T-0049 AC-6/AC-7/AC-11). Server Component
 * shell that loads <see cref="MakerProductDetail"/>, IDOR-shielded by
 * the backend — a 404 from the helper means "doesn't exist OR belongs
 * to someone else", and we collapse both to <c>notFound()</c> so the
 * URL doesn't enumerate product ids across makers.
 *
 * <para>
 * Renders three sections, in order:
 * </para>
 * <list type="number">
 *   <item><description>Optional "this product is inactive" banner.</description></item>
 *   <item><description>The shared <see cref="ProductForm"/> in edit mode.</description></item>
 *   <item><description>The <see cref="ImageManager"/> grid + upload control.</description></item>
 * </list>
 * <para>
 * The destructive "Smazat produkt" action lives at the bottom of the
 * page so it stays out of the primary edit flow.
 * </para>
 */
export default async function MakerProductEditPage({ params }: PageProps) {
  const { productId } = await params;
  // The category list is reference data that does not depend on the
  // product, so both reads go out together. Awaiting the options inline
  // in the JSX (the previous shape) put a second full backend round trip
  // AFTER the detail had already resolved.
  const [result, categoryOptions] = await Promise.all([
    loadProduct(productId),
    loadProductCategoryOptions(),
  ]);

  if (!result.success) {
    if (result.error.type === 'NotFound') {
      notFound();
    }
    return (
      <section className="py-12 lg:py-16">
        <div className="mx-auto flex max-w-3xl flex-col gap-6 px-4 sm:px-6 lg:px-8">
          <Link
            href="/dashboard/maker/produkty"
            className="inline-flex w-fit items-center gap-1.5 text-sm text-zinc-400 transition-colors hover:text-zinc-200"
          >
            <Icon name="chevronLeft" size={16} />
            {t('dashboard.maker.products.edit.back')}
          </Link>
          <Alert variant="error">
            <p className="font-semibold">{t('dashboard.maker.products.edit.error.title')}</p>
            <p className="mt-1">{t('dashboard.maker.products.edit.error.body')}</p>
          </Alert>
        </div>
      </section>
    );
  }

  const product = result.value;

  return (
    <section className="py-12 lg:py-16">
      <div className="mx-auto flex max-w-3xl flex-col gap-6 px-4 sm:px-6 lg:px-8">
        <Link
          href="/dashboard/maker/produkty"
          className="inline-flex w-fit items-center gap-1.5 text-sm text-zinc-400 transition-colors hover:text-zinc-200"
        >
          <Icon name="chevronLeft" size={16} />
          {t('dashboard.maker.products.edit.back')}
        </Link>

        <header>
          <h1 className="text-shine text-3xl font-bold tracking-tight sm:text-4xl">
            {t('dashboard.maker.products.edit.title')}
          </h1>
          <p className="mt-3 text-base text-zinc-400">{product.title}</p>
        </header>

        {!product.isActive ? (
          <Alert variant="warning">
            {t('dashboard.maker.products.edit.inactive_banner')}
          </Alert>
        ) : null}

        <ProductForm mode="edit" initial={product} categoryOptions={categoryOptions} />

        <ImageManager productId={product.productId} images={product.images} />

        <div className="flex items-center justify-end pt-2">
          <DeleteProductButton productId={product.productId} variant="page" />
        </div>
      </div>
    </section>
  );
}
