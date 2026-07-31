'use client';

import { type TextareaHTMLAttributes, forwardRef } from 'react';

interface TextareaProps extends TextareaHTMLAttributes<HTMLTextAreaElement> {
  label?: string;
  error?: string;
}

export const Textarea = forwardRef<HTMLTextAreaElement, TextareaProps>(
  function Textarea({ label, error, className = '', id, ...props }, ref) {
    const textareaId = id ?? label?.toLowerCase().replace(/\s+/g, '-');

    return (
      <div className="flex flex-col gap-1.5">
        {label && (
          <label htmlFor={textareaId} className="text-sm font-medium text-zinc-300">
            {label}
          </label>
        )}
        <textarea
          ref={ref}
          id={textareaId}
          className={`rounded-lg border border-zinc-700 bg-zinc-900 px-4 py-2.5 text-sm text-zinc-100 placeholder-zinc-500 transition-colors duration-150 focus:border-brand-400 focus:outline-none focus:ring-2 focus:ring-brand-400/25 disabled:bg-zinc-800 disabled:text-zinc-500 ${error ? 'border-error focus:ring-error/20' : ''} ${className}`}
          {...props}
        />
        {error && <p className="text-sm text-error">{error}</p>}
      </div>
    );
  }
);
