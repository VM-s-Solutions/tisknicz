'use client';

import Link from 'next/link';
import { useRef, useState, type FormEvent } from 'react';
import { Alert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { GoogleSignInButton } from '@/components/shared/google-sign-in-button';
import {
  type CompanyPreview,
  lookupCompanyPreview,
  registerCustomer,
} from '@/lib/api-client-helpers/auth';
import { t } from '@/lib/i18n';
import { isValidCzechIco, normalizeIcoInput } from '@/lib/validation/czech-ico';

/**
 * Customer registration form. On success the backend sends an email-
 * confirmation link and the page swaps to the "check your inbox" state.
 * Until the user clicks the link in their email they cannot log in
 * (per ADR 0012 §"Email confirmation").
 *
 * T-0162 "Jsem firma": ticking the checkbox reveals an IČO input that
 * reuses the T-0159 maker-registration loop — digits-only normalisation,
 * local mod-11 gate, and a debounced ARES preview (name + DIČ) via the
 * anonymous registry-preview endpoint. A failed preview never blocks
 * submit; the backend re-runs the authoritative lookup and stores the
 * company snapshot on the account.
 */

type PreviewState =
  | { kind: 'idle' }
  | { kind: 'loading' }
  | { kind: 'found'; company: CompanyPreview }
  | { kind: 'notFound' }
  | { kind: 'unavailable' };

const PREVIEW_DEBOUNCE_MS = 400;

export function RegisterForm() {
  const [fullName, setFullName] = useState('');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [isCompany, setIsCompany] = useState(false);
  const [ico, setIco] = useState('');
  const [icoChecksumFailed, setIcoChecksumFailed] = useState(false);
  const [preview, setPreview] = useState<PreviewState>({ kind: 'idle' });
  const [submitting, setSubmitting] = useState(false);
  const [serverError, setServerError] = useState<string | null>(null);
  const [done, setDone] = useState(false);

  // Event-handler-driven debounce (no effect fetching): each keystroke
  // resets the timer; the sequence counter drops stale responses when a
  // slow lookup resolves after the user typed a different IČO. Same
  // shape as register-maker-form (T-0159).
  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const lookupSeqRef = useRef(0);

  function handleIsCompanyChange(checked: boolean): void {
    setIsCompany(checked);
    setServerError(null);
    if (!checked) {
      // Unchecking must leave no stale company state behind — the submit
      // payload for a private person carries no IČO at all.
      if (debounceRef.current) clearTimeout(debounceRef.current);
      lookupSeqRef.current++;
      setIco('');
      setIcoChecksumFailed(false);
      setPreview({ kind: 'idle' });
    }
  }

  function handleIcoChange(raw: string): void {
    const next = normalizeIcoInput(raw);
    setIco(next);
    setServerError(null);
    if (debounceRef.current) clearTimeout(debounceRef.current);
    lookupSeqRef.current++;

    if (next.length < 8) {
      setIcoChecksumFailed(false);
      setPreview({ kind: 'idle' });
      return;
    }
    if (!isValidCzechIco(next)) {
      setIcoChecksumFailed(true);
      setPreview({ kind: 'idle' });
      return;
    }

    setIcoChecksumFailed(false);
    setPreview({ kind: 'loading' });
    const seq = lookupSeqRef.current;
    debounceRef.current = setTimeout(() => {
      void (async () => {
        const result = await lookupCompanyPreview(next);
        if (seq !== lookupSeqRef.current) return; // user typed on — stale
        if (result.success) {
          setPreview({ kind: 'found', company: result.value });
        } else if (result.error.code === 'company.notFound') {
          setPreview({ kind: 'notFound' });
        } else {
          setPreview({ kind: 'unavailable' });
        }
      })();
    }, PREVIEW_DEBOUNCE_MS);
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setServerError(null);

    // Local checksum gate mirrors the backend's CzechIcoValidator —
    // catches typos without a round trip (T-0159 pattern).
    if (isCompany && !isValidCzechIco(ico)) {
      setIcoChecksumFailed(true);
      setServerError(t('auth.register.company_ico_checksum_invalid'));
      return;
    }

    setSubmitting(true);
    const result = await registerCustomer('customer', {
      email,
      password,
      fullName,
      countryCodePrimary: 'CZ',
      ...(isCompany ? { companyRegistrationNumber: ico } : {}),
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
      <div className="flex flex-col items-center gap-3 text-center">
        <h2 className="text-lg font-semibold">{t('auth.register.success_title')}</h2>
        <p className="text-sm text-zinc-300">{t('auth.register.success_body')}</p>
        <p className="text-sm text-zinc-400">
          <Link href="/login" className="text-brand-400 hover:underline">
            {t('auth.register.login_link')}
          </Link>
        </p>
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-5">
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

        <label className="flex items-center gap-2 text-sm text-zinc-200">
          <input
            type="checkbox"
            checked={isCompany}
            onChange={(e) => handleIsCompanyChange(e.target.checked)}
            disabled={submitting}
            className="h-4 w-4 rounded border-zinc-700 bg-zinc-900 text-brand-400 focus:ring-brand-400/40"
          />
          {t('auth.register.is_company')}
        </label>
        {isCompany && (
          <>
            <p className="-mt-2 text-xs text-zinc-500">{t('auth.register.is_company_hint')}</p>
            <Input
              type="text"
              label={t('auth.register.company_ico')}
              value={ico}
              onChange={(e) => handleIcoChange(e.target.value)}
              inputMode="numeric"
              pattern="[0-9]{8}"
              maxLength={8}
              error={
                icoChecksumFailed
                  ? t('auth.register.company_ico_checksum_invalid')
                  : undefined
              }
              required
              disabled={submitting}
            />
            <p className="-mt-2 text-xs text-zinc-500">{t('auth.register.company_ico_hint')}</p>
            {preview.kind === 'loading' && (
              <p className="-mt-1 text-xs text-zinc-400">
                {t('auth.register.company_preview_loading')}
              </p>
            )}
            {preview.kind === 'notFound' && (
              <Alert variant="warning">{t('auth.register.company_preview_not_found')}</Alert>
            )}
            {preview.kind === 'unavailable' && (
              <p className="-mt-1 text-xs text-zinc-500">
                {t('auth.register.company_preview_unavailable')}
              </p>
            )}
            {preview.kind === 'found' && (
              <div className="rounded-xl border border-zinc-800 bg-surface-card p-4 text-sm">
                <p className="text-xs uppercase tracking-wide text-zinc-500">
                  {t('auth.register.company_preview_heading')}
                </p>
                <p className="mt-1 font-semibold text-zinc-100">
                  {preview.company.companyName}
                </p>
                <p className="mt-1 text-zinc-300">
                  {t('auth.register.company_preview_vat_id')}:{' '}
                  {preview.company.vatId ?? t('auth.register.company_preview_no_vat_id')}
                </p>
                {preview.company.isActiveInRegistry ? (
                  <p className="mt-2 text-xs text-zinc-500">
                    {t('auth.register.company_preview_confirm_hint')}
                  </p>
                ) : (
                  <Alert variant="error" className="mt-2">
                    {t('auth.register.company_preview_dissolved')}
                  </Alert>
                )}
              </div>
            )}
          </>
        )}

        <Button type="submit" loading={submitting} className="mt-2">
          {submitting ? t('auth.register.submitting') : t('auth.register.submit')}
        </Button>
      </form>
      <div className="flex items-center gap-3 text-xs text-zinc-500">
        <div className="h-px flex-1 bg-zinc-800" />
        {t('auth.oauth.orDivider')}
        <div className="h-px flex-1 bg-zinc-800" />
      </div>
      <GoogleSignInButton host="customer" onError={setServerError} />
      <p className="text-center text-sm text-zinc-400">
        {t('auth.register.already_have_account')}{' '}
        <Link href="/login" className="text-brand-400 hover:underline">
          {t('auth.register.login_link')}
        </Link>
      </p>
    </div>
  );
}

function mapRegisterError(code: string, fallback: string): string {
  switch (code) {
    case 'auth.emailAlreadyExists':
      return t('auth.register.email_already_exists');
    case 'validation.icoFormat':
    case 'company.notFound':
      return t('auth.register.company_ico_invalid');
    case 'user.companyDissolved':
      return t('user.companyDissolved');
    default:
      return fallback;
  }
}
