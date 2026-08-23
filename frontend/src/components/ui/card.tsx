import { type HTMLAttributes } from 'react';

interface CardProps extends HTMLAttributes<HTMLDivElement> {
  padding?: 'none' | 'sm' | 'md' | 'lg';
  hover?: boolean;
  /**
   * Surface treatment. Both are flat fills of `--color-surface-card` —
   * elevation reads through the hairline border, never a gradient, a
   * shadow or an accent bar.
   */
  variant?: 'default' | 'elevated';
}

const paddingStyles = {
  none: '',
  sm: 'p-4',
  md: 'p-6',
  lg: 'p-8',
};

const variantStyles: Record<NonNullable<CardProps['variant']>, string> = {
  default: 'bg-surface-card',
  elevated: 'panel',
};

export function Card({
  padding = 'md',
  hover = false,
  variant = 'default',
  className = '',
  children,
  ...props
}: CardProps) {
  return (
    <div
      className={`rounded-xl border border-zinc-800 ${variantStyles[variant]} ${hover ? 'card-lift' : ''} ${paddingStyles[padding]} ${className}`}
      {...props}
    >
      {children}
    </div>
  );
}
