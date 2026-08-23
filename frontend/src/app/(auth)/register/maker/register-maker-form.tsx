'use client';

import Link from 'next/link';
import { useRef, useState, type FormEvent } from 'react';
import { Alert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Icon } from '@/components/ui/icon';
import { Input } from '@/components/ui/input';
import { PasswordInput } from '@/components/ui/password-input';
import {
  type CompanyPreview,
  lookupCompanyPreview,
  registerMaker,
} from '@/lib/api-client-helpers/auth';
import { t } from '@/lib/i18n';
import { isValidCzechIco, normalizeIcoInput } from '@/lib/validation/czech-ico';

/**
 * Maker registration form. Posts to the Public host's
 * /api/v1/makers/register, which does the full 6-step flow per T-0033:
 * IČO format gate → ARES lookup → dissolved-entity reject →
 * email/IČO conflict pre-checks → atomic User+Address+Maker add →
 * email-confirmation token.
 *
 * The form only collects the IČO + the user's personal credentials —
 * company name, legal seat, etc. come from ARES. T-0159 (business
 * decision Q4) adds the confirmation loop: input is normalised to
 * digits, the mod-11 checksum is validated locally (backend mirror),
 * and a debounced ARES preview shows WHO the IČO belongs to before
 * submit. A failed preview never blocks the form — the submit re-runs
 * the authoritative lookup server-side. The "stale snapshot" banner
 * from the response surfaces the ADR 0018 fallback case.
 */

type PreviewState =
  | { kind: 'idle' }
  | { kind: 'loading' }
  | { kind: 'found'; company: CompanyPreview }
  | { kind: 'notFound' }
  | { kind: 'unavailable' };

const PREVIEW_DEBOUNCE_MS = 400;

