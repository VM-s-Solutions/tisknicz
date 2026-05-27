'use client';

import Link from 'next/link';
import { useState, type FormEvent } from 'react';
import { Alert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Input } from '@/components/ui/input';
import { registerMaker } from '@/lib/api-client-helpers/auth';
import { t } from '@/lib/i18n';

/**
 * Maker registration form. Posts to the Public host's
 * /api/v1/makers/register, which does the full 6-step flow per T-0033:
 * IČO format gate → ARES lookup → dissolved-entity reject →
 * email/IČO conflict pre-checks → atomic User+Address+Maker add →
 * email-confirmation token.
 *
 * The form only collects the IČO + the user's personal credentials —
 * company name, legal seat, etc. come from ARES. The "stale snapshot"
 * banner from the response surfaces the ADR 0018 fallback case.
 */
export function RegisterMakerForm() {
  const [fullName, setFullName] = useState('');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [ico, setIco] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [serverError, setServerError] = useState<string | null>(null);
  const [doneState, setDoneState] = useState<{ stale: boolean } | null>(null);

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setServerError(null);
    setSubmitting(true);

    const result = await registerMaker({
      email,
      password,
      fullName,
      countryCodePrimary: 'CZ',
      registrationNumber: ico.trim(),
    });

    setSubmitting(false);
    if (result.success) {
      setDoneState({ stale: result.value.snapshotIsStale });
      return;
    }
    setServerError(mapRegisterMakerError(result.error.code, result.error.message));
  }

  if (doneState) {
    return (
      <Card padding="lg" className="flex flex-col gap-3">
        <h2 className="text-lg font-semibold">{t('auth.register.success_title')}</h2>
        <p className="text-sm text-zinc-300">{t('auth.register.success_body')}</p>
        {doneState.stale && (
          <Alert variant="warning">{t('auth.register_maker.snapshot_stale_notice')}</Alert>
        )}
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
          label={t('auth.register_maker.ico')}
          value={ico}
          onChange={(e) => setIco(e.target.value)}
          inputMode="numeric"
          pattern="[0-9]{8}"
          required
          disabled={submitting}
        />
        <p className="-mt-2 text-xs text-zinc-500">{t('auth.register_maker.ico_hint')}</p>
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
          {submitting ? t('auth.register.submitting') : t('auth.register_maker.submit')}
        </Button>
      </form>
    </Card>
  );
}

function mapRegisterMakerError(code: string, fallback: string): string {
  switch (code) {
    case 'validation.icoFormat':
    case 'company.notFound':
      return t('auth.register_maker.ico_invalid');
    case 'maker.icoAlreadyRegistered':
      return t('auth.register_maker.ico_already_registered');
    case 'maker.companyDissolved':
      return t('auth.register_maker.company_dissolved');
    case 'auth.emailAlreadyExists':
      return t('auth.register.email_already_exists');
    default:
      return fallback;
  }
}
