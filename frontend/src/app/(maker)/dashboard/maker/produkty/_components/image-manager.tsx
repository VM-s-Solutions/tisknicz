'use client';

import Image from 'next/image';
import { useRouter } from 'next/navigation';
import { useRef, useState, type ChangeEvent } from 'react';
import { Alert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Icon } from '@/components/ui/icon';
import { Spinner } from '@/components/ui/spinner';
import { buildProductImageUrl } from '@/lib/api-client-helpers/catalog';
import {
  type ProductImageItem,
  removeProductImage,
  uploadProductImage,
} from '@/lib/api-client-helpers/maker-products';
import { t, type MessageKey } from '@/lib/i18n';

interface ImageManagerProps {
  readonly productId: string;
  readonly images: readonly ProductImageItem[];
}

const ACCEPTED_TYPES = 'image/jpeg,image/png,image/webp';
const THUMB_WIDTH = 280;
const THUMB_HEIGHT = 210;

/**
 * Existing-images grid + single-file uploader for the maker product
 * edit page (T-0049 AC-9 / AC-10). Server Components on the route
 * shell pass the current images list as a prop; mutations
 * (<see cref="uploadProductImage"/>, <see cref="removeProductImage"/>)
 * call <c>router.refresh()</c> on success so the parent Server
 * Component re-fetches and the grid stays consistent with backend
 * state.
 *
 * <para>
 * No client-side preview before upload — fire-and-await per the
 * ticket. The browser sets the multipart boundary in
 * <c>Content-Type</c> itself; the helper builds the <c>FormData</c>
 * and passes it as the raw body.
 * </para>
 */
export function ImageManager({ productId, images }: ImageManagerProps) {
  const router = useRouter();
  const fileInputRef = useRef<HTMLInputElement | null>(null);
  const [uploading, setUploading] = useState(false);
  const [removingId, setRemovingId] = useState<string | null>(null);
  const [uploadError, setUploadError] = useState<string | null>(null);
  const [removeError, setRemoveError] = useState<string | null>(null);

  async function handleFileChange(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0];
    // Always reset the input value so re-selecting the same file
    // re-fires onChange. We do this regardless of success so
    // cancellation paths still let the user retry.
    if (event.target) {
      event.target.value = '';
    }
    if (!file) return;
    setUploadError(null);
    setUploading(true);
    const result = await uploadProductImage(productId, file);
    setUploading(false);
    if (!result.success) {
      setUploadError(t(mapUploadErrorCode(result.error.code, result.error.type)));
      return;
    }
    router.refresh();
  }

  async function handleRemove(imageId: string) {
    setRemoveError(null);
    setRemovingId(imageId);
    const result = await removeProductImage(productId, imageId);
    setRemovingId(null);
    if (!result.success) {
      setRemoveError(t('dashboard.maker.products.images.error.remove_failed'));
      return;
    }
    router.refresh();
  }

  return (
    <Card variant="elevated" padding="lg" className="flex flex-col gap-5">
      <div className="flex items-center gap-3">
        <span className="icon-tile h-9 w-9">
          <Icon name="image" size={16} />
        </span>
        <div className="flex flex-col gap-0.5">
          <h2 className="text-lg font-semibold text-white">
            {t('dashboard.maker.products.images.title')}
          </h2>
          <p className="text-sm text-zinc-400">
            {t('dashboard.maker.products.images.description')}
          </p>
        </div>
      </div>

      {uploadError ? <Alert variant="error">{uploadError}</Alert> : null}
      {removeError ? <Alert variant="error">{removeError}</Alert> : null}

      {images.length > 0 ? (
        <ul className="grid grid-cols-2 gap-3 sm:grid-cols-3 sm:gap-4">
          {images.map((image, index) => {
            const url = buildProductImageUrl(image.blobPath);
            const isRemoving = removingId === image.imageId;
            return (
              <li
                key={image.imageId}
                className="flex flex-col gap-2 overflow-hidden rounded-xl border border-zinc-800 bg-surface-elevated"
              >
                <div className="relative aspect-[4/3] w-full bg-zinc-900">
                  {url ? (
                    <Image
                      src={url}
                      alt={t('dashboard.maker.products.images.image_alt', { n: index + 1 })}
                      width={THUMB_WIDTH}
                      height={THUMB_HEIGHT}
                      sizes="(max-width: 640px) 100vw, (max-width: 1024px) 50vw, 280px"
                      className="h-full w-full object-cover"
                    />
                  ) : (
                    <div className="flex h-full w-full items-center justify-center text-xs text-zinc-500">
                      <Icon name="image" size={20} />
                    </div>
                  )}
                </div>
                <div className="flex items-center justify-end p-2">
                  <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    onClick={() => handleRemove(image.imageId)}
                    loading={isRemoving}
                    disabled={removingId !== null && !isRemoving}
                  >
                    <Icon name="trash" size={14} />
                    {isRemoving
                      ? t('dashboard.maker.products.images.removing')
                      : t('dashboard.maker.products.images.remove')}
                  </Button>
                </div>
              </li>
            );
          })}
        </ul>
      ) : null}

      <div className="relative flex flex-col items-center gap-3 overflow-hidden rounded-xl border border-dashed border-zinc-700 px-6 py-8 text-center">
        <span className="icon-tile relative h-12 w-12">
          <Icon name="upload" size={20} />
        </span>
        {images.length === 0 ? (
          <p className="relative text-sm text-zinc-500">
            {t('dashboard.maker.products.images.empty')}
          </p>
        ) : null}
        <input
          ref={fileInputRef}
          type="file"
          accept={ACCEPTED_TYPES}
          onChange={handleFileChange}
          disabled={uploading}
          className="hidden"
          id="product-image-upload"
        />
        <div className="relative flex flex-wrap items-center justify-center gap-3">
          <Button
            type="button"
            variant="outline"
            onClick={() => fileInputRef.current?.click()}
            loading={uploading}
            disabled={uploading}
          >
            <Icon name="upload" size={16} />
            {uploading
              ? t('dashboard.maker.products.images.uploading')
              : t('dashboard.maker.products.images.upload_button')}
          </Button>
          {uploading ? (
            <span className="flex items-center gap-2 text-sm text-zinc-400">
              <Spinner size="sm" />
              {t('dashboard.maker.products.images.uploading')}
            </span>
          ) : null}
        </div>
      </div>
    </Card>
  );
}

/**
 * Map a backend <c>ApiError</c> code into one of the local i18n keys.
 * Codes that don't match a known image-upload failure fall through to
 * the generic <c>invalid</c> copy (better than leaking a raw backend
 * string into the UI).
 *
 * <para>
 * <c>product.imageLimitReached</c> covers the 10-image cap (409 from
 * <c>AddProductImage</c>). The file.* codes come from
 * <c>BusinessErrorMessage.FileTooLarge / FileUnsupportedType /
 * FileInvalid</c> emitted by the controller's
 * <c>ImageUploadValidator</c> path.
 * </para>
 */
function mapUploadErrorCode(code: string, errorType: string): MessageKey {
  if (code === 'file.tooLarge' || code === 'file.too_large') {
    return 'dashboard.maker.products.images.error.too_large';
  }
  if (code === 'file.unsupportedType' || code === 'file.unsupported_type') {
    return 'dashboard.maker.products.images.error.unsupported_type';
  }
  if (
    code === 'product.imageLimitReached' ||
    code === 'product.image_limit_reached' ||
    errorType === 'Conflict'
  ) {
    return 'dashboard.maker.products.images.error.limit_reached';
  }
  return 'dashboard.maker.products.images.error.invalid';
}
