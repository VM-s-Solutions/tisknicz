import { Icon } from '@/components/ui/icon';

interface StarsProps {
  /** Display value 0–5 (already converted from basis points). */
  readonly value: number;
  readonly size?: number;
  readonly className?: string;
}

/**
 * Presentational star row (T-0047; promoted from the maker profile to a
 * shared primitive once product ratings shipped). Pure display — no
 * business logic. Renders five glyphs: <c>Math.floor</c> of
 * <c>value</c> is filled, the rest are outline (e.g. 4.4 and 4.9 both
 * render as four filled stars). The fractional part shows up in the
 * 1-decimal numeric display next to the glyphs (AC-1) so users still
 * see the precision; the row itself stays integer-valued and never
 * over-promises.
 */
export function Stars({ value, size = 16, className = '' }: StarsProps) {
  const filled = Math.max(0, Math.min(5, Math.floor(value)));
  const slots = [0, 1, 2, 3, 4] as const;
  return (
    <span className={`inline-flex items-center gap-0.5 text-accent-400 ${className}`} aria-hidden>
      {slots.map((i) => (
        <Icon key={i} name={i < filled ? 'star' : 'starOutline'} size={size} />
      ))}
    </span>
  );
}
