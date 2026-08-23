'use client';

import {
  useCallback,
  useEffect,
  useId,
  useRef,
  useState,
  type CSSProperties,
  type ReactNode,
} from 'react';
import { createPortal } from 'react-dom';

type TooltipSide = 'top' | 'bottom' | 'left' | 'right';

interface TooltipProps {
  /** Tooltip body — keep it short; it supplements, never replaces, a visible label. */
  readonly content: ReactNode;
  readonly side?: TooltipSide;
  readonly children: ReactNode;
  /** Extra classes for the inline trigger wrapper. */
  readonly className?: string;
}

const OFFSET_PX = 8;

const sideTransform: Record<TooltipSide, string> = {
  top: 'translate(-50%, -100%)',
  bottom: 'translate(-50%, 0)',
  left: 'translate(-100%, -50%)',
  right: 'translate(0, -50%)',
};

/**
 * Custom tooltip on the solid dark surface system (bg-zinc-800 panel,
 * hairline border, small arrow). Opens after a short hover/focus delay,
 * closes on leave, blur, Escape, scroll, or resize. Rendered through a
 * portal with fixed positioning so it never clips inside
 * `overflow-hidden` panels. The trigger wrapper gets
 * `aria-describedby` while open (WAI-ARIA tooltip pattern).
 */
export function Tooltip({ content, side = 'top', children, className = '' }: TooltipProps) {
  const tooltipId = useId();
  const wrapperRef = useRef<HTMLSpanElement>(null);
  const showTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const [position, setPosition] = useState<{ left: number; top: number } | null>(null);

  const hide = useCallback(() => {
    if (showTimer.current) {
      clearTimeout(showTimer.current);
      showTimer.current = null;
    }
    setPosition(null);
  }, []);

  const show = useCallback(() => {
    const rect = wrapperRef.current?.getBoundingClientRect();
    if (!rect) return;
    switch (side) {
      case 'top':
        setPosition({ left: rect.left + rect.width / 2, top: rect.top - OFFSET_PX });
        break;
      case 'bottom':
        setPosition({ left: rect.left + rect.width / 2, top: rect.bottom + OFFSET_PX });
        break;
      case 'left':
        setPosition({ left: rect.left - OFFSET_PX, top: rect.top + rect.height / 2 });
        break;
      case 'right':
        setPosition({ left: rect.right + OFFSET_PX, top: rect.top + rect.height / 2 });
        break;
    }
  }, [side]);

  const scheduleShow = useCallback(() => {
    if (showTimer.current) clearTimeout(showTimer.current);
    showTimer.current = setTimeout(show, 250);
  }, [show]);

  // A fixed-position tooltip goes stale the moment the page scrolls or
  // resizes — close instead of chasing the trigger.
  useEffect(() => {
    if (!position) return;
    window.addEventListener('scroll', hide, true);
    window.addEventListener('resize', hide);
    return () => {
      window.removeEventListener('scroll', hide, true);
      window.removeEventListener('resize', hide);
    };
  }, [position, hide]);

  useEffect(() => hide, [hide]);

  const open = position !== null;

  const arrowBySide: Record<TooltipSide, string> = {
    top: 'bottom-0 left-1/2 -ml-1 translate-y-1/2 border-b border-r',
    bottom: 'top-0 left-1/2 -ml-1 -translate-y-1/2 border-t border-l',
    left: 'right-0 top-1/2 -mt-1 translate-x-1/2 border-t border-r',
    right: 'left-0 top-1/2 -mt-1 -translate-x-1/2 border-b border-l',
  };

  const tooltipStyle: CSSProperties | undefined = position
    ? { left: position.left, top: position.top, transform: sideTransform[side] }
    : undefined;

  return (
    <span
      ref={wrapperRef}
      className={`inline-flex ${className}`}
      aria-describedby={open ? tooltipId : undefined}
      onPointerEnter={scheduleShow}
      onPointerLeave={hide}
      onFocus={scheduleShow}
      onBlur={hide}
      onKeyDown={(event) => {
        if (event.key === 'Escape') hide();
      }}
    >
      {children}
      {open &&
        createPortal(
          <span
            id={tooltipId}
            role="tooltip"
            style={tooltipStyle}
            className="pointer-events-none fixed z-50 block max-w-xs rounded-lg border border-zinc-700 bg-zinc-800 px-3 py-1.5 text-xs font-medium leading-relaxed text-zinc-100 elevated-shadow"
          >
            {content}
            <span
              aria-hidden="true"
              className={`absolute block h-2 w-2 rotate-45 border-zinc-700 bg-zinc-800 ${arrowBySide[side]}`}
            />
          </span>,
          document.body,
        )}
    </span>
  );
}