export function RegisterMakerForm() {
  const [fullName, setFullName] = useState('');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [passwordConfirm, setPasswordConfirm] = useState('');
  const [ico, setIco] = useState('');
  const [icoChecksumFailed, setIcoChecksumFailed] = useState(false);
  const [preview, setPreview] = useState<PreviewState>({ kind: 'idle' });
  const [submitting, setSubmitting] = useState(false);
  const [serverError, setServerError] = useState<string | null>(null);
  const [doneState, setDoneState] = useState<{ stale: boolean } | null>(null);

  // Event-handler-driven debounce (no effect fetching): each keystroke
  // resets the timer; the sequence counter drops stale responses when a
  // slow lookup resolves after the user typed a different IČO.
  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const lookupSeqRef = useRef(0);

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

    // Both password fields must agree before we spend a round trip. The
    // form is `noValidate`, so `required` alone would not stop an empty
    // confirm — this check is the gate.
    if (password !== passwordConfirm) {
      setServerError(t('auth.register.password_mismatch'));
      return;
    }

    // Local checksum gate mirrors the backend's CzechIcoValidator —
    // catches typos without a round trip.
    if (!isValidCzechIco(ico)) {
      setIcoChecksumFailed(true);
      setServerError(t('auth.register_maker.ico_checksum_invalid'));
      return;
    }

    setSubmitting(true);
    const result = await registerMaker({
      email,
      password,
      fullName,
      countryCodePrimary: 'CZ',
      registrationNumber: ico,
    });

    setSubmitting(false);
    if (result.success) {
      setDoneState({ stale: result.value.snapshotIsStale });
      return;
    }
    setServerError(mapRegisterMakerError(result.error.code, result.error.message));
  }

  const passwordMismatch = passwordConfirm.length > 0 && passwordConfirm !== password;

  if (doneState) {
    return (
      <div className="flex flex-col items-center gap-3 text-center">
        <h2 className="text-lg font-semibold text-zinc-50">{t('auth.register.success_title')}</h2>
        <p className="text-sm text-zinc-300">{t('auth.register.success_body')}</p>
        {doneState.stale && (
          <Alert variant="warning">{t('auth.register_maker.snapshot_stale_notice')}</Alert>
        )}
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
          icon="building"
          label={t('auth.register_maker.ico')}
          value={ico}
          onChange={(e) => handleIcoChange(e.target.value)}
          inputMode="numeric"
          pattern="[0-9]{8}"
          maxLength={8}
          error={icoChecksumFailed ? t('auth.register_maker.ico_checksum_invalid') : undefined}
          required
          disabled={submitting}
        />
        <p className="-mt-2 text-xs text-zinc-500">{t('auth.register_maker.ico_hint')}</p>

        {preview.kind === 'loading' && (
          <p className="-mt-1 text-xs text-zinc-400">{t('auth.register_maker.preview_loading')}</p>
        )}
        {preview.kind === 'notFound' && (
          <Alert variant="warning">{t('auth.register_maker.preview_not_found')}</Alert>
        )}
        {preview.kind === 'unavailable' && (
          <p className="-mt-1 text-xs text-zinc-500">{t('auth.register_maker.preview_unavailable')}</p>
        )}
        {preview.kind === 'found' && (
          <div className="rounded-xl border border-zinc-800 bg-surface-card p-4 text-sm">
            <p className="text-xs uppercase tracking-wide text-zinc-500">
              {t('auth.register_maker.preview_heading')}
            </p>
            <p className="mt-1 break-words font-semibold text-zinc-100">{preview.company.companyName}</p>
            {preview.company.legalForm && (
              <p className="text-xs text-zinc-400">{preview.company.legalForm}</p>
            )}
            <p className="mt-1 break-words text-zinc-300">
              {preview.company.street} {preview.company.houseNumber}, {preview.company.zip}{' '}
              {preview.company.city}
            </p>
            {preview.company.vatId && (
              <p className="break-words text-zinc-300">
                {t('auth.register_maker.preview_vat_id')}: {preview.company.vatId}
              </p>
            )}
            {preview.company.isActiveInRegistry ? (
              <p className="mt-2 text-xs text-zinc-500">
                {t('auth.register_maker.preview_confirm_hint')}
              </p>
            ) : (
              <Alert variant="error" className="mt-2">
                {t('auth.register_maker.preview_dissolved')}
              </Alert>
            )}
          </div>
        )}
        <Input
          type="text"
          icon="user"
          label={t('auth.register.full_name')}
          value={fullName}
          onChange={(e) => setFullName(e.target.value)}
          autoComplete="name"
          required
          disabled={submitting}
        />
        <Input
          type="email"
          icon="mail"
          label={t('auth.register.email')}
          value={email}
          onChange={(e) => setEmail(e.target.value)}
          autoComplete="email"
          required
          disabled={submitting}
        />
        <PasswordInput
          icon="lock"
          label={t('auth.register.password')}
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          autoComplete="new-password"
          minLength={10}
          required
          disabled={submitting}
        />
        <p className="-mt-2 text-xs text-zinc-500">{t('auth.register.password_hint')}</p>
        <PasswordInput
          icon="lock"
          label={t('auth.register.password_confirm')}
          value={passwordConfirm}
          onChange={(e) => setPasswordConfirm(e.target.value)}
          autoComplete="new-password"
          minLength={10}
          error={passwordMismatch ? t('auth.register.password_mismatch') : undefined}
          required
          disabled={submitting}
        />
        <Button type="submit" loading={submitting} className="mt-2">
          {submitting ? t('auth.register.submitting') : t('auth.register_maker.submit')}
          {!submitting ? (
            <span aria-hidden="true">
              <Icon name="arrowRight" size={16} />
            </span>
          ) : null}
        </Button>
      </form>
      <p className="text-center text-sm text-zinc-400">
        {t('auth.register.already_have_account')}{' '}
        <Link href="/login" className="text-brand-400 hover:underline">
          {t('auth.register.login_link')}
        </Link>
      </p>
    </div>
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
