'use client';

import { useId, type CSSProperties, type ReactNode } from 'react';

interface RangeSliderProps {
  readonly min: number;
  readonly max: number;
  readonly step?: number;
  readonly value: number;
  readonly onChange: (value: number) => void;
  readonly label?: ReactNode;
  /** Right-aligned readout next to the label (e.g. "4+ hvězd"). */
  readonly valueLabel?: ReactNode;
  /** Spoken value — the number alone is rarely meaningful. */
  readonly ariaValueText?: string;
  readonly disabled?: boolean;
  readonly id?: string;
}

/**
 * Single-value slider on the solid dark surface system. Built from the
 * same parts as {@link Switch} — zinc-800 track, brand-500 fill,
 * hairline focus ring — because native `<input type="range">` renders
 * browser chrome that matches nothing else on the page.
 *
 * The knob is `brand-300`, the primary button's text colour, so the
 * control reads as brand rather than as a bright neutral. It is
 * deliberately NOT `brand-500`: that is the fill colour, and a knob in
 * the fill colour vanishes across the filled half of the track.
 * `brand-300` stays legible against both the fill and the zinc-800
 * remainder.
 *
 * A real range input drives it (full keyboard, drag, and click-to-seek
 * behaviour, plus the correct AT role); it sits transparent on top while
 * three sibling spans paint the visible control. The spans are siblings
 * rather than children so Tailwind's `peer-*` selectors can reach the
 * knob from the input's focus state.
 *
 * The travel is inset by half a thumb at each end so the knob stays
 * inside the control at both extremes. The thumb is 1rem (`h-4 w-4`)
 * and that figure is baked into `inset-x-2`, `left-2` and both
 * `calc()` offsets — Tailwind cannot read a JS constant, so resizing the
 * knob means changing all four together.
 */
export function RangeSlider({
  min,
  max,
  step = 1,
  value,
  onChange,
  label,
  valueLabel,
  ariaValueText,
  disabled = false,
  id,
}: RangeSliderProps) {
  const generatedId = useId();
  const inputId = id ?? generatedId;

  const span = max - min;
  const fraction = span > 0 ? Math.min(1, Math.max(0, (value - min) / span)) : 0;
  // Only the fraction crosses into CSS; the geometry stays in classes.
  const style = { '--range-fraction': String(fraction) } as CSSProperties;

  return (
    <div className="flex flex-col gap-1.5">
      {(label || valueLabel) && (
        <div className="flex items-baseline justify-between gap-2">
          {label && (
            <label htmlFor={inputId} className="text-sm font-medium text-zinc-300">
              {label}
            </label>
          )}
          {valueLabel && <span className="text-xs font-medium text-zinc-400">{valueLabel}</span>}
        </div>
      )}

      <div
        style={style}
        className={`relative flex h-6 w-full items-center ${disabled ? 'opacity-60' : ''}`}
      >
        <input
          id={inputId}
          type="range"
          min={min}
          max={max}
          step={step}
          value={value}
          disabled={disabled}
          aria-valuetext={ariaValueText}
          onChange={(event) => onChange(Number(event.target.value))}
          className="peer absolute inset-0 z-10 h-full w-full cursor-pointer opacity-0 disabled:cursor-not-allowed"
        />
        <span
          aria-hidden="true"
          className="pointer-events-none absolute inset-x-2 h-1.5 rounded-full bg-zinc-800"
        />
        <span
          aria-hidden="true"
          className="pointer-events-none absolute left-2 h-1.5 w-[calc((100%_-_1rem)_*_var(--range-fraction))] rounded-full bg-brand-500"
        />
        <span
          aria-hidden="true"
          className="pointer-events-none absolute left-[calc((100%_-_1rem)_*_var(--range-fraction))] h-4 w-4 rounded-full bg-brand-300 shadow-md ring-brand-400/40 transition-colors duration-150 peer-hover:bg-brand-200 peer-focus-visible:ring-2"
        />
      </div>
    </div>
  );
}
