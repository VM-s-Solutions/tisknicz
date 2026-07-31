'use client';

import { useRef, useState, type ChangeEvent, type ReactNode } from 'react';
import { Alert } from '@/components/ui/alert';
import { Avatar } from '@/components/ui/avatar';
import { Button } from '@/components/ui/button';
import { Icon } from '@/components/ui/icon';
import { t, type MessageKey } from '@/lib/i18n';
import type { ApiError, Result } from '@/lib/runtime/result';

/** Mirrors the backend allow-list (`FileSignatures.RasterImageContentTypes`). */
const ACCEPTED_TYPES = 'image/jpeg,image/png,image/webp';

/**
 * Mirrors `ProfileImageValidator.MaxSizeBytes` (2 MB).
 *
 * <para>
 * This is a UX pre-check, not a validation rule moved to the client —
 * the server stays authoritative and re-checks every byte. It exists
 * because the oversize case never reaches our handler: the endpoint's
 * `RequestSizeLimit` makes Kestrel reject the body first, which returns
 * ASP.NET's raw ProblemDetails rather than a `file.tooLarge` code, so
 * the picker would otherwise show the generic "upload failed" copy for
 * the single most likely failure. Checking here also spares the user
 * pushing a large file up just to be told no.
 * </para>
 */
const MAX_SIZE_BYTES = 2 * 1024 * 1024;

interface ProfileImagePickerProps {
  /** Current image URL, or null when none is set. */
  readonly currentUrl: string | null;
  /** Name the initials fallback is derived from. */
  readonly name?: string | null;
  /** Upload handler — returns the stored blob path on success. */
  readonly onUpload: (file: File) => Promise<Result<{ blobPath: string }, ApiError>>;
  /** Remove handler. Only reachable while an image exists. */
  readonly onRemove: () => Promise<Result<void, ApiError>>;
  /**
   * Called after a successful upload or removal with the new blob path
   * (null after removal) so the parent can update its own state.
   */
  readonly onChanged: (blobPath: string | null) => void;
  /** Short line under the buttons — what the image is used for. */
  readonly hint: string;
  /**
   * Block rendered above the buttons, beside the image — e.g. the
   * account name. Lets a profile header reuse the picker's own image
   * tile instead of rendering a second avatar of its own next to it.
   */
  readonly heading?: ReactNode;
}

/**
 * Avatar / logo picker: current image beside a pick-file and a remove
 * button. Shared by the maker and customer profile pages — the two
 * differ only in which endpoints they call and what the hint says, so
 * they pass those in rather than each growing their own copy of the
 * upload state machine.
 *
 * <para>
 * The preview switches to the newly-picked file immediately via an
 * object URL, so the tile updates on selection rather than after the
 * round-trip. That local URL is revoked once the server path arrives —
 * leaving it alive leaks the blob for the life of the document.
 * </para>
 */
export function ProfileImagePicker({
  currentUrl,
  name,
  onUpload,
  onRemove,
  onChanged,
  hint,
  heading,
}: ProfileImagePickerProps) {
  const fileInputRef = useRef<HTMLInputElement | null>(null);
  const [previewUrl, setPreviewUrl] = useState<string | null>(null);
  const [busy, setBusy] = useState<'upload' | 'remove' | null>(null);
  const [error, setError] = useState<string | null>(null);

  const shownUrl = previewUrl ?? currentUrl;

  function clearPreview() {
    setPreviewUrl((previous) => {
      if (previous) URL.revokeObjectURL(previous);
      return null;
    });
  }

  async function handleFileChange(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0];
    // Reset the input regardless of outcome so re-picking the same file
    // fires onChange again — otherwise a failed upload can't be retried
    // without choosing a different file first.
    event.target.value = '';
    if (!file) return;

    if (file.size > MAX_SIZE_BYTES) {
      setError(t('profile.image.error.too_large'));
      return;
    }

    setError(null);
    clearPreview();
    const objectUrl = URL.createObjectURL(file);
    setPreviewUrl(objectUrl);
    setBusy('upload');

    const result = await onUpload(file);
    setBusy(null);

    if (!result.success) {
      clearPreview();
      setError(t(mapUploadErrorCode(result.error.code)));
      return;
    }

    // The server path is live now; drop the local object URL so the
    // browser can release the file.
    clearPreview();
    onChanged(result.value.blobPath);
  }

  async function handleRemove() {
    setError(null);
    setBusy('remove');
    const result = await onRemove();
    setBusy(null);

    if (!result.success) {
      setError(t('profile.image.error.remove_failed'));
      return;
    }

    clearPreview();
    onChanged(null);
  }

  return (
    <div className="flex flex-col gap-3">
      {error ? <Alert variant="error">{error}</Alert> : null}

      <div className="flex items-center gap-4">
        <Avatar src={shownUrl} name={name} size="xl" />

        <div className="flex min-w-0 flex-col gap-2">
          {heading}
          <input
            ref={fileInputRef}
            type="file"
            accept={ACCEPTED_TYPES}
            onChange={handleFileChange}
            disabled={busy !== null}
            className="hidden"
          />
          <div className="flex flex-wrap items-center gap-2">
            <Button
              type="button"
              variant="outline"
              size="sm"
              onClick={() => fileInputRef.current?.click()}
              loading={busy === 'upload'}
              disabled={busy !== null}
            >
              {busy !== 'upload' ? <Icon name="upload" size={14} /> : null}
              {busy === 'upload'
                ? t('profile.image.uploading')
                : currentUrl
                  ? t('profile.image.replace')
                  : t('profile.image.upload')}
            </Button>

            {currentUrl ? (
              <Button
                type="button"
                variant="ghost"
                size="sm"
                onClick={handleRemove}
                loading={busy === 'remove'}
                disabled={busy !== null}
              >
                {busy !== 'remove' ? <Icon name="trash" size={14} /> : null}
                {busy === 'remove' ? t('profile.image.removing') : t('profile.image.remove')}
              </Button>
            ) : null}
          </div>

          <p className="text-xs text-zinc-500">{hint}</p>
          <p className="text-xs text-zinc-500">{t('profile.image.constraints')}</p>
        </div>
      </div>
    </div>
  );
}

/**
 * Map a backend `ApiError` code to local i18n copy. The codes come from
 * `BusinessErrorMessage.FileTooLarge / FileUnsupportedType /
 * FileInvalid` on the controller's `ProfileImageValidator` path;
 * anything unrecognised falls through to the generic invalid message
 * rather than leaking a raw backend string into the UI.
 */
function mapUploadErrorCode(code: string): MessageKey {
  if (code === 'file.tooLarge' || code === 'file.too_large') {
    return 'profile.image.error.too_large';
  }
  if (code === 'file.unsupportedType' || code === 'file.unsupported_type') {
    return 'profile.image.error.unsupported_type';
  }
  return 'profile.image.error.invalid';
}
