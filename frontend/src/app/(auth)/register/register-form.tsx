'use client';

import Link from 'next/link';
import { useState, type FormEvent } from 'react';
import { Alert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Input } from '@/components/ui/input';
import { registerCustomer } from '@/lib/api-client-helpers/auth';
import { t } from '@/lib/i18n';

/**
 * Customer registration form. On success the backend sends an email-
 * confirmation link and the page swaps to the "check your inbox" state.
 * Until the user clicks the link in their email they cannot log in
 * (per ADR 0012 §"Email confirmation").
 */
export function RegisterForm() {
  const [fullName, setFullName] = useState('');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [serverError, setServerError] = useState<string | null>(null);
  const [done, setDone] = useState(false);

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setServerError(null);
    setSubmitting(true);

    const result = await registerCustomer('customer', {
      email,
      password,
      fullName,
      countryCodePrimary: 'CZ',
    });

    setSubmitting(false);
    if (result.success) {
      setDone(true);
      return;
    }
    setServerError(mapRegisterError(result.error.code, result.error.message));
  }

  if (done) {
    return (
      <Card padding="lg" className="flex flex-col gap-3">
        <h2 className="text-lg font-semibold">{t('auth.register.success_title')}</h2>
        <p className="text-sm text-zinc-300">{t('auth.register.success_body')}</p>
        <p className="text-sm text-zinc-400">
          <Link href="/auth/login" className="text-brand-400 hover:underline">
            {t('auth.register.login_link')}
          </Link>
        </p>
      </Card>
    );
  }

  return (
    <Card padding="lg" className="flex flex-col gap-5">
      <form onSubmit={handleSubmit} className="flex flex-col gap-4" noValidate>
        {serverError && <Alert variant="error">{serverError}</Alert>}
        <Input
          type="text"
          label={t('auth.register.full_name')}
          value={fullName}
          onChange={(e) => setFullName(e.target.value)}
          autoComplete="name"
          required
          disabled={submitting}
        />
        <Input
          type="email"
          label={t('auth.register.email')}
          value={email}
          onChange={(e) => setEmail(e.target.value)}
          autoComplete="email"
          required
          disabled={submitting}
        />
        <Input
          type="password"
          label={t('auth.register.password')}
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          autoComplete="new-password"
          minLength={10}
          required
          disabled={submitting}
        />
        <p className="text-xs text-zinc-500">{t('auth.register.password_hint')}</p>
        <Button type="submit" loading={submitting} className="mt-2">
          {submitting ? t('auth.register.submitting') : t('auth.register.submit')}
        </Button>
      </form>
      <p className="text-center text-sm text-zinc-400">
        {t('auth.register.already_have_account')}{' '}
        <Link href="/auth/login" className="text-brand-400 hover:underline">
          {t('auth.register.login_link')}
        </Link>
      </p>
    </Card>
  );
}

function mapRegisterError(code: string, fallback: string): string {
  switch (code) {
    case 'auth.emailAlreadyExists':
      return t('auth.register.email_already_exists');
    default:
      return fallback;
  }
}
