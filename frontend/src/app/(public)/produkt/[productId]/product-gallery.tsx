'use client';

import Image from 'next/image';
import { useState } from 'react';
import { t } from '@/lib/i18n';
import {
  buildProductImageUrl,
  type ProductImageItem,
} from '@/lib/api-client-helpers/catalog';

interface ProductGalleryProps {
  readonly images: readonly ProductImageItem[];
  readonly title: string;
}

const PRIMARY_WIDTH = 600;
const PRIMARY_HEIGHT = 600;
const THUMB_WIDTH = 96;
const THUMB_HEIGHT = 96;

/**
 * Client-side gallery for the product detail page (T-0048 AC-2). The
 * primary image is swapped via local <c>useState</c> — no
 * <c>useEffect</c>, no data fetching, no business logic. The Server
 * Component shell passes the already-sorted image list (backend orders
 * by <c>sortOrder</c> ascending) and the gallery preserves that order.
 *
 * Thumbnails are <c>&lt;button type="button"&gt;</c> with an
 * <c>aria-label</c> per i18n so the strip is keyboard-focusable +
 * screen-reader friendly without dragging in extra ARIA scaffolding.
 *
 * Empty list → placeholder card. Exactly one image → primary only, no
 * thumbnail strip (keeps the simple case visually clean).
 */
export function ProductGallery({ images, title }: ProductGalleryProps) {
  const [selectedIndex, setSelectedIndex] = useState(0);

  if (images.length === 0) {
    return (
      <div className="flex aspect-square w-full items-center justify-center rounded-2xl border border-zinc-800 bg-surface-elevated text-sm text-zinc-500">
        {t('catalog.product_detail.gallery.no_image')}
      </div>
    );
  }

  // Defensive clamp — selectedIndex stays in range even if the parent
  // re-keys with a shorter list (shouldn't happen on a Server Component
  // shell, but keeps the gallery from rendering an undefined slot).
  const safeIndex = Math.min(selectedIndex, images.length - 1);
  const primary = images[safeIndex];
  const primaryUrl = buildProductImageUrl(primary.blobPath);

  return (
    <div className="flex flex-col gap-4">
      <div className="relative aspect-square w-full overflow-hidden rounded-2xl border border-zinc-800 bg-surface-elevated">
        {primaryUrl ? (
          <Image
            src={primaryUrl}
            alt={t('catalog.product.image_alt', { title })}
            width={PRIMARY_WIDTH}
            height={PRIMARY_HEIGHT}
            className="h-full w-full object-cover"
            priority
          />
        ) : (
          <div className="flex h-full w-full items-center justify-center text-sm text-zinc-500">
            {t('catalog.product_detail.gallery.no_image')}
          </div>
        )}
      </div>

      {images.length > 1 ? (
        <ul className="flex flex-wrap gap-2">
          {images.map((image, index) => {
            const url = buildProductImageUrl(image.blobPath);
            const isSelected = index === safeIndex;
            return (
              <li key={image.imageId}>
                <button
                  type="button"
                  onClick={() => setSelectedIndex(index)}
                  aria-label={t('catalog.product_detail.gallery.thumbnail_aria', {
                    n: index + 1,
                  })}
                  aria-pressed={isSelected}
                  className={`relative h-20 w-20 overflow-hidden rounded-xl border bg-surface-elevated transition-all duration-200 focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-400 focus-visible:ring-offset-2 focus-visible:ring-offset-surface-primary ${
                    isSelected
                      ? 'border-brand-400 ring-2 ring-brand-400/40'
                      : 'border-zinc-800 hover:border-zinc-600'
                  }`}
                >
                  {url ? (
                    <Image
                      src={url}
                      alt={t('catalog.product.image_alt', { title })}
                      width={THUMB_WIDTH}
                      height={THUMB_HEIGHT}
                      className="h-full w-full object-cover"
                    />
                  ) : (
                    <span className="flex h-full w-full items-center justify-center text-xs text-zinc-500">
                      {t('catalog.product_detail.gallery.no_image')}
                    </span>
                  )}
                </button>
              </li>
            );
          })}
        </ul>
      ) : null}
    </div>
  );
}
