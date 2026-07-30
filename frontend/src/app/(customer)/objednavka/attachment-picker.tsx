'use client';

import { useRef, useState, type ChangeEvent } from 'react';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Icon } from '@/components/ui/icon';
import { formatFileSize } from '@/lib/format/file-size';
import { t } from '@/lib/i18n';
import {
  ORDER_ATTACHMENT_ALLOWED_TYPES,
  ORDER_ATTACHMENT_MAX_BYTES,
  ORDER_ATTACHMENT_MAX_FILES,
} from '@/lib/utils/validation';

/**
 * Multi-file attachment picker for the checkout form (T-0084a, Q2
 * lock). Files are held client-side and uploaded by the form AFTER
 * CreateOrder succeeds; this component owns only the pre-checks
 * (type/size/count — UX mirrors of the T-0064 validator, backend
 * authoritative) and the per-file status display during submit
 * (čeká → nahrává se → hotovo | chyba).
 */

export type CheckoutAttachmentStatus = 'pending' | 'uploading' | 'done' | 'failed';

export interface CheckoutAttachment {
  readonly file: File;
  readonly status: CheckoutAttachmentStatus;
}

const STATUS_BADGE: Record<
  CheckoutAttachmentStatus,
  { readonly labelKey: 'checkout.attachments.statePending' | 'checkout.attachments.stateUploading' | 'checkout.attachments.stateDone' | 'checkout.attachments.stateFailed'; readonly variant: 'default' | 'warning' | 'success' | 'error' }
> = {
  pending: { labelKey: 'checkout.attachments.statePending', variant: 'default' },
  uploading: { labelKey: 'checkout.attachments.stateUploading', variant: 'warning' },
  done: { labelKey: 'checkout.attachments.stateDone', variant: 'success' },
  failed: { labelKey: 'checkout.attachments.stateFailed', variant: 'error' },
};

const ACCEPT_ATTRIBUTE = [...ORDER_ATTACHMENT_ALLOWED_TYPES].join(',');

interface AttachmentPickerProps {
  readonly attachments: readonly CheckoutAttachment[];
  readonly onAdd: (files: readonly File[]) => void;
  readonly onRemove: (index: number) => void;
  readonly disabled: boolean;
}

export function AttachmentPicker({ attachments, onAdd, onRemove, disabled }: AttachmentPickerProps) {
  const inputRef = useRef<HTMLInputElement | null>(null);
  const [rejections, setRejections] = useState<readonly string[]>([]);

  function handleChange(event: ChangeEvent<HTMLInputElement>) {
    const selected = Array.from(event.target.files ?? []);
    // Reset so re-selecting the same file re-fires onChange (same
    // convention as the maker image-manager).
    event.target.value = '';
    if (selected.length === 0) return;

    const rejected: string[] = [];
    const accepted: File[] = [];
    let slots = ORDER_ATTACHMENT_MAX_FILES - attachments.length;

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
    if (accepted.length > 0) {
      onAdd(accepted);
    }
  }

  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-col gap-1">
        <span className="text-sm font-medium text-zinc-400">{t('checkout.attachments.label')}</span>
        <p className="text-xs text-zinc-500">{t('checkout.attachments.hint')}</p>
      </div>

      {rejections.length > 0 ? (
        <ul className="flex flex-col gap-1 text-sm text-error">
          {rejections.map((message) => (
            <li key={message}>{message}</li>
          ))}
        </ul>
      ) : null}

      {attachments.length > 0 ? (
        <ul className="flex flex-col gap-2">
          {attachments.map((attachment, index) => {
            const badge = STATUS_BADGE[attachment.status];
            return (
              <li
                key={`${attachment.file.name}-${index}`}
                className="flex items-center justify-between gap-3 rounded-xl border border-zinc-800 bg-zinc-900/60 px-4 py-2.5"
              >
                <div className="flex min-w-0 items-center gap-2">
                  <Icon name="file" size={16} className="shrink-0 text-zinc-500" />
                  <span className="truncate text-sm text-zinc-200">{attachment.file.name}</span>
                  <span className="shrink-0 text-xs text-zinc-500">
                    {formatFileSize(attachment.file.size)}
                  </span>
                </div>
                <div className="flex shrink-0 items-center gap-2">
                  <Badge variant={badge.variant}>{t(badge.labelKey)}</Badge>
                  {attachment.status === 'pending' && !disabled ? (
                    <Button type="button" variant="ghost" size="sm" onClick={() => onRemove(index)}>
                      <Icon name="x" size={14} />
                      {t('checkout.attachments.remove')}
                    </Button>
                  ) : null}
                </div>
              </li>
            );
          })}
        </ul>
      ) : null}

      <div>
        <input
          ref={inputRef}
          type="file"
          multiple
          accept={ACCEPT_ATTRIBUTE}
          onChange={handleChange}
          disabled={disabled}
          className="hidden"
          id="checkout-attachment-input"
        />
        <Button
          type="button"
          variant="outline"
          size="sm"
          disabled={disabled || attachments.length >= ORDER_ATTACHMENT_MAX_FILES}
          onClick={() => inputRef.current?.click()}
        >
          <Icon name="upload" size={14} />
          {t('checkout.attachments.add')}
        </Button>
      </div>
    </div>
  );
}
