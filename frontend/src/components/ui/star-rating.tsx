'use client';

import { useState } from 'react';
import { Icon } from '@/components/ui/icon';

/**
 * Hand-built star primitive (T-0115) — two variants in one component, no
 * third-party library. The interactive variant (`onChange` provided) is a
 * keyboard-operable `role="radiogroup"` over the existing `star` /
 * `starOutline` icons; the display variant (`onChange` omitted) is a
 * non-interactive filled/outline render for the read-only block. Whole
 * stars only (Rating is SMALLINT 1..5) — no half-star rendering at MVP.
 *
 * No business logic lives here: the value is a 1..5 integer the parent
 * owns; the backend stays authoritative for the rating itself.
 */

const STARS = [1, 2, 3, 4, 5] as const;

interface StarRatingBaseProps {
  /** Selected/displayed value, 1..5 (0 = nothing selected for the interactive variant). */
  readonly value: number;
  readonly size?: 'sm' | 'md';
  readonly className?: string;
}

interface InteractiveProps extends StarRatingBaseProps {
  readonly onChange: (value: number) => void;
  /** i18n label per star value (1..5) for the radio aria-labels. */
  readonly ariaLabelFor: (value: number) => string;
  /** Accessible name for the radiogroup. */
  readonly groupLabel: string;
  readonly disabled?: boolean;
}

interface DisplayProps extends StarRatingBaseProps {
  readonly onChange?: undefined;
}

type StarRatingProps = InteractiveProps | DisplayProps;

const SIZE_PX: Record<NonNullable<StarRatingBaseProps['size']>, number> = {
  sm: 16,
  md: 24,
};

export function StarRating(props: StarRatingProps) {
  if (props.onChange === undefined) {
    return <StarRatingDisplay {...props} />;
  }
  return <StarRatingInteractive {...props} />;
}

function StarRatingDisplay({ value, size = 'md', className = '' }: DisplayProps) {
  const filled = Math.max(0, Math.min(5, Math.round(value)));
  const px = SIZE_PX[size];
  return (
    <span
      className={`inline-flex items-center gap-0.5 text-accent-400 ${className}`}
      aria-hidden="true"
    >
      {STARS.map((star) => (
        <Icon key={star} name={star <= filled ? 'star' : 'starOutline'} size={px} />
      ))}
    </span>
  );
}

function StarRatingInteractive({
  value,
  size = 'md',
  className = '',
  onChange,
  ariaLabelFor,
  groupLabel,
  disabled = false,
}: InteractiveProps) {
  const [hovered, setHovered] = useState(0);
  const px = SIZE_PX[size];
  const active = hovered > 0 ? hovered : value;

  function handleKeyDown(event: React.KeyboardEvent<HTMLDivElement>) {
    if (disabled) return;
    if (event.key === 'ArrowRight' || event.key === 'ArrowUp') {
      event.preventDefault();
      onChange(Math.min(5, (value || 0) + 1));
    } else if (event.key === 'ArrowLeft' || event.key === 'ArrowDown') {
      event.preventDefault();
      onChange(Math.max(1, (value || 1) - 1));
    }
  }

  return (
    <div
      role="radiogroup"
      aria-label={groupLabel}
      onKeyDown={handleKeyDown}
      className={`inline-flex items-center gap-1 ${className}`}
    >
      {STARS.map((star) => {
        const isSelected = value === star;
        const isLit = star <= active;
        return (
          <button
            key={star}
            type="button"
            role="radio"
            aria-checked={isSelected}
            aria-label={ariaLabelFor(star)}
            disabled={disabled}
            // Only the selected radio is tab-focusable; when nothing is
            // selected the first star takes focus (roving-tabindex pattern).
            tabIndex={isSelected || (value === 0 && star === 1) ? 0 : -1}
            onClick={() => onChange(star)}
            onMouseEnter={() => setHovered(star)}
            onMouseLeave={() => setHovered(0)}
            onFocus={() => setHovered(star)}
            onBlur={() => setHovered(0)}
            className={`rounded-md p-0.5 transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-400/40 disabled:cursor-not-allowed disabled:opacity-60 ${
              isLit ? 'text-accent-400' : 'text-zinc-500 hover:text-accent-400/60'
            }`}
          >
            <Icon name={isLit ? 'star' : 'starOutline'} size={px} />
          </button>
        );
      })}
    </div>
  );
}
