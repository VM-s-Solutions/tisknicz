'use client';

import { useRouter, useSearchParams } from 'next/navigation';
import { useEffect, useState, type FormEvent } from 'react';
import { Alert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
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
      <Card padding="lg" className="flex flex-col gap-3">
        <p className="text-sm text-zinc-300">{t('auth.magic.request_done_body')}</p>
      </Card>
    );
  }

  return (
    <Card padding="lg" className="flex flex-col gap-4">
      <form onSubmit={handleSubmit} className="flex flex-col gap-4" noValidate>
        {serverError && <Alert variant="error">{serverError}</Alert>}
        <Input
          type="email"
          label={t('auth.login.email')}
          value={email}
          onChange={(e) => setEmail(e.target.value)}
          autoComplete="email"
          required
          disabled={submitting}
        />
        <Button type="submit" loading={submitting} className="mt-2">
          {t('auth.login.magic_link')}
        </Button>
      </form>
    </Card>
  );
}

function Consume({ token }: { token: string }) {
  const router = useRouter();
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    void (async () => {
      const result = await consumeMagicLink('customer', { token });
      if (cancelled) return;
      if (result.success) {
        router.push('/');
        return;
      }
      setError(mapConsumeError(result.error.code, result.error.message));
    })();
    return () => {
      cancelled = true;
    };
  }, [token, router]);

  if (error) {
    return (
      <Card padding="lg" className="flex flex-col gap-3">
        <h2 className="text-lg font-semibold">{t('auth.magic.failed_title')}</h2>
        <p className="text-sm text-zinc-300">{error}</p>
      </Card>
    );
  }

  return (
    <Card padding="lg" className="flex items-center gap-3 text-sm text-zinc-300">
      <Spinner />
      <span>{t('auth.magic.consuming')}</span>
    </Card>
  );
}

function mapConsumeError(code: string, fallback: string): string {
  switch (code) {
    case 'auth.magicLinkInvalid':
      return t('auth.magic.failed_body');
    default:
      return fallback;
  }
}
