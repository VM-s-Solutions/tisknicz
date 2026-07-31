import { type HTMLAttributes } from 'react';

interface BadgeProps extends HTMLAttributes<HTMLSpanElement> {
  variant?: 'default' | 'success' | 'warning' | 'error' | 'brand' | 'info';
  /**
   * Leading status dot. On by default for stateful labels; turn off
   * when the badge carries its own icon or is a plain text label.
   */
  dot?: boolean;
}

/**
 * Label chip: squared corners, quiet 12px text, a tinted hairline border
 * and a faint tint of its own hue; an optional dot carries the status.
 *
 * Colours come from the semantic tokens in `globals.css`, never a stock
 * Tailwind ramp — those sit off-hue against the 197° neutrals.
 *
 * The fill is a tint of the badge's own colour rather than a fixed ink
 * surface. A fixed surface only works on one background: `bg-zinc-900`
 * matches the page exactly, so chips vanished on the page and punched a
 * dark hole through cards. A self-tint reads as raised on every surface.
 */
const variantStyles: Record<NonNullable<BadgeProps['variant']>, { chip: string; dot: string }> = {
  default: { chip: 'border-zinc-700 bg-zinc-800/60 text-zinc-300', dot: 'bg-zinc-500' },
  info: { chip: 'border-info/30 bg-info/10 text-info', dot: 'bg-info' },
  success: { chip: 'border-success/30 bg-success/10 text-success', dot: 'bg-success' },
  warning: { chip: 'border-warning/30 bg-warning/10 text-warning', dot: 'bg-warning' },
  error: { chip: 'border-error/30 bg-error/10 text-error', dot: 'bg-error' },
  brand: { chip: 'border-brand-500/40 bg-brand-500/10 text-brand-300', dot: 'bg-brand-400' },
};

export function Badge({ variant = 'default', dot = true, className = '', children, ...props }: BadgeProps) {
  const styles = variantStyles[variant];
  return (
    <span
      className={`inline-flex items-center gap-1.5 rounded-md border px-2 py-1 text-xs font-medium leading-none ${styles.chip} ${className}`}
      {...props}
    >
      {dot && <span aria-hidden="true" className={`h-1.5 w-1.5 shrink-0 rounded-full ${styles.dot}`} />}
      {children}
    </span>
  );
}
