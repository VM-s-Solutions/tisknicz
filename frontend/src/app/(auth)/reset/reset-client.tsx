'use client';

import Link from 'next/link';
import { useSearchParams } from 'next/navigation';
import { useState, type FormEvent } from 'react';
import { Alert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Icon } from '@/components/ui/icon';
import { Input } from '@/components/ui/input';
import {
  confirmPasswordReset,
  requestPasswordReset,
} from '@/lib/api-client-helpers/auth';
import { t } from '@/lib/i18n';

/**
 * Dual-mode page:
 *   - no token in URL → render the "request reset link" form.
 *   - `?token=…` present → render the "set new password" form.
 *
 * The success page is enumeration-safe ("if an account exists, we sent
 * an email") per ADR 0012 §"Anti-abuse — username enumeration".
 */
export function ResetClient() {
  const searchParams = useSearchParams();
  const token = searchParams.get('token');
  return token ? <ConfirmReset token={token} /> : <RequestReset />;
}

function RequestReset() {
  const [email, setEmail] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [serverError, setServerError] = useState<string | null>(null);
  const [done, setDone] = useState(false);

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setServerError(null);
    setSubmitting(true);
    const result = await requestPasswordReset('customer', { email });
    setSubmitting(false);
    if (result.success) {
      setDone(true);
      return;
    }
    setServerError(result.error.message);
  }

  if (done) {
    return (
      <>
        <h1 className="text-2xl font-semibold tracking-tight text-white sm:text-3xl">{t('auth.reset.request_done_title')}</h1>
        <Card padding="lg" variant="elevated" className="mt-6 flex flex-col gap-3">
          <p className="text-sm text-zinc-300">{t('auth.reset.request_done_body')}</p>
          <p className="text-sm">
            <Link href="/login" className="text-brand-400 hover:underline">
              {t('auth.login.submit')}
            </Link>
          </p>
        </Card>
      </>
    );
  }

  return (
    <>
      <h1 className="text-2xl font-semibold tracking-tight text-white sm:text-3xl">{t('auth.reset.request_title')}</h1>
      <Card padding="lg" variant="elevated" className="mt-6 flex flex-col gap-5">
        <p className="text-sm text-zinc-400">{t('auth.reset.request_intro')}</p>
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
            {t('auth.reset.request_submit')}
          </Button>
        </form>
      </Card>
    </>
  );
}

function ConfirmReset({ token }: { token: string }) {
  const [newPassword, setNewPassword] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [serverError, setServerError] = useState<string | null>(null);
  const [done, setDone] = useState(false);

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setServerError(null);
    setSubmitting(true);
    const result = await confirmPasswordReset('customer', { token, newPassword });
    setSubmitting(false);
    if (result.success) {
      setDone(true);
      return;
    }
    setServerError(mapConfirmError(result.error.code, result.error.message));
  }

  if (done) {
    return (
      <>
        <h1 className="text-2xl font-semibold tracking-tight text-white sm:text-3xl">{t('auth.reset.confirm_done_title')}</h1>
        <Card padding="lg" variant="elevated" className="mt-6 flex flex-col gap-3">
          <p className="text-sm text-zinc-300">{t('auth.reset.confirm_done_body')}</p>
          <p className="text-sm">
            <Link href="/login" className="text-brand-400 hover:underline">
              {t('auth.login.submit')}
            </Link>
          </p>
        </Card>
      </>
    );
  }

  return (
    <>
      <h1 className="text-2xl font-semibold tracking-tight text-white sm:text-3xl">{t('auth.reset.confirm_title')}</h1>
      <Card padding="lg" variant="elevated" className="mt-6 flex flex-col gap-5">
        <p className="text-sm text-zinc-400">{t('auth.reset.confirm_intro')}</p>
        <form onSubmit={handleSubmit} className="flex flex-col gap-4" noValidate>
          {serverError && <Alert variant="error">{serverError}</Alert>}
          <Input
            type="password"
            icon="lock"
            label={t('auth.register.password')}
            value={newPassword}
            onChange={(e) => setNewPassword(e.target.value)}
            autoComplete="new-password"
            minLength={10}
            required
            disabled={submitting}
          />
          <Button type="submit" loading={submitting} className="mt-2">
            {!submitting ? (
              <span aria-hidden="true">
                <Icon name="check" size={16} />
              </span>
            ) : null}
            {t('auth.reset.confirm_submit')}
          </Button>
        </form>
      </Card>
    </>
  );
}

function mapConfirmError(code: string, fallback: string): string {
  switch (code) {
    case 'auth.passwordResetInvalid':
      return t('auth.reset.confirm_failed');
    default:
      return fallback;
  }
}
