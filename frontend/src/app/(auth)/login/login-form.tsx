'use client';

import Link from 'next/link';
import { useRouter, useSearchParams } from 'next/navigation';
import { useState, type FormEvent } from 'react';
import { Alert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Input } from '@/components/ui/input';
import { login } from '@/lib/api-client-helpers/auth';
import { t } from '@/lib/i18n';

/**
 * Login form for the customer host. Posts to /api/v1/auth/login which
 * sets the audience-scoped session cookies on success. On error the
 * BusinessErrorMessage code is mapped to a localized message.
 *
 * Hardcoded to the 'customer' host for the MVP; T-0036 (maker dashboard)
 * reuses this component with `host="maker"`.
 */
export function LoginForm() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const redirectTo = searchParams.get('redirect') ?? '/';

  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [serverError, setServerError] = useState<string | null>(null);

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setServerError(null);
    setSubmitting(true);
    const result = await login('customer', { email, password });
    setSubmitting(false);

    if (result.success) {
      router.push(redirectTo);
      return;
    }

    setServerError(mapLoginError(result.error.code, result.error.message));
  }

  return (
    <Card padding="lg" className="flex flex-col gap-5">
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
        <Input
          type="password"
          label={t('auth.login.password')}
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          autoComplete="current-password"
          required
          disabled={submitting}
        />
        <Button type="submit" loading={submitting} className="mt-2">
          {submitting ? t('auth.login.submitting') : t('auth.login.submit')}
        </Button>
      </form>
      <div className="flex flex-col gap-2 text-sm text-zinc-400">
        <Link href="/auth/reset" className="text-brand-400 hover:underline">
          {t('auth.login.forgot_password')}
        </Link>
        <Link href="/auth/magic" className="text-brand-400 hover:underline">
          {t('auth.login.magic_link')}
        </Link>
        <p className="mt-2">
          {t('auth.login.no_account')}{' '}
          <Link href="/auth/register" className="text-brand-400 hover:underline">
            {t('auth.login.register_link')}
          </Link>
        </p>
      </div>
    </Card>
  );
}

function mapLoginError(code: string, fallback: string): string {
  switch (code) {
    case 'auth.invalidCredentials':
      return t('auth.login.invalid_credentials');
    case 'auth.locked':
      return t('auth.login.account_locked');
    case 'auth.emailNotConfirmed':
      return t('auth.login.email_not_confirmed');
    default:
      return fallback;
  }
}
