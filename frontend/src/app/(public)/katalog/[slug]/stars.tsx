import { Icon } from '@/components/ui/icon';

interface StarsProps {
  /** Display value 0–5 (already converted from basis points). */
  readonly value: number;
  readonly className?: string;
}

/**
 * Presentational star row for the maker profile header (T-0047). Pure
 * display — no business logic. Renders five glyphs: the floor of
 * <c>value</c> is filled, the rest are outline. A half-star isn't
 * modelled at MVP (the AC only requires a 1-decimal numeric display
 * alongside the glyphs).
 */
export function Stars({ value, className = '' }: StarsProps) {
  const filled = Math.max(0, Math.min(5, Math.round(value)));
  const slots = [0, 1, 2, 3, 4] as const;
  return (
    <span className={`inline-flex items-center gap-0.5 text-accent-400 ${className}`} aria-hidden>
      {slots.map((i) => (
        <Icon key={i} name={i < filled ? 'star' : 'starOutline'} size={16} />
      ))}
    </span>
  );
}
