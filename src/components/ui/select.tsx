'use client';

import { type SelectHTMLAttributes, forwardRef } from 'react';

interface SelectProps extends SelectHTMLAttributes<HTMLSelectElement> {
  label?: string;
  error?: string;
  options: { value: string; label: string }[];
  placeholder?: string;
}

export const Select = forwardRef<HTMLSelectElement, SelectProps>(
  function Select({ label, error, options, placeholder, className = '', id, ...props }, ref) {
    const selectId = id ?? label?.toLowerCase().replace(/\s+/g, '-');

    return (
      <div className="flex flex-col gap-1.5">
        {label && (
          <label htmlFor={selectId} className="text-sm font-medium text-zinc-400">
            {label}
          </label>
        )}
        <select
          ref={ref}
          id={selectId}
          className={`rounded-xl border border-zinc-700 bg-zinc-900 px-4 py-2.5 text-sm text-zinc-100 transition-all duration-200 focus:border-brand-400 focus:outline-none focus:ring-2 focus:ring-brand-400/20 disabled:bg-zinc-800 disabled:text-zinc-500 ${error ? 'border-error focus:ring-error/20' : ''} ${className}`}
          {...props}
        >
          {placeholder && <option value="">{placeholder}</option>}
          {options.map((opt) => (
            <option key={opt.value} value={opt.value}>{opt.label}</option>
          ))}
        </select>
        {error && <p className="text-sm text-error">{error}</p>}
      </div>
    );
  }
);
