'use client';

import { type ButtonHTMLAttributes, type Ref } from 'react';

interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: 'primary' | 'secondary' | 'outline' | 'ghost' | 'danger' | 'dangerGhost';
  size?: 'sm' | 'md' | 'lg';
  loading?: boolean;
  /**
   * React 19 lets function components accept `ref` as a regular prop
   * (no `forwardRef` indirection). Threaded through to the underlying
   * <c>&lt;button&gt;</c> so callers can imperatively focus / measure
   * — e.g. the delete-modal's focus trap (T-0049).
   */
  ref?: Ref<HTMLButtonElement>;
}

/**
 * Hairline button system. No variant ships a solid block of colour — a
 * button is a bordered outline whose *border and text* carry the weight,
 * and hover adds only a faint tint. Rank reads through colour, not fill:
 * brand hairline (primary) > bright neutral (secondary) > muted neutral
 * (outline) > borderless (ghost).
 *
 * Squared-ish corners (rounded-lg, never a full pill), colour-only hover
 * feedback — no movement, no glow.
 *
 * Two destructive weights: `danger` is the high-commitment one (delete
 * account, cancel order) and is the single exception that carries a tinted
 * fill at rest, so it can never be mistaken for a neutral action;
 * `dangerGhost` is the quiet one for reversible things like logout.
 */
const variantStyles: Record<NonNullable<ButtonProps['variant']>, string> = {
  primary:
    'border border-brand-500/60 bg-transparent font-semibold text-brand-300 hover:border-brand-500 hover:bg-tint-brand hover:text-on-tint-brand active:bg-tint-brand-strong',
  secondary:
    'border border-zinc-700 bg-transparent font-medium text-zinc-100 hover:border-zinc-600 hover:bg-zinc-800/60 active:bg-zinc-800',
  outline:
    'border border-zinc-700 bg-transparent font-medium text-zinc-300 hover:border-brand-500/60 hover:text-brand-300 active:border-brand-400',
  ghost:
    'border border-transparent bg-transparent font-medium text-zinc-300 hover:bg-zinc-800/60 hover:text-zinc-50',
  danger:
    'border border-error/50 bg-tint-error font-semibold text-on-tint-error hover:border-error/70 hover:bg-tint-error-strong',
  dangerGhost:
    'border border-error/30 bg-transparent font-medium text-error hover:border-error/50 hover:bg-tint-error hover:text-on-tint-error',
};

const sizeStyles: Record<NonNullable<ButtonProps['size']>, string> = {
  sm: 'px-3 py-1.5 text-sm',
  md: 'px-4 py-2 text-sm',
  lg: 'px-6 py-2.5 text-base',
};

export function Button({
  variant = 'primary',
  size = 'md',
  loading = false,
  disabled,
  className = '',
  children,
  ...props
}: ButtonProps) {
  return (
    <button
      className={`inline-flex items-center justify-center gap-2 rounded-lg tracking-wide transition-colors duration-150 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-400/60 focus-visible:ring-offset-2 focus-visible:ring-offset-surface-primary disabled:cursor-not-allowed disabled:opacity-50 ${variantStyles[variant]} ${sizeStyles[size]} ${className}`}
      disabled={disabled || loading}
      {...props}
    >
      {loading && (
        <svg className="animate-spin h-4 w-4" viewBox="0 0 24 24" fill="none">
          <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
          <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
        </svg>
      )}
      {children}
    </button>
  );
}
