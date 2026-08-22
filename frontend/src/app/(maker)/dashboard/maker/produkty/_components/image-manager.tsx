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
import { resolveErrorMessage } from '@/lib/runtime/errors';
import type { ApiError } from '@/lib/runtime/result';

interface ImageManagerProps {
  readonly productId: string;
  readonly images: readonly ProductImageItem[];
}

const ACCEPTED_TYPES = 'image/jpeg,image/png,image/webp';
const THUMB_WIDTH = 280;
const THUMB_HEIGHT = 210;
/** Backend cap (Product.MaxImages) — mirrored so the maker sees "N/10"
 * before the 409 does the teaching. */
const MAX_IMAGES = 10;

interface QueueProgress {
  readonly current: number;
  readonly total: number;
}

/**
 * Existing-images grid + uploader for the maker product edit page
 * (T-0049 AC-9 / AC-10; reworked in T-0174, audit MAKER-M6). The picker
 * accepts multiple files and uploads them sequentially — per-file errors
 * stay attributable and the one-file backend endpoint sees no burst.
 * A visible "N/10" counter surfaces the cap before the 409 would.
 *
 * <para>
 * Mutations call <c>router.refresh()</c> on completion so the parent
 * Server Component re-fetches and the grid stays consistent with
 * backend state. The browser sets the multipart boundary itself.
 * </para>
 */
export function ImageManager({ productId, images }: ImageManagerProps) {
  const router = useRouter();
  const fileInputRef = useRef<HTMLInputElement | null>(null);
  const [progress, setProgress] = useState<QueueProgress | null>(null);
  const [removingId, setRemovingId] = useState<string | null>(null);
  const [uploadErrors, setUploadErrors] = useState<readonly string[]>([]);
  const [skippedNotice, setSkippedNotice] = useState<string | null>(null);
  const [removeError, setRemoveError] = useState<string | null>(null);

  const uploading = progress !== null;
  const capacityLeft = Math.max(0, MAX_IMAGES - images.length);
  const atCapacity = capacityLeft === 0;

  async function handleFilesChange(event: ChangeEvent<HTMLInputElement>) {
    const picked = Array.from(event.target.files ?? []);
    // Always reset the input value so re-selecting the same files
    // re-fires onChange, on success and failure alike.
    if (event.target) {
      event.target.value = '';
    }
    if (picked.length === 0) return;

    setUploadErrors([]);
    setSkippedNotice(null);

    const queue = picked.slice(0, capacityLeft);
    const skipped = picked.length - queue.length;
    if (skipped > 0) {
      setSkippedNotice(
        t('dashboard.maker.products.images.skipped_over_cap', { count: skipped, max: MAX_IMAGES }),
      );
    }
    if (queue.length === 0) return;

    const failures: string[] = [];
    for (const [index, file] of queue.entries()) {
      setProgress({ current: index + 1, total: queue.length });
      const result = await uploadProductImage(productId, file);
      if (!result.success) {
        failures.push(`${file.name}: ${describeUploadError(result.error)}`);
      }
    }
    setProgress(null);
    setUploadErrors(failures);
    // One refresh for the whole batch — every success is already
    // persisted server-side; failures are listed for retry.
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
      <div className="flex items-center justify-between gap-3">
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
        <span className="whitespace-nowrap text-sm text-zinc-400">
          {t('dashboard.maker.products.images.counter', {
            count: images.length,
            max: MAX_IMAGES,
          })}
        </span>
      </div>

      {uploadErrors.length > 0 ? (
        <Alert variant="error">
          <p className="font-semibold">
            {t('dashboard.maker.products.images.error.batch_failed')}
          </p>
          <ul className="mt-1 list-inside list-disc text-sm">
            {uploadErrors.map((message) => (
              <li key={message}>{message}</li>
            ))}
          </ul>
        </Alert>
      ) : null}
      {skippedNotice ? <Alert variant="warning">{skippedNotice}</Alert> : null}
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
        {atCapacity ? (
          <p className="relative text-sm text-zinc-500">
            {t('dashboard.maker.products.images.at_capacity', { max: MAX_IMAGES })}
          </p>
        ) : null}
        <input
          ref={fileInputRef}
          type="file"
          accept={ACCEPTED_TYPES}
          multiple
          onChange={handleFilesChange}
          disabled={uploading || atCapacity}
          className="hidden"
          id="product-image-upload"
        />
        <div className="relative flex flex-wrap items-center justify-center gap-3">
          <Button
            type="button"
            variant="outline"
            onClick={() => fileInputRef.current?.click()}
            loading={uploading}
            disabled={uploading || atCapacity}
          >
            <Icon name="upload" size={16} />
            {uploading
              ? t('dashboard.maker.products.images.uploading')
              : t('dashboard.maker.products.images.upload_button')}
          </Button>
          {progress ? (
            <span className="flex items-center gap-2 text-sm text-zinc-400" role="status">
              <Spinner size="sm" />
              {t('dashboard.maker.products.images.uploading_progress', {
                current: progress.current,
                total: progress.total,
              })}
            </span>
          ) : null}
        </div>
      </div>
    </Card>
  );
}

/**
 * Human copy for one failed file. File-validation codes map to the
 * specific local keys; transport failures (timeout, unreachable — audit
 * MAKER-H2: an aborted big upload used to read "invalid file") go
 * through the shared resolver so the maker sees the truthful transient
 * copy; anything else falls back to the generic invalid-file text.
 */
function describeUploadError(error: ApiError): string {
  if (error.code.startsWith('network.')) {
    return resolveErrorMessage(error);
  }
  return t(mapUploadErrorCode(error.code, error.type));
}

/**
 * Map a backend <c>ApiError</c> code into one of the local i18n keys.
 * <c>product.imageLimitReached</c> covers the 10-image cap (409 from
 * <c>AddProductImage</c>); the file.* codes come from
 * <c>ImageUploadValidator</c>.
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
