'use client';

import { useId, type InputHTMLAttributes, type ReactNode } from 'react';
import { Icon } from '@/components/ui/icon';

interface CheckboxProps extends InputHTMLAttributes<HTMLInputElement> {
  label?: ReactNode;
  description?: string;
  error?: string;
}

/**
 * Custom checkbox on the solid dark surface system: a real (visually
 * hidden) `<input type="checkbox">` for full form/keyboard/AT support,
 * with a styled box driven purely by `peer-*` CSS so it works both
 * controlled and uncontrolled.
 */
export function Checkbox({
  label,
  description,
  error,
  className = '',
  id,
  disabled,
  ...props
}: CheckboxProps) {
  const generatedId = useId();
  const inputId = id ?? generatedId;

  return (
    <div className={`flex flex-col gap-1.5 ${className}`}>
      <label
        htmlFor={inputId}
        className={`group flex items-start gap-3 ${disabled ? 'cursor-not-allowed opacity-60' : 'cursor-pointer'}`}
      >
        <span className="relative mt-0.5 inline-flex h-5 w-5 shrink-0">
          <input
            type="checkbox"
            id={inputId}
            disabled={disabled}
            className="peer absolute inset-0 h-full w-full cursor-pointer opacity-0 disabled:cursor-not-allowed"
            {...props}
          />
          <span
            aria-hidden="true"
            className={`pointer-events-none absolute inset-0 rounded-md border bg-zinc-900 transition-colors duration-150 peer-checked:border-brand-400 peer-checked:bg-brand-500 peer-focus-visible:ring-2 peer-focus-visible:ring-brand-400/40 peer-disabled:bg-zinc-800 ${
              error ? 'border-error' : 'border-zinc-600 group-hover:border-zinc-400'
            }`}
          />
          <span
            aria-hidden="true"
            className="pointer-events-none absolute inset-0 flex items-center justify-center text-on-brand opacity-0 peer-checked:opacity-100"
          >
            <Icon name="check" size={13} strokeWidth={3} />
          </span>
        </span>
        {(label || description) && (
          <span className="flex flex-col gap-0.5">
            {label && <span className="text-sm text-zinc-200">{label}</span>}
            {description && <span className="text-xs leading-relaxed text-zinc-500">{description}</span>}
          </span>
        )}
      </label>
      {error && <p className="text-sm text-error">{error}</p>}
    </div>
  );
}
