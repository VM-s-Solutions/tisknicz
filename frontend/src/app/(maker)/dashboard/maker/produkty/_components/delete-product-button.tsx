'use client';

import { useRouter } from 'next/navigation';
import { useEffect, useRef, useState } from 'react';
import { Alert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Icon } from '@/components/ui/icon';
import { deleteProduct } from '@/lib/api-client-helpers/maker-products';
import { t } from '@/lib/i18n';

interface DeleteProductButtonProps {
  readonly productId: string;
  /**
   * <c>card</c>: compact action used inside the product card row on
   * the index.
   * <c>page</c>: full-width destructive action on the edit page; on
   * success the user is sent back to the index instead of refreshing
   * in place (the resource they were editing no longer matches the
   * "active" filter).
   */
  readonly variant?: 'card' | 'page';
}

/**
 * Confirm-then-delete control for a maker product (T-0049 AC-8). Opens
 * a lightweight inline modal — we don't have a shared <c>Dialog</c>
 * primitive yet, and <c>window.confirm</c> is explicitly off-limits per
 * the ticket spec. On confirm, calls <c>deleteProduct</c>; on success,
 * the card variant <c>router.refresh()</c>es the index (the row stays
 * but renders as inactive); the page variant pushes back to the index.
 */
export function DeleteProductButton({ productId, variant = 'card' }: DeleteProductButtonProps) {
  const router = useRouter();
  const [open, setOpen] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  // Focus-trap anchors: the Tab key cycles forward off the last button
  // into `endSentinelRef`, which redirects to the first interactive
  // (Cancel) via `firstFocusableRef`; Shift+Tab off the first cycles
  // backward through `startSentinelRef` to the last (Confirm) via
  // `lastFocusableRef`. T-0049 Copilot review M3.
  const firstFocusableRef = useRef<HTMLButtonElement | null>(null);
  const lastFocusableRef = useRef<HTMLButtonElement | null>(null);

  // Esc-to-close + lock background scroll while the modal is open. Both
  // run only on the client (this whole component is 'use client') so
  // gating on `open` keeps the body-scroll restore symmetric.
  useEffect(() => {
    if (!open) return;
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape' && !submitting) {
        setOpen(false);
      }
    };
    window.addEventListener('keydown', onKey);
    return () => {
      document.body.style.overflow = previousOverflow;
      window.removeEventListener('keydown', onKey);
    };
  }, [open, submitting]);

  async function handleConfirm() {
    setErrorMessage(null);
    setSubmitting(true);
    const result = await deleteProduct(productId);
    setSubmitting(false);
    if (!result.success) {
      setErrorMessage(t('dashboard.maker.products.delete.error'));
      return;
    }
    setOpen(false);
    if (variant === 'page') {
      router.push('/dashboard/maker/produkty');
      return;
    }
    router.refresh();
  }

  return (
    <>
      {variant === 'page' ? (
        <Button variant="danger" type="button" onClick={() => setOpen(true)}>
          <Icon name="trash" size={16} />
          {t('dashboard.maker.products.delete.button')}
        </Button>
      ) : (
        <button
          type="button"
          onClick={() => setOpen(true)}
          className="inline-flex items-center gap-1.5 rounded-lg border border-error/40 bg-error-fill-soft px-3.5 py-1.5 text-sm font-semibold text-error transition-colors hover:bg-error-fill-soft-strong focus:outline-none focus-visible:ring-2 focus-visible:ring-error focus-visible:ring-offset-2 focus-visible:ring-offset-surface-primary"
        >
          <Icon name="trash" size={14} />
          {t('dashboard.maker.products.actions.delete')}
        </button>
      )}

      {open ? (
        <div
          role="dialog"
          aria-modal="true"
          aria-labelledby={`delete-${productId}-title`}
          aria-describedby={`delete-${productId}-description`}
          className="fixed inset-0 z-50 flex items-center justify-center p-4"
        >
          {/* Backdrop: non-focusable, hidden from a11y tree. The
              accessible close path is the Cancel button + Esc; the
              click-outside is a mouse-only convenience. T-0049 Copilot
              review M2. */}
          <div
            aria-hidden="true"
            onClick={() => {
              if (!submitting) setOpen(false);
            }}
            className="absolute inset-0 bg-black/70"
          />
          {/* Start sentinel: Shift+Tab off Cancel lands here, redirects
              to the last interactive (Confirm). T-0049 Copilot review M3. */}
          <div
            tabIndex={0}
            onFocus={() => lastFocusableRef.current?.focus()}
          />
          <div className="relative z-10 w-full max-w-md rounded-xl border border-zinc-800 bg-surface-card p-6 shadow-2xl">
            <h2
              id={`delete-${productId}-title`}
              className="text-lg font-semibold text-zinc-50"
            >
              {t('dashboard.maker.products.delete.confirm.title')}
            </h2>
            <p
              id={`delete-${productId}-description`}
              className="mt-2 text-sm text-zinc-400"
            >
              {t('dashboard.maker.products.delete.confirm.body')}
            </p>
            {errorMessage ? (
              <div className="mt-4">
                <Alert variant="error">{errorMessage}</Alert>
              </div>
            ) : null}
            <div className="mt-6 flex items-center justify-end gap-3">
              {/* autoFocus lands keyboard focus on the safe action when
                  the dialog opens, so the user is inside the modal
                  (and one Tab away from Confirm) rather than stranded
                  behind the overlay. T-0049 Copilot review M2. */}
              <Button
                ref={firstFocusableRef}
                type="button"
                variant="outline"
                onClick={() => setOpen(false)}
                disabled={submitting}
                autoFocus
              >
                {t('dashboard.maker.products.delete.confirm.cancel_button')}
              </Button>
              <Button
                ref={lastFocusableRef}
                type="button"
                variant="danger"
                onClick={handleConfirm}
                loading={submitting}
              >
                {t('dashboard.maker.products.delete.confirm.confirm_button')}
              </Button>
            </div>
          </div>
          {/* End sentinel: Tab off Confirm lands here, redirects to the
              first interactive (Cancel). T-0049 Copilot review M3. */}
          <div
            tabIndex={0}
            onFocus={() => firstFocusableRef.current?.focus()}
          />
        </div>
      ) : null}
    </>
  );
}
