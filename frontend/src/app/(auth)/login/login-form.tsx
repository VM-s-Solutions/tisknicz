'use client';

import Link from 'next/link';
import { useRouter, useSearchParams } from 'next/navigation';
import { useState, type FormEvent } from 'react';
import { Alert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { AppleSignInButton } from '@/components/shared/apple-sign-in-button';
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
  // Open-redirect guard (checkout-flow Gate 3 F1): accept only
  // path-only targets — a single leading slash, not protocol-relative
  // `//host` (nor `/\host`, which WHATWG URL parsing normalises to
  // `//host`). Anything else falls back to the homepage.
  const rawRedirect = searchParams.get('redirect') ?? '/';
  const redirectTo = /^\/(?![/\\])/.test(rawRedirect) ? rawRedirect : '/';

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
    <>
      <div className="flex flex-col gap-5">
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
        <div className="flex items-center gap-3 text-xs text-zinc-500">
          <div className="h-px flex-1 bg-zinc-800" />
          {t('auth.apple.orDivider')}
          <div className="h-px flex-1 bg-zinc-800" />
        </div>
        <AppleSignInButton host="customer" onError={setServerError} />
        <div className="flex flex-col gap-2 text-sm text-zinc-400">
          <Link href="/reset" className="text-brand-400 hover:underline">
            {t('auth.login.forgot_password')}
          </Link>
          <Link href="/magic" className="text-brand-400 hover:underline">
            {t('auth.login.magic_link')}
          </Link>
          <div className="mt-2 border-t border-zinc-800 pt-3">
            <p>{t('auth.login.no_account')}</p>
            <div className="mt-1 flex flex-wrap gap-x-3 gap-y-1">
              <Link href="/register?type=customer" className="text-brand-400 hover:underline">
                {t('auth.login.register_customer_link')}
              </Link>
              <span aria-hidden="true">•</span>
              <Link href="/register?type=maker" className="text-brand-400 hover:underline">
                {t('auth.login.register_maker_link')}
              </Link>
            </div>
          </div>
        </div>
      </div>
    </>
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
