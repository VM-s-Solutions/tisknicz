'use client';

import { useEffect, useRef, type ReactNode } from 'react';

/** Elements that can hold focus inside a dialog. */
const FOCUSABLE =
  'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

interface DialogProps {
  readonly titleId: string;
  readonly title: string;
  readonly onClose: () => void;
  /** True while a request is in flight — Esc/backdrop must not dismiss it. */
  readonly closeDisabled?: boolean;
  readonly children: ReactNode;
}

/**
 * The shared admin dialog (T-0176, audit ADM-M10). Three separate shells
 * — the order-detail modal, the complete-batch modal and the
 * country-config confirm — each carried a comment claiming a "focus-trap
 * pattern" while implementing none: focus stayed on the trigger behind
 * the backdrop, Tab walked into the obscured page, and screen-reader
 * users could leave the dialog without closing it.
 *
 * This one actually traps: initial focus moves inside on mount, Tab and
 * Shift+Tab cycle within, Escape closes (unless a request is in flight),
 * and focus returns to whatever opened it on unmount. Background scroll
 * stays locked.
 */
export function Dialog({ titleId, title, onClose, closeDisabled = false, children }: DialogProps) {
  const panelRef = useRef<HTMLDivElement | null>(null);
  const returnFocusRef = useRef<HTMLElement | null>(null);

  useEffect(() => {
    returnFocusRef.current = document.activeElement as HTMLElement | null;
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';

    // Initial focus inside the dialog — without it the keyboard stays
    // behind the backdrop.
    const focusables = () =>
      Array.from(panelRef.current?.querySelectorAll<HTMLElement>(FOCUSABLE) ?? []);
    (focusables()[0] ?? panelRef.current)?.focus({ preventScroll: true });

    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape' && !closeDisabled) {
        onClose();
        return;
      }
      if (event.key !== 'Tab') return;
      const items = focusables();
      if (items.length === 0) {
        event.preventDefault();
        return;
      }
      const first = items[0];
      const last = items[items.length - 1];
      const active = document.activeElement;
      // Wrap at both ends, and pull focus back in if it escaped.
      if (event.shiftKey && (active === first || !panelRef.current?.contains(active))) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && (active === last || !panelRef.current?.contains(active))) {
        event.preventDefault();
        first.focus();
      }
    };

    window.addEventListener('keydown', onKey);
    return () => {
      document.body.style.overflow = previousOverflow;
      window.removeEventListener('keydown', onKey);
      returnFocusRef.current?.focus({ preventScroll: true });
    };
  }, [onClose, closeDisabled]);

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-labelledby={titleId}
      className="fixed inset-0 z-50 flex items-center justify-center overflow-y-auto p-4"
    >
      <div
        aria-hidden="true"
        onClick={() => {
          if (!closeDisabled) onClose();
        }}
        className="absolute inset-0 bg-black/70"
      />
      <div
        ref={panelRef}
        tabIndex={-1}
        className="relative z-10 my-8 w-full max-w-lg rounded-xl border border-zinc-800 bg-surface-card p-6 shadow-2xl"
      >
        <h2 id={titleId} className="text-lg font-semibold text-white">
          {title}
        </h2>
        <div className="mt-4 flex flex-col gap-4">{children}</div>
      </div>
    </div>
  );
}
