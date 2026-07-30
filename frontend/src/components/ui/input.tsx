'use client';

import { type InputHTMLAttributes, forwardRef } from 'react';
import { Icon, type IconName } from '@/components/ui/icon';

interface InputProps extends InputHTMLAttributes<HTMLInputElement> {
  label?: string;
  error?: string;
  /** Decorative leading icon inside the field. */
  icon?: IconName;
}

export const Input = forwardRef<HTMLInputElement, InputProps>(
  function Input({ label, error, icon, className = '', id, ...props }, ref) {
    const inputId = id ?? label?.toLowerCase().replace(/\s+/g, '-');

    return (
      <div className="flex flex-col gap-1.5">
        {label && (
          <label htmlFor={inputId} className="text-sm font-medium text-zinc-400">
            {label}
          </label>
        )}
        <div className="relative">
          {icon && (
            <span
              aria-hidden="true"
              className="pointer-events-none absolute left-3.5 top-1/2 -translate-y-1/2 text-zinc-500"
            >
              <Icon name={icon} size={16} />
            </span>
          )}
          <input
            ref={ref}
            id={inputId}
            className={`w-full rounded-xl border border-zinc-700 bg-zinc-900 py-2.5 pr-4 text-sm text-zinc-100 placeholder-zinc-600 transition-all duration-200 focus:border-brand-400 focus:outline-none focus:ring-2 focus:ring-brand-400/20 disabled:bg-zinc-800 disabled:text-zinc-500 ${icon ? 'pl-10' : 'pl-4'} ${error ? 'border-error focus:ring-error/20' : ''} ${className}`}
            {...props}
          />
        </div>
        {error && <p className="text-sm text-error">{error}</p>}
      </div>
    );
  }
);
