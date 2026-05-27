'use client';

import { useState } from 'react';
import { Alert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { requestMagicLink } from '@/lib/api-client-helpers/auth';
import { t } from '@/lib/i18n';
import type { ApiHost } from '@/lib/runtime/api-fetch';

interface EmailConfirmationBannerProps {
  /** Pre-filled email — usually the signed-in user's address. */
  readonly email: string;
  readonly host?: ApiHost;
}

/**
 * Banner shown to logged-in users whose email isn't confirmed yet.
 * The "Send again" button issues a fresh confirmation link via the
 * <c>request-magic-link</c> endpoint, which the backend rate-limits per
 * user per ADR 0012 §"Anti-abuse — token issuance".
 */
export function EmailConfirmationBanner({ email, host = 'customer' }: EmailConfirmationBannerProps) {
  const [sent, setSent] = useState(false);
  const [busy, setBusy] = useState(false);

  async function handleResend() {
    if (busy || sent) return;
    setBusy(true);
    const result = await requestMagicLink(host, { email });
    setBusy(false);
    if (result.success) {
      setSent(true);
    }
  }

  return (
    <Alert variant="warning" className="flex items-center justify-between gap-4">
      <span>{t('auth.verify.banner')}</span>
      <Button variant="outline" size="sm" disabled={busy || sent} onClick={handleResend}>
        {sent ? t('auth.verify.banner_sent') : t('auth.verify.banner_action')}
      </Button>
    </Alert>
  );
}
