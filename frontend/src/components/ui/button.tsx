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

const variantStyles: Record<NonNullable<ButtonProps['variant']>, string> = {
  primary: 'border border-brand-500 bg-brand-500 text-zinc-950 hover:bg-brand-400 active:bg-brand-300',
  secondary: 'border border-zinc-700 bg-zinc-800 text-zinc-100 hover:border-zinc-600 hover:bg-zinc-700 active:bg-zinc-600',
  outline: 'border border-zinc-700 bg-transparent text-zinc-200 hover:border-zinc-500 hover:bg-zinc-800 active:bg-zinc-700',
  ghost: 'border border-transparent bg-transparent text-zinc-300 hover:bg-zinc-800 hover:text-zinc-100 active:bg-zinc-700',
  danger: 'border border-red-700 bg-red-800 text-red-50 hover:bg-red-700 active:bg-red-600',
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
      className={`inline-flex items-center justify-center gap-2 rounded-lg font-semibold transition-colors duration-200 focus:outline-none focus:ring-2 focus:ring-brand-500/50 focus:ring-offset-2 focus:ring-offset-zinc-900 disabled:cursor-not-allowed disabled:opacity-50 ${variantStyles[variant]} ${sizeStyles[size]} ${className}`}
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
