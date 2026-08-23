'use client';

import { Button } from '@/components/ui/button';
import { Icon } from '@/components/ui/icon';
import { t } from '@/lib/i18n';

/** Lifecycle of one save round-trip, owned by the form. */
export type SaveState = 'idle' | 'saving' | 'saved';

interface SaveButtonProps {
  readonly state: SaveState;
  /** True while the form differs from what is currently persisted. */
  readonly dirty: boolean;
  /** Label at rest (already translated). Defaults to "Uložit změny". */
  readonly label?: string;
  /** Label after a successful save (already translated). Defaults to "Uloženo". */
  readonly savedLabel?: string;
  readonly className?: string;
}

/**
 * Submit button that carries its own save feedback instead of leaning on
 * a success alert somewhere else on the page. Long forms put the alert
 * at the top and the button at the bottom, so on a scrolled page the
 * only confirmation a save produced was off-screen — the change looked
 * like it did nothing.
 *
 * <para>
 * Three states, all read from the button the user just pressed: it is
 * disabled while nothing has changed (nothing to save), spins while the
 * request is in flight, and flips to a checked "Uloženo" that holds
 * until the next edit makes the form dirty again. The success state is
 * also announced through a polite live region — a disabled button's
 * changed label is not reliably read out on its own.
 * </para>
 */
export function SaveButton({ state, dirty, label, savedLabel, className = '' }: SaveButtonProps) {
  const saving = state === 'saving';
  // A successful save that the user has since edited is no longer the
  // current truth, so `saved` only shows while the form stays clean.
  const showSaved = state === 'saved' && !dirty;
  const restLabel = label ?? t('common.save_changes');
  const doneLabel = savedLabel ?? t('common.saved');

  return (
    <div className={`flex items-center gap-3 ${className}`}>
      <Button
        type="submit"
        variant={showSaved ? 'secondary' : 'primary'}
        loading={saving}
        disabled={!dirty}
        title={!dirty && !showSaved ? t('common.no_changes') : undefined}
        className={
          showSaved ? 'border-success/50 text-success hover:border-success/60 hover:bg-tint-success hover:text-on-tint-success' : ''
        }
      >
        {!saving && (
          <span aria-hidden="true">
            <Icon name={showSaved ? 'check' : 'save'} size={16} />
          </span>
        )}
        {saving ? t('common.saving') : showSaved ? doneLabel : restLabel}
      </Button>
      <span role="status" aria-live="polite" className="sr-only">
        {showSaved ? doneLabel : ''}
      </span>
    </div>
  );
}
