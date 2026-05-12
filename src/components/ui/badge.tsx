import { type HTMLAttributes } from 'react';

interface BadgeProps extends HTMLAttributes<HTMLSpanElement> {
  variant?: 'default' | 'success' | 'warning' | 'error' | 'brand';
}

const variantStyles: Record<NonNullable<BadgeProps['variant']>, string> = {
  default: 'bg-zinc-800 text-zinc-300',
  success: 'bg-emerald-950/50 text-emerald-400 border border-emerald-900/50',
  warning: 'bg-amber-950/50 text-amber-400 border border-amber-900/50',
  error: 'bg-red-950/50 text-red-400 border border-red-900/50',
  brand: 'bg-brand-400/10 text-brand-400 border border-brand-400/20',
};

export function Badge({ variant = 'default', className = '', children, ...props }: BadgeProps) {
  return (
    <span
      className={`inline-flex items-center gap-1 rounded-full px-2.5 py-0.5 text-xs font-semibold ${variantStyles[variant]} ${className}`}
      {...props}
    >
      {children}
    </span>
  );
}
