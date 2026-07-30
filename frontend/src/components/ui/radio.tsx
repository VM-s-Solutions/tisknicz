'use client';

import { useId, type InputHTMLAttributes, type ReactNode } from 'react';

interface RadioProps extends InputHTMLAttributes<HTMLInputElement> {
  label?: ReactNode;
  description?: string;
}

/**
 * Custom radio on the solid dark surface system. Same construction as
 * `Checkbox`: real hidden `<input type="radio">` + `peer-*`-driven
 * visuals, so keyboard groups and form semantics keep working.
 */
export function Radio({ label, description, className = '', id, disabled, ...props }: RadioProps) {
  const generatedId = useId();
  const inputId = id ?? generatedId;

  return (
    <label
      htmlFor={inputId}
      className={`group flex items-start gap-3 ${disabled ? 'cursor-not-allowed opacity-60' : 'cursor-pointer'} ${className}`}
    >
      <span className="relative mt-0.5 inline-flex h-5 w-5 shrink-0">
        <input
          type="radio"
          id={inputId}
          disabled={disabled}
          className="peer absolute inset-0 h-full w-full cursor-pointer opacity-0 disabled:cursor-not-allowed"
          {...props}
        />
        <span
          aria-hidden="true"
          className="pointer-events-none absolute inset-0 rounded-full border border-zinc-600 bg-zinc-900 transition-colors duration-150 group-hover:border-zinc-400 peer-checked:border-brand-400 peer-focus-visible:ring-2 peer-focus-visible:ring-brand-400/40 peer-disabled:bg-zinc-800"
        />
        <span
          aria-hidden="true"
          className="pointer-events-none absolute inset-0 flex scale-50 items-center justify-center opacity-0 transition-all duration-150 peer-checked:scale-100 peer-checked:opacity-100"
        >
          <span className="h-2.5 w-2.5 rounded-full bg-brand-400" />
        </span>
      </span>
      {(label || description) && (
        <span className="flex flex-col gap-0.5">
          {label && <span className="text-sm text-zinc-200">{label}</span>}
          {description && <span className="text-xs leading-relaxed text-zinc-500">{description}</span>}
        </span>
      )}
    </label>
  );
}
