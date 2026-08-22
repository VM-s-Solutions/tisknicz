'use client';

import Link from 'next/link';
import { useRouter, useSearchParams } from 'next/navigation';
import { useEffect, useRef, useState, type FormEvent } from 'react';
import { Alert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Icon } from '@/components/ui/icon';
import { Input } from '@/components/ui/input';
import { Spinner } from '@/components/ui/spinner';
import {
  consumeMagicLink,
  requestMagicLink,
} from '@/lib/api-client-helpers/auth';
import { t } from '@/lib/i18n';

/**
 * Dual-mode:
 *   - no token → render the "send me a magic link" form.
 *   - `?token=…` → consume it, set the session cookies, route to home.
 */
export function MagicClient() {
  const searchParams = useSearchParams();
  const token = searchParams.get('token');
  return token ? <Consume token={token} /> : <RequestLink />;
}

function RequestLink() {
  const [email, setEmail] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [serverError, setServerError] = useState<string | null>(null);
  const [done, setDone] = useState(false);

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setServerError(null);
    setSubmitting(true);
    const result = await requestMagicLink('customer', { email });
    setSubmitting(false);
    if (result.success) {
      setDone(true);
      return;
    }
    setServerError(result.error.message);
  }

  if (done) {
    return (
      <Card padding="lg" variant="elevated" className="flex flex-col gap-3">
        <p className="text-sm text-zinc-300">{t('auth.magic.request_done_body')}</p>
      </Card>
    );
  }

  return (
    <Card padding="lg" variant="elevated" className="flex flex-col gap-4">
      <form onSubmit={handleSubmit} className="flex flex-col gap-4" noValidate>
        {serverError && <Alert variant="error">{serverError}</Alert>}
        <Input
          type="email"
          icon="mail"
          label={t('auth.login.email')}
          value={email}
          onChange={(e) => setEmail(e.target.value)}
          autoComplete="email"
          required
          disabled={submitting}
        />
        <Button type="submit" loading={submitting} className="mt-2">
          {!submitting ? (
            <span aria-hidden="true">
              <Icon name="send" size={16} />
            </span>
          ) : null}
          {t('auth.login.magic_link')}
        </Button>
      </form>
    </Card>
  );
}

function Consume({ token }: { token: string }) {
  const router = useRouter();
  const [error, setError] = useState<string | null>(null);
  // One-time token: guard against StrictMode's dev double-mount so the
  // second fire can't burn/err a link the first fire already consumed
  // (same class as verify-client, T-0168 / audit AUTH-M1).
  const firedRef = useRef(false);

  useEffect(() => {
    if (firedRef.current) return;
    firedRef.current = true;
    void (async () => {
      const result = await consumeMagicLink('customer', { token });
      if (result.success) {
        // `replace`: the consumed magic-link URL is single-use and must
        // not stay one Back press away (see LoginForm). `refresh` so the
        // session-aware chrome picks the new cookies up (T-0152).
        router.replace('/');
        router.refresh();
        return;
      }
      // T-0168 (audit AUTH-H3): the request form offers magic links to
      // EVERYONE, but consume was hardcoded to the customer host — a
      // maker's link died with an unmapped 403. The backend deliberately
      // does not burn the token on an audience mismatch, so retrying the
      // maker host completes the login (mirrors LoginForm's dual-host
      // fallback).
      if (result.error.code === 'auth.forbidden') {
        const makerResult = await consumeMagicLink('maker', { token });
        if (makerResult.success) {
          router.replace('/dashboard/maker/objednavky');
          router.refresh();
          return;
        }
        setError(mapConsumeError(makerResult.error.code));
        return;
      }
      setError(mapConsumeError(result.error.code));
    })();
  }, [token, router]);

  if (error) {
    return (
      <Card padding="lg" variant="elevated" className="flex flex-col gap-4">
        <h2 className="text-lg font-semibold text-white">{t('auth.magic.failed_title')}</h2>
        <p className="text-sm text-zinc-300">{error}</p>
        {/* Recovery paths (T-0168): the failure card used to dead-end. */}
        <div className="flex flex-wrap items-center gap-4 border-t border-zinc-800/80 pt-4 text-sm">
          <Link href="/magic" className="text-brand-400 hover:underline">
            {t('auth.magic.request_new')}
          </Link>
          <Link href="/login" className="text-zinc-300 hover:underline">
            {t('auth.login.submit')}
          </Link>
        </div>
      </Card>
    );
  }

  return (
    <Card padding="lg" variant="elevated" className="flex items-center gap-3 text-sm text-zinc-300">
      <Spinner />
      <span>{t('auth.magic.consuming')}</span>
    </Card>
  );
}

/**
 * Every consume failure maps to owned Czech copy — the backend `Error`
 * carries no message, so the previous raw-code fallback surfaced the
 * generic 403 text ("K této akci nemáte oprávnění.") for a maker's link.
 */
function mapConsumeError(code: string): string {
  switch (code) {
    case 'auth.forbidden':
      return t('auth.magic.failed_wrong_audience');
    case 'auth.magicLinkInvalid':
    default:
      return t('auth.magic.failed_body');
  }
}
