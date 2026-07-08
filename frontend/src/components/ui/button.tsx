'use client';

import { type ButtonHTMLAttributes, type Ref } from 'react';

interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: 'primary' | 'secondary' | 'outline' | 'ghost' | 'danger';
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
 * Hairline button system: transparent surfaces, 1px borders, colored
 * text and a soft glow on hover instead of solid fills. `ghost` is the
 * only borderless variant (pure text with an animated underline).
 */
const variantStyles: Record<NonNullable<ButtonProps['variant']>, string> = {
  primary:
    'border border-brand-500/60 bg-transparent text-brand-300 hover:border-brand-400 hover:text-brand-200 hover:shadow-lg hover:shadow-brand-500/20 active:border-brand-300',
  secondary:
    'border border-zinc-700 bg-transparent text-zinc-200 hover:border-zinc-500 hover:text-white hover:shadow-lg hover:shadow-zinc-500/10 active:border-zinc-400',
  outline:
    'border border-zinc-700 bg-transparent text-zinc-300 hover:border-brand-500/50 hover:text-brand-200 active:border-brand-400',
  ghost:
    'link-underline border border-transparent bg-transparent text-zinc-300 hover:text-white',
  danger:
    'border border-red-500/50 bg-transparent text-red-300 hover:border-red-400 hover:text-red-200 hover:shadow-lg hover:shadow-red-500/20 active:border-red-300',
};

const sizeStyles: Record<NonNullable<ButtonProps['size']>, string> = {
  sm: 'px-3.5 py-2 text-sm',
  md: 'px-5 py-2.5 text-sm',
  lg: 'px-7 py-3.5 text-base',
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
      className={`inline-flex items-center justify-center gap-2 rounded-full font-medium tracking-wide transition-all duration-200 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-400/50 focus-visible:ring-offset-2 focus-visible:ring-offset-surface-primary disabled:cursor-not-allowed disabled:opacity-50 disabled:shadow-none ${variantStyles[variant]} ${sizeStyles[size]} ${className}`}
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
