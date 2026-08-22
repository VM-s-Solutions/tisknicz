'use client';

import Link from 'next/link';
import { useSearchParams } from 'next/navigation';
import { useEffect, useRef, useState } from 'react';
import { ResendConfirmationForm } from '@/components/shared/resend-confirmation-form';
import { Alert } from '@/components/ui/alert';
import { Card } from '@/components/ui/card';
import { Spinner } from '@/components/ui/spinner';
import { confirmEmail } from '@/lib/api-client-helpers/auth';
import { t } from '@/lib/i18n';

type State = 'pending' | 'success' | 'failed' | 'missing-token';

/**
 * Consumes the email-confirmation token from the URL's `?token=…` query
 * param. Fires once on mount via useEffect — exception to the
 * "no useEffect for data fetching" rule per CLAUDE.md because the
 * server cannot consume the token (it must run from the client to bind
 * the audience-scoped session cookie if we later upgrade this to a
 * login-on-confirm flow).
 */
export function VerifyClient() {
  const searchParams = useSearchParams();
  const token = searchParams.get('token');
  const [state, setState] = useState<State>(token ? 'pending' : 'missing-token');
  // T-0168 (audit AUTH-M1): the token is ONE-TIME — StrictMode's dev
  // double-mount used to fire the POST twice, burning the token on the
  // first call and showing the second call's "invalid link" to a user
  // who had just been confirmed. The ref survives the simulated remount.
  const firedRef = useRef(false);

  useEffect(() => {
    if (!token || firedRef.current) return;
    firedRef.current = true;
    void (async () => {
      const result = await confirmEmail('customer', { token });
      setState(result.success ? 'success' : 'failed');
    })();
  }, [token]);

  if (state === 'missing-token') {
    return (
      <Card padding="lg" variant="elevated">
        <Alert variant="error">{t('auth.verify.missing_token')}</Alert>
      </Card>
    );
  }

  if (state === 'pending') {
    return (
      <Card padding="lg" variant="elevated" className="flex items-center gap-3 text-sm text-zinc-300">
        <Spinner />
        <span>{t('auth.verify.confirming')}</span>
      </Card>
    );
  }

  if (state === 'success') {
    return (
      <Card padding="lg" variant="elevated" className="flex flex-col gap-3">
        <h2 className="text-lg font-semibold text-white">{t('auth.verify.success_title')}</h2>
        <p className="text-sm text-zinc-300">{t('auth.verify.success_body')}</p>
        <p className="text-sm">
          <Link href="/login" className="text-brand-400 hover:underline">
            {t('auth.login.submit')}
          </Link>
        </p>
      </Card>
    );
  }

  return (
    <Card padding="lg" variant="elevated" className="flex flex-col gap-4">
      <h2 className="text-lg font-semibold text-white">{t('auth.verify.failed_title')}</h2>
      <p className="text-sm text-zinc-300">{t('auth.verify.failed_body')}</p>
      {/* Recovery paths (T-0168): the failed card used to dead-end with
          zero actions. Already-confirmed users just need to log in;
          everyone else can issue themselves a fresh link. */}
      <p className="text-sm text-zinc-300">
        {t('auth.verify.failed_already_confirmed_hint')}{' '}
        <Link href="/login" className="text-brand-400 hover:underline">
          {t('auth.login.submit')}
        </Link>
      </p>
      <div className="border-t border-zinc-800/80 pt-4">
        <p className="mb-3 text-sm text-zinc-400">{t('auth.verify.resend')}</p>
        <ResendConfirmationForm />
      </div>
    </Card>
  );
}
