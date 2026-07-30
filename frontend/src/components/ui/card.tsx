import { type HTMLAttributes } from 'react';

interface CardProps extends HTMLAttributes<HTMLDivElement> {
  padding?: 'none' | 'sm' | 'md' | 'lg';
  hover?: boolean;
  /**
   * Surface treatment. `default` keeps the original flat card;
   * `elevated` uses the lit `.panel` gradient surface; `accent` adds
   * the teal top hairline for the one primary surface on a page.
   */
  variant?: 'default' | 'elevated' | 'accent';
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
  accent: 'panel panel-accent',
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
      className={`rounded-2xl border border-zinc-800 ${variantStyles[variant]} ${hover ? 'card-lift' : ''} ${paddingStyles[padding]} ${className}`}
      {...props}
    >
      {children}
    </div>
  );
}
