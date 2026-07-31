import { type HTMLAttributes } from 'react';

interface AlertProps extends HTMLAttributes<HTMLDivElement> {
  variant?: 'info' | 'success' | 'warning' | 'error';
}

/**
 * Callout on the solid ink surface: a colored left rule + icon carry
 * the severity, while the body text stays high-contrast neutral.
 *
 * Severity colours come from the semantic tokens in `globals.css`, so an
 * alert and a badge of the same severity are the same colour.
 */
const variantStyles: Record<NonNullable<AlertProps['variant']>, { accent: string; icon: string }> = {
  info: {
    accent: 'border-l-info text-info',
    icon: 'M13 16h-1v-4h-1m1-4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z',
  },
  success: {
    accent: 'border-l-success text-success',
    icon: 'M9 12l2 2 4-4m6 2a9 9 0 11-18 0 9 9 0 0118 0z',
  },
  warning: {
    accent: 'border-l-warning text-warning',
    icon: 'M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-2.5L13.732 4c-.77-.833-1.964-.833-2.732 0L3.34 16.5c-.77.833.192 2.5 1.732 2.5z',
  },
  error: {
    accent: 'border-l-error text-error',
    icon: 'M10 14l2-2m0 0l2-2m-2 2l-2-2m2 2l2 2m7-2a9 9 0 11-18 0 9 9 0 0118 0z',
  },
};

export function Alert({ variant = 'info', className = '', children, ...props }: AlertProps) {
  const styles = variantStyles[variant];

  return (
    <div
      className={`flex items-start gap-3 rounded-lg border border-l-2 border-zinc-800 bg-surface-card p-4 text-sm ${styles.accent} ${className}`}
      role="alert"
      {...props}
    >
      <svg className="mt-0.5 h-5 w-5 shrink-0" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round">
        <path d={styles.icon} />
      </svg>
      <div className="text-zinc-200">{children}</div>
    </div>
  );
}
