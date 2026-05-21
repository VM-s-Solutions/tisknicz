'use client';

import { type InputHTMLAttributes, forwardRef } from 'react';

interface InputProps extends InputHTMLAttributes<HTMLInputElement> {
  label?: string;
  error?: string;
}

export const Input = forwardRef<HTMLInputElement, InputProps>(
  function Input({ label, error, className = '', id, ...props }, ref) {
    const inputId = id ?? label?.toLowerCase().replace(/\s+/g, '-');

    return (
      <div className="flex flex-col gap-1.5">
        {label && (
          <label htmlFor={inputId} className="text-sm font-medium text-zinc-400">
            {label}
          </label>
        )}
        <input
          ref={ref}
          id={inputId}
          className={`rounded-xl border border-zinc-700 bg-zinc-900 px-4 py-2.5 text-sm text-zinc-100 placeholder-zinc-600 transition-all duration-200 focus:border-brand-400 focus:outline-none focus:ring-2 focus:ring-brand-400/20 disabled:bg-zinc-800 disabled:text-zinc-500 ${error ? 'border-error focus:ring-error/20' : ''} ${className}`}
          {...props}
        />
        {error && <p className="text-sm text-error">{error}</p>}
      </div>
    );
  }
);
