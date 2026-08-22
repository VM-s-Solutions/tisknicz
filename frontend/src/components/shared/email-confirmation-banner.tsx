'use client';

import { useState } from 'react';
import { Alert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { resendConfirmation } from '@/lib/api-client-helpers/auth';
import { t } from '@/lib/i18n';
import { resolveErrorMessage } from '@/lib/runtime/errors';
import type { ApiHost } from '@/lib/runtime/api-fetch';

interface EmailConfirmationBannerProps {
  /** Pre-filled email — usually the signed-in user's address. */
  readonly email: string;
  readonly host?: ApiHost;
}

/**
 * Banner shown to logged-in users whose email isn't confirmed yet.
 * T-0168: the resend now goes through the dedicated
 * <c>resend-confirmation</c> endpoint (it used to send a magic LINK,
 * which is a login artifact, not the confirmation email the copy
 * promises), and a failed resend surfaces its error instead of silently
 * re-enabling the button (audit AUTH-L4). The backend rate-limits per
 * email per ADR 0012 §"Anti-abuse — token issuance".
 */
export function EmailConfirmationBanner({ email, host = 'customer' }: EmailConfirmationBannerProps) {
  const [sent, setSent] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function handleResend() {
    if (busy || sent) return;
    setBusy(true);
    setError(null);
    const result = await resendConfirmation(host, { email });
    setBusy(false);
    if (result.success) {
      setSent(true);
      return;
    }
    setError(resolveErrorMessage(result.error));
  }

  return (
    <Alert variant="warning">
      <div className="flex flex-wrap items-center gap-x-4 gap-y-2">
        <span className="min-w-0">{t('auth.verify.banner')}</span>
        <Button variant="outline" size="sm" disabled={busy || sent} loading={busy} onClick={handleResend}>
          {sent ? t('auth.verify.banner_sent') : t('auth.verify.banner_action')}
        </Button>
        {error ? <p className="w-full text-sm text-error">{error}</p> : null}
      </div>
    </Alert>
  );
}
