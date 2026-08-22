import Image from 'next/image';
import Link from 'next/link';
import { Badge } from '@/components/ui/badge';
import { Card } from '@/components/ui/card';
import { Icon } from '@/components/ui/icon';
import { buildProductImageUrl } from '@/lib/api-client-helpers/catalog';
import type { MakerProductListItem } from '@/lib/api-client-helpers/maker-products';
import { CATALOG_CATEGORIES } from '@/lib/catalog/categories';
import { formatWeight } from '@/lib/format/weight';
import { t } from '@/lib/i18n';
import { formatCzk } from '@/lib/money/formatter';
import { formatDate } from '@/lib/utils/dates';
import { DeleteProductButton } from './_components/delete-product-button';

interface ProductCardProps {
  readonly item: MakerProductListItem;
}

const IMAGE_WIDTH = 320;
const IMAGE_HEIGHT = 240;

/**
 * One product on the maker dashboard index (T-0049). Server Component
 * — the only interactivity is the embedded <c>DeleteProductButton</c>
 * which opens a confirm modal client-side.
 *
 * Categories: <c>MakerProductListItem.categoryId</c> is the category
 * row ID; we look it up in <c>CATALOG_CATEGORIES</c> for the i18n
 * label. If the id isn't in the launch list (admin added a category
 * after launch, T-0119), we fall back to rendering the raw id — better
 * to show the maker exactly what's attached to their product than hide
 * it behind a placeholder (T-0049 review M1).
 *
 * Soft-deleted rows render with reduced opacity and the
 * <c>inactive</c> badge — the dashboard surfaces drafts and
 * recently-deactivated items per <c>GetMyProducts</c>.
 */
export function MakerProductCard({ item }: ProductCardProps) {
  const imageUrl = buildProductImageUrl(item.primaryImageBlobPath);
  const categoryOption = CATALOG_CATEGORIES.find((c) => c.id === item.categoryId);
  // Spec fallback: if an admin adds a category post-launch the id
  // won't match the seeded list — render it raw rather than hiding the
  // mismatch behind a "Bez kategorie" placeholder so the maker can see
  // what's really attached to their product. T-0049 review M1.
  const categoryLabel = categoryOption ? t(categoryOption.labelKey) : item.categoryId;
  const createdDate = formatDate(item.createdOn);

  return (
    <Card
      padding="none"
      variant="elevated"
      hover
      className={`flex flex-col overflow-hidden ${item.isActive ? '' : 'opacity-70'}`}
    >
      <Link
        href={`/dashboard/maker/produkty/${encodeURIComponent(item.productId)}`}
        className="block focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-400 focus-visible:ring-offset-2 focus-visible:ring-offset-surface-primary"
      >
        <div className="relative aspect-[4/3] w-full overflow-hidden rounded-t-xl bg-surface-elevated">
          {imageUrl ? (
            <Image
              src={imageUrl}
              alt={t('dashboard.maker.products.card.image_alt', { title: item.title })}
              width={IMAGE_WIDTH}
              height={IMAGE_HEIGHT}
              sizes="(max-width: 640px) 100vw, (max-width: 1024px) 50vw, 320px"
              className="h-full w-full object-cover"
            />
          ) : (
            <div className="flex h-full w-full items-center justify-center gap-2 text-sm text-zinc-500">
              <Icon name="image" size={20} />
              {t('dashboard.maker.products.card.no_image')}
            </div>
          )}
          <div className="absolute right-3 top-3">
            {item.isActive ? (
              <Badge variant="success">{t('dashboard.maker.products.badge.active')}</Badge>
            ) : (
              <Badge variant="warning">{t('dashboard.maker.products.badge.inactive')}</Badge>
            )}
          </div>
        </div>
      </Link>

      <div className="flex flex-1 flex-col gap-3 p-4">
        <div className="flex flex-col gap-1">
          <Link
            href={`/dashboard/maker/produkty/${encodeURIComponent(item.productId)}`}
            className="text-base font-semibold text-white transition-colors hover:text-brand-400 line-clamp-2"
          >
            {item.title}
          </Link>
          <p className="text-xs uppercase tracking-wide text-zinc-500">{categoryLabel}</p>
        </div>

        <p className="text-lg font-bold text-brand-400">
          <ProductPrice item={item} />
        </p>

        {/* Each row is a single "Label: value" string already (the i18n
            keys interpolate the value), so semantically these are list
            items, not <dt>/<dd> pairs — switched off <dl>. T-0049
            Copilot review L3. */}
        <ul className="flex flex-col gap-1 text-xs text-zinc-400">
          <li>{t('dashboard.maker.products.card.weight', { value: formatWeight(item.weightGrams) })}</li>
          <li>{t('dashboard.maker.products.card.image_count', { count: item.imageCount })}</li>
          <li>{t('dashboard.maker.products.card.created', { date: createdDate })}</li>
        </ul>
      </div>

      <div className="flex items-center justify-between gap-2 border-t border-zinc-800 px-4 py-3">
        <Link
          href={`/dashboard/maker/produkty/${encodeURIComponent(item.productId)}`}
          className="inline-flex items-center gap-1.5 rounded-lg border border-zinc-700 px-3.5 py-1.5 text-sm font-semibold text-zinc-200 transition-colors hover:border-brand-500/60 hover:text-brand-300"
        >
          <Icon name="edit" size={14} />
          {t('dashboard.maker.products.actions.edit')}
        </Link>
        {/* An inactive product is already soft-deleted — offering a second
            "Smazat" invited a meaningless re-delete (T-0174, MAKER-H4 guard;
            reactivation itself is Q-0040 / T-0180). */}
        {item.isActive ? (
          <DeleteProductButton productId={item.productId} variant="card" />
        ) : null}
      </div>
    </Card>
  );
}

function ProductPrice({ item }: { readonly item: MakerProductListItem }) {
  if (item.priceType === 'OnRequest' || item.priceCurrency !== 'CZK') {
    // Defensive: a non-CZK price snapshot shouldn't reach the dashboard
    // during MVP, but a single contract-violating row mustn't 500 the
    // grid. Fall back to the catalog's "Na poptávku" copy and let the
    // rest of the cards render. Mirrors T-0047 / T-0048 convention.
    return <>{t('catalog.product.price.on_request')}</>;
  }
  const formatted = formatCzk(item.priceAmountMinor, item.priceCurrency);
  if (item.priceType === 'From') {
    return <>{t('catalog.product.price.from', { price: formatted })}</>;
  }
  return <>{formatted}</>;
}
