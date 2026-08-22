'use client';

import { usePathname, useRouter, useSearchParams } from 'next/navigation';
import { useEffect, useRef, useState, type ChangeEvent } from 'react';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Icon } from '@/components/ui/icon';
import {
  type OrderAttachmentSummary,
  uploadOrderAttachment,
} from '@/lib/api-client-helpers/orders-client';
import { FileDownloadButton } from './order-actions-client';
import { formatFileSize } from '@/lib/format/file-size';
import { t } from '@/lib/i18n';
import { resolveErrorMessage } from '@/lib/runtime/errors';
import {
  ORDER_ATTACHMENT_ALLOWED_TYPES,
  ORDER_ATTACHMENT_MAX_BYTES,
  ORDER_ATTACHMENT_MAX_FILES,
} from '@/lib/utils/validation';

/**
 * Attachment retry surface on the pre-payment order page (T-0084b,
 * Q2 lock of T-0084a). Lists the already-uploaded files from the SSR
 * detail prop, allows adding more up to the 10-file cap (count gate =
 * existing + non-failed local rows; the backend 409 stays the
 * authority), and offers per-file retry — the failed `File` stays in
 * client memory until the page unloads. Successes append optimistically
 * (no `router.refresh()` — a full server re-render per file would drop
 * the in-memory failed files; T-0084b Alternatives Option E).
 */

type UploadStatus = 'queued' | 'uploading' | 'done' | 'failed';

interface ManagedUpload {
  readonly id: number;
  readonly file: File;
  readonly status: UploadStatus;
  readonly errorMessage?: string;
}

const ACCEPT_ATTRIBUTE = [...ORDER_ATTACHMENT_ALLOWED_TYPES].join(',');

interface AttachmentManagerClientProps {
  readonly orderId: string;
  readonly initialAttachments: readonly OrderAttachmentSummary[];
}

