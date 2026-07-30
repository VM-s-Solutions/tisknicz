import Link from 'next/link';
import type { Metadata } from 'next';
import { Icon } from '@/components/ui/icon';
import { loadProductCategoryOptions } from '@/lib/catalog/load-category-options';
import { t } from '@/lib/i18n';
import { ProductForm } from '../_components/product-form';

export function generateMetadata(): Metadata {
  return {
    title: t('dashboard.maker.products.create.metadata.title'),
  };
}

/**
 * Maker product create page (T-0049). Server Component shell that
 * renders the shared <c>ProductForm</c> in <c>create</c> mode. On
 * successful submit the form pushes to the edit route so the maker
 * can immediately upload images (image upload requires
 * <c>productId</c> in the path).
 */
export default async function MakerProductCreatePage() {
  const categoryOptions = await loadProductCategoryOptions();
  return (
    <section className="bg-surface-primary py-12 lg:py-16">
      <div className="mx-auto flex max-w-3xl flex-col gap-6 px-4 sm:px-6 lg:px-8">
        <div>
          <Link
            href="/dashboard/maker/produkty"
            className="inline-flex items-center gap-2 text-sm font-medium text-zinc-400 transition-colors hover:text-white"
          >
            <Icon name="arrowLeft" size={16} />
            {t('dashboard.maker.products.create.back')}
          </Link>
        </div>
        <header>
          <h1 className="text-shine text-3xl font-bold tracking-tight sm:text-4xl">
            {t('dashboard.maker.products.create.title')}
          </h1>
          <p className="mt-3 max-w-2xl text-base text-zinc-400">
            {t('dashboard.maker.products.create.subtitle')}
          </p>
        </header>
        <ProductForm mode="create" categoryOptions={categoryOptions} />
      </div>
    </section>
  );
}
