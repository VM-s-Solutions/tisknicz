'use client';

import Link from 'next/link';
import { useRouter, useSearchParams } from 'next/navigation';
import { useState, type FormEvent } from 'react';
import { Alert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Icon } from '@/components/ui/icon';
import { Input } from '@/components/ui/input';
import { GoogleSignInButton } from '@/components/shared/google-sign-in-button';
import { login } from '@/lib/api-client-helpers/auth';
import { safeRedirectTarget } from '@/lib/auth';
import { t } from '@/lib/i18n';
import type { ApiHost } from '@/lib/runtime/api-fetch';

/**
 * Shared login form for customers AND makers. The backend binds each
 * account to exactly one audience (`User.MatchesAudience` — a maker can
 * only log in against the maker host), so the form first tries the host
 * matching the redirect target and falls back to the other one when the
 * backend answers `auth.forbidden` (right password, wrong audience).
 * Credential errors never trigger the fallback — the password check runs
 * before the audience check, so their message is already accurate.
 */
export function LoginForm() {
  const router = useRouter();
  const searchParams = useSearchParams();
  // Open-redirect guard (checkout-flow Gate 3 F1) — see `safeRedirectTarget`.
  const safeRedirect = safeRedirectTarget(searchParams.get('redirect'));

  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [serverError, setServerError] = useState<string | null>(null);

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setServerError(null);
    setSubmitting(true);

    // A maker bounced here from /dashboard/maker/* should hit the maker
    // host first — one request instead of two.
    const hostOrder: readonly ApiHost[] =
      safeRedirect?.startsWith('/dashboard/maker') ? ['maker', 'customer'] : ['customer', 'maker'];

    let host = hostOrder[0];
    let result = await login(host, { email, password });
    if (!result.success && result.error.code === 'auth.forbidden') {
      host = hostOrder[1];
      result = await login(host, { email, password });
    }
    setSubmitting(false);

    if (result.success) {
      const target = safeRedirect ?? (host === 'maker' ? '/dashboard/maker/objednavky' : '/');
      // `replace`, never `push`: a pushed /login stays one Back press
      // away and re-renders the form to an already-authenticated user
      // (reported as "back gives me a login screen even though I'm
      // logged in"). The page's already-signed-in panel is the
      // second line of defence for entries pushed before this change.
      router.replace(target);
      // Re-render the server tree so session-aware chrome (navbar
      // account menu, dashboard layouts) picks up the fresh cookie.
      router.refresh();
      return;
    }

    setServerError(mapLoginError(result.error.code, result.error.message));
  }

  return (
    <div className="flex flex-col gap-5">
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
        <Input
          type="password"
          icon="lock"
          label={t('auth.login.password')}
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          autoComplete="current-password"
          required
          disabled={submitting}
        />
        <Button type="submit" loading={submitting} className="mt-2">
          {submitting ? t('auth.login.submitting') : t('auth.login.submit')}
          {!submitting ? (
            <span aria-hidden="true">
              <Icon name="arrowRight" size={16} />
            </span>
          ) : null}
        </Button>
      </form>
      <div className="flex items-center gap-3 text-xs text-zinc-500">
        <div className="h-px flex-1 bg-zinc-800" />
        {t('auth.oauth.orDivider')}
        <div className="h-px flex-1 bg-zinc-800" />
      </div>
      <GoogleSignInButton host="customer" onError={setServerError} />
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