export function AttachmentManagerClient({
  orderId,
  initialAttachments,
}: AttachmentManagerClientProps) {
  const router = useRouter();
  const pathname = usePathname();
  const searchParams = useSearchParams();
  const inputRef = useRef<HTMLInputElement | null>(null);
  const nextIdRef = useRef(0);
  const [uploads, setUploads] = useState<readonly ManagedUpload[]>([]);
  const [rejections, setRejections] = useState<readonly string[]>([]);

  // The `?attachmentsFailed=N` handoff warning must not resurrect on a
  // refresh AFTER the files were successfully re-uploaded — strip the
  // param once this retry surface has mounted (T-0172, CUST-L3).
  useEffect(() => {
    if (!searchParams.has('attachmentsFailed')) return;
    const params = new URLSearchParams(searchParams.toString());
    params.delete('attachmentsFailed');
    const query = params.toString();
    router.replace(query ? `${pathname}?${query}` : pathname, { scroll: false });
    // eslint-disable-next-line react-hooks/exhaustive-deps -- one-shot on mount
  }, []);

  const occupiedSlots =
    initialAttachments.length + uploads.filter((u) => u.status !== 'failed').length;
  const capReached = occupiedSlots >= ORDER_ATTACHMENT_MAX_FILES;

  function patchUpload(id: number, patch: Partial<Omit<ManagedUpload, 'id' | 'file'>>) {
    setUploads((prev) => prev.map((u) => (u.id === id ? { ...u, ...patch } : u)));
  }

  async function uploadOne(id: number, file: File) {
    patchUpload(id, { status: 'uploading', errorMessage: undefined });
    const result = await uploadOrderAttachment(orderId, file);
    if (result.success) {
      patchUpload(id, { status: 'done' });
      return;
    }
    patchUpload(id, { status: 'failed', errorMessage: resolveErrorMessage(result.error) });
  }

  async function handleChange(event: ChangeEvent<HTMLInputElement>) {
    const selected = Array.from(event.target.files ?? []);
    event.target.value = '';
    if (selected.length === 0) return;

    const rejected: string[] = [];
    const accepted: File[] = [];
    let slots = ORDER_ATTACHMENT_MAX_FILES - occupiedSlots;

    for (const file of selected) {
      if (!ORDER_ATTACHMENT_ALLOWED_TYPES.has(file.type)) {
        rejected.push(t('checkout.attachments.rejectedType', { name: file.name }));
        continue;
      }
      if (file.size > ORDER_ATTACHMENT_MAX_BYTES) {
        rejected.push(t('checkout.attachments.rejectedSize', { name: file.name }));
        continue;
      }
      if (slots <= 0) {
        rejected.push(t('checkout.attachments.rejectedCount'));
        break;
      }
      accepted.push(file);
      slots -= 1;
    }

    setRejections(rejected);
    if (accepted.length === 0) return;

    const entries: ManagedUpload[] = accepted.map((file) => ({
      id: nextIdRef.current++,
      file,
      status: 'queued',
    }));
    setUploads((prev) => [...prev, ...entries]);
    for (const entry of entries) {
      await uploadOne(entry.id, entry.file);
    }
  }

  function handleRetry(upload: ManagedUpload) {
    if (upload.status !== 'failed') return;
    void uploadOne(upload.id, upload.file);
  }

  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-col gap-1">
        <h2 className="flex items-center gap-2 text-xs font-semibold tracking-widest text-zinc-500 uppercase">
          <Icon name="upload" size={14} className="shrink-0" />
          {t('order.page.attachments.heading')}
        </h2>
        <p className="text-xs text-zinc-500">{t('checkout.attachments.hint')}</p>
      </div>

      {rejections.length > 0 ? (
        <ul className="flex flex-col gap-1 text-sm text-error">
          {rejections.map((message) => (
            <li key={message}>{message}</li>
          ))}
        </ul>
      ) : null}

      {initialAttachments.length > 0 || uploads.length > 0 ? (
        <ul className="flex flex-col gap-2">
          {initialAttachments.map((attachment) => (
            <li
              key={attachment.id}
              className="flex items-center justify-between gap-3 rounded-xl border border-zinc-800 bg-zinc-900/60 px-4 py-2.5"
            >
              <div className="flex min-w-0 items-center gap-2">
                <Icon name="file" size={16} className="shrink-0 text-zinc-500" />
                {/* Blob download through apiFetch — `downloadUrl` is a
                    backend-relative API path, so a plain <a href> resolved
                    against the FRONTEND origin and 404'd in every
                    environment (T-0172, CUST-H1; the tracking surface's
                    own rule at order-actions-client.tsx). A navigation
                    would also drop the in-memory failed-upload retries. */}
                <FileDownloadButton
                  path={attachment.downloadUrl}
                  filename={attachment.filename}
                  label={attachment.filename}
                />
                <span className="shrink-0 text-xs text-zinc-500">
                  {formatFileSize(attachment.sizeBytes)}
                </span>
              </div>
              <Badge variant="success">{t('order.page.attachments.done')}</Badge>
            </li>
          ))}
          {uploads.map((upload) => (
            <li
              key={upload.id}
              className="flex flex-col gap-1 rounded-xl border border-zinc-800 bg-zinc-900/60 px-4 py-2.5"
            >
              <div className="flex items-center justify-between gap-3">
                <div className="flex min-w-0 items-center gap-2">
                  <Icon name="file" size={16} className="shrink-0 text-zinc-500" />
                  <span className="truncate text-sm text-zinc-200">{upload.file.name}</span>
                  <span className="shrink-0 text-xs text-zinc-500">
                    {formatFileSize(upload.file.size)}
                  </span>
                </div>
                <div className="flex shrink-0 items-center gap-2">
                  <UploadStatusBadge status={upload.status} />
                  {upload.status === 'failed' ? (
                    <Button
                      type="button"
                      variant="outline"
                      size="sm"
                      onClick={() => handleRetry(upload)}
                    >
                      {t('order.page.attachments.retry')}
                    </Button>
                  ) : null}
                </div>
              </div>
              {upload.status === 'failed' && upload.errorMessage ? (
                <p className="text-sm text-error">{upload.errorMessage}</p>
              ) : null}
            </li>
          ))}
        </ul>
      ) : null}

      <div>
        <input
          ref={inputRef}
          type="file"
          multiple
          accept={ACCEPT_ATTRIBUTE}
          onChange={handleChange}
          disabled={capReached}
          className="hidden"
          id="order-attachment-input"
        />
        <Button
          type="button"
          variant="outline"
          size="sm"
          disabled={capReached}
          onClick={() => inputRef.current?.click()}
        >
          <Icon name="upload" size={14} />
          {t('order.page.attachments.addMore')}
        </Button>
      </div>
    </div>
  );
}

function UploadStatusBadge({ status }: { readonly status: UploadStatus }) {
  switch (status) {
    case 'queued':
      return <Badge variant="default">{t('checkout.attachments.statePending')}</Badge>;
    case 'uploading':
      return <Badge variant="warning">{t('order.page.attachments.uploading')}</Badge>;
    case 'done':
      return <Badge variant="success">{t('order.page.attachments.done')}</Badge>;
    case 'failed':
      return <Badge variant="error">{t('order.page.attachments.failed')}</Badge>;
  }
}
