'use client';

import { useId, type InputHTMLAttributes, type ReactNode } from 'react';

interface SwitchProps extends InputHTMLAttributes<HTMLInputElement> {
  label?: ReactNode;
  description?: string;
}

/**
 * Custom on/off switch on the solid dark surface system. A hidden
 * `<input type="checkbox" role="switch">` drives a teal track + sliding
 * thumb via `peer-*` CSS. Use for immediate on/off settings (consent
 * toggles, availability flags); use `Checkbox` inside submitted forms.
 */
export function Switch({ label, description, className = '', id, disabled, ...props }: SwitchProps) {
  const generatedId = useId();
  const inputId = id ?? generatedId;

  return (
    <label
      htmlFor={inputId}
      className={`group flex items-center gap-3 ${disabled ? 'cursor-not-allowed opacity-60' : 'cursor-pointer'} ${className}`}
    >
      <span className="relative inline-flex h-6 w-11 shrink-0">
        <input
          type="checkbox"
          role="switch"
          id={inputId}
          disabled={disabled}
          className="peer absolute inset-0 h-full w-full cursor-pointer opacity-0 disabled:cursor-not-allowed"
          {...props}
        />
        <span
          aria-hidden="true"
          className="pointer-events-none absolute inset-0 rounded-full border border-zinc-600 bg-zinc-800 transition-colors duration-200 group-hover:border-zinc-400 peer-checked:border-brand-400 peer-checked:bg-brand-500 peer-focus-visible:ring-2 peer-focus-visible:ring-brand-400/40"
        />
        <span
          aria-hidden="true"
          className="pointer-events-none absolute left-0.5 top-0.5 h-5 w-5 rounded-full bg-zinc-100 shadow-md peer-checked:translate-x-5"
        />
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
