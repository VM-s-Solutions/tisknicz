'use client';

import { useState, type FormEvent } from 'react';
import { Alert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { resendConfirmation } from '@/lib/api-client-helpers/auth';
import { t } from '@/lib/i18n';
import { resolveErrorMessage } from '@/lib/runtime/errors';

/**
 * Inline "send the confirmation e-mail again" affordance for LOGGED-OUT
 * users (T-0168, audit AUTH-M2): a lost/expired confirmation email used
 * to be a permanent dead end — the only resend lived behind a login the
 * user could not complete. Success copy is uniform (the backend answers
 * identically for unknown accounts, so this surface must too).
 */
export function ResendConfirmationForm({
  defaultEmail = '',
  compact = false,
}: {
  readonly defaultEmail?: string;
  /** Hide the email input when the address is already known-good. */
  readonly compact?: boolean;
}) {
  const [email, setEmail] = useState(defaultEmail);
  const [busy, setBusy] = useState(false);
  const [sent, setSent] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (busy || sent || email.trim() === '') return;
    setBusy(true);
    setError(null);
    const result = await resendConfirmation('customer', { email: email.trim() });
    setBusy(false);
    if (result.success) {
      setSent(true);
      return;
    }
    setError(resolveErrorMessage(result.error));
  }

  if (sent) {
    return <Alert variant="success">{t('auth.resend.sent')}</Alert>;
  }

  return (
    <form onSubmit={handleSubmit} className="flex flex-col gap-3" noValidate>
      {error ? <Alert variant="error">{error}</Alert> : null}
      {!compact ? (
        <Input
          type="email"
          icon="mail"
          label={t('auth.login.email')}
          value={email}
          onChange={(e) => setEmail(e.target.value)}
          autoComplete="email"
          required
          disabled={busy}
        />
      ) : null}
      <Button
        type="submit"
        variant="outline"
        size="sm"
        loading={busy}
        disabled={busy || email.trim() === ''}
        className="self-start"
      >
        {t('auth.resend.action')}
      </Button>
    </form>
  );
}
