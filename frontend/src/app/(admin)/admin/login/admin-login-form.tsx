'use client';

import { useRouter, useSearchParams } from 'next/navigation';
import { useState, type FormEvent } from 'react';
import { Alert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Input } from '@/components/ui/input';
import { login } from '@/lib/api-client-helpers/auth';
import { safeRedirectTarget } from '@/lib/auth';
import { t } from '@/lib/i18n';

/**
 * Dedicated admin login form (T-0118a, US-admin-0001, AC-2). A thin
 * variant of the customer `LoginForm` that posts `login('admin', …)` —
 * the JWT it receives carries `aud=admin`, so per-host audience match
 * (ADR 0013) means a customer/maker token replayed against the admin
 * host 401s at the backend, and a non-admin reaching this form gets
 * `auth.forbidden`. NO register / magic-link / OAuth affordance: admins
 * are provisioned, not self-registered (US-admin-0001 no-OAuth lock).
 *
 * On success `router.replace(redirectTo)` lands on the guarded redirect
 * target (open-redirect guard: path-only, single leading slash — the
 * customer LoginForm precedent). Errors map the BusinessErrorMessage
 * code to a keyed admin message (vykání).
 */
export function AdminLoginForm() {
  const router = useRouter();
  const searchParams = useSearchParams();
  // Open-redirect guard — see `safeRedirectTarget`.
  const redirectTo = safeRedirectTarget(searchParams.get('redirect')) ?? '/dashboard/admin';

  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [serverError, setServerError] = useState<string | null>(null);

  async function handleSubmit(event: FormEvent<HTMLFormElement>): Promise<void> {
    event.preventDefault();
    setServerError(null);
    setSubmitting(true);
    const result = await login('admin', { email, password });
    setSubmitting(false);

    if (result.success) {
      // `replace` so Back never returns to the login form (see LoginForm).
      router.replace(redirectTo);
      return;
    }

    setServerError(mapAdminLoginError(result.error.code));
  }

  return (
    <Card padding="lg" className="flex flex-col gap-5">
      <form onSubmit={handleSubmit} className="flex flex-col gap-4" noValidate>
        {serverError && <Alert variant="error">{serverError}</Alert>}
        <Input
          type="email"
          label={t('dashboard.admin.login.email')}
          value={email}
          onChange={(e) => setEmail(e.target.value)}
          autoComplete="email"
          required
          disabled={submitting}
        />
        <Input
          type="password"
          label={t('dashboard.admin.login.password')}
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          autoComplete="current-password"
          required
          disabled={submitting}
        />
        <Button type="submit" loading={submitting} className="mt-2">
          {submitting ? t('dashboard.admin.login.submitting') : t('dashboard.admin.login.submit')}
        </Button>
      </form>
    </Card>
  );
}

function mapAdminLoginError(code: string): string {
  switch (code) {
    case 'auth.invalidCredentials':
      return t('dashboard.admin.login.error.invalidCredentials');
    case 'auth.locked':
      return t('dashboard.admin.login.error.locked');
    case 'auth.forbidden':
      return t('dashboard.admin.login.error.forbidden');
    case 'auth.oauthNotAllowedForAdmin':
      return t('dashboard.admin.login.error.oauthNotAllowed');
    default:
      return t('dashboard.admin.login.error.generic');
  }
}
