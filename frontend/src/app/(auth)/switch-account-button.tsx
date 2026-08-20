'use client';

import { useRouter } from 'next/navigation';
import { useState } from 'react';
import { Alert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { logout } from '@/lib/api-client-helpers/auth';
import type { Audience } from '@/lib/auth';
import { t } from '@/lib/i18n';

interface SwitchAccountButtonProps {
  /** Audience whose session cookies are cleared before re-authenticating. */
  readonly audience: Audience;
}

/**
 * "Sign in with a different account" — clears the current session and
 * re-renders the server tree so /login falls back to the form. A plain
 * link would not work: the cookies would still be there and the page
 * would render the already-signed-in panel again.
 *
 * `router.refresh()` (not `push`) keeps the URL — including the
 * `?redirect=` target — intact, so the fresh login still lands where the
 * user was originally headed.
 */
export function SwitchAccountButton({ audience }: SwitchAccountButtonProps) {
  const router = useRouter();
  const [pending, setPending] = useState(false);
  const [failed, setFailed] = useState(false);

  async function handleSwitch(): Promise<void> {
    if (pending) return;
    setPending(true);
    setFailed(false);
    const result = await logout(audience);
    if (!result.success) {
      setPending(false);
      setFailed(true);
      return;
    }
    router.refresh();
  }

  return (
    <div className="flex flex-col gap-3">
      {failed && <Alert variant="error">{t('auth.signedIn.switch_failed')}</Alert>}
      <Button type="button" variant="secondary" onClick={handleSwitch} loading={pending}>
        {pending ? t('auth.signedIn.switching') : t('auth.signedIn.switch')}
      </Button>
    </div>
  );
}
