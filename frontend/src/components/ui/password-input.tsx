'use client';

import { type InputHTMLAttributes, forwardRef, useId, useState } from 'react';
import { Icon, type IconName } from '@/components/ui/icon';
import { Input } from '@/components/ui/input';
import { t } from '@/lib/i18n';

interface PasswordInputProps
  extends Omit<InputHTMLAttributes<HTMLInputElement>, 'type'> {
  label?: string;
  error?: string;
  icon?: IconName;
}

/**
 * Password field with a reveal toggle. The eye button flips the input
 * between `password` and `text` — the single most requested fix for
 * mistyped passwords, and the reason the confirm field on registration
 * stays short.
 *
 * Accessibility: the toggle is a real `<button>` (keyboard reachable,
 * outside the tab-trap of the input), carries an `aria-label` that says
 * what pressing it will do, and `aria-pressed` so a screen reader
 * announces the current state. It is `aria-controls`-linked to the input
 * so the relationship survives the label being rendered above the box.
 */
export const PasswordInput = forwardRef<HTMLInputElement, PasswordInputProps>(
  function PasswordInput({ label, id, disabled, ...props }, ref) {
    const [revealed, setRevealed] = useState(false);
    const generatedId = useId();
    const inputId = id ?? label?.toLowerCase().replace(/\s+/g, '-') ?? generatedId;

    return (
      <Input
        {...props}
        ref={ref}
        id={inputId}
        label={label}
        disabled={disabled}
        type={revealed ? 'text' : 'password'}
        trailing={
          <button
            type="button"
            onClick={() => setRevealed((current) => !current)}
            disabled={disabled}
            aria-label={revealed ? t('auth.password.hide') : t('auth.password.show')}
            aria-pressed={revealed}
            aria-controls={inputId}
            className="rounded-md p-2 text-zinc-500 transition-colors duration-150 hover:text-zinc-200 focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-400/40 disabled:cursor-not-allowed disabled:text-zinc-700"
          >
            <Icon name={revealed ? 'eyeOff' : 'eye'} size={16} />
          </button>
        }
      />
    );
  }
);
