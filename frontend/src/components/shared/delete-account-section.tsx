'use client';

import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { useState, type FormEvent } from 'react';
import { Alert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Icon } from '@/components/ui/icon';
import { Input } from '@/components/ui/input';
import { deleteMyAccount } from '@/lib/api-client-helpers/profile';
import { t } from '@/lib/i18n';
import type { ApiHost } from '@/lib/runtime/api-fetch';

/**
 * Self-service GDPR account deletion (soft delete) — shared by the
 * customer and maker profile pages. The backend gates the operation on
 * retyping the account e-mail and rejects it while any order is
 * in-flight; on success it revokes every session and clears the cookies,
 * so this component immediately routes to the homepage.
 */
export function DeleteAccountSection({ host }: { host: ApiHost }) {
  const router = useRouter();
  const [confirmedEmail, setConfirmedEmail] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [serverError, setServerError] = useState<string | null>(null);

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setServerError(null);
    setSubmitting(true);
    const result = await deleteMyAccount(host, { confirmedEmail: confirmedEmail.trim() });
    if (result.success) {
      // The session cookies are gone — leave the authenticated area.
      router.push('/');
      router.refresh();
      return;
    }
    setSubmitting(false);
    setServerError(mapDeleteAccountError(result.error.code, result.error.message));
  }

  return (
    <Card variant="elevated" padding="lg" className="flex flex-col gap-5 border-red-900/50">
      <h2 className="flex items-center gap-3 text-lg font-semibold text-red-400">
        <span className="icon-tile h-9 w-9 shrink-0" aria-hidden="true">
          <Icon name="trash" size={16} />
        </span>
        {t('profile.delete_account.title')}
      </h2>
      <p className="text-sm leading-relaxed text-zinc-300">
        {t('profile.delete_account.description')}{' '}
        <Link href="/gdpr" className="underline hover:text-white">
          {t('profile.delete_account.gdpr_link')}
        </Link>
      </p>
      {host === 'maker' && (
        <p className="text-sm leading-relaxed text-zinc-400">{t('profile.delete_account.maker_note')}</p>
      )}
      <form onSubmit={handleSubmit} className="flex flex-col gap-4" noValidate>
        {serverError && <Alert variant="error">{serverError}</Alert>}
        <Input
          type="email"
          icon="mail"
          label={t('profile.delete_account.confirm_label')}
          value={confirmedEmail}
          onChange={(e) => setConfirmedEmail(e.target.value)}
          autoComplete="off"
          required
          disabled={submitting}
        />
        <Button
          type="submit"
          variant="danger"
          loading={submitting}
          disabled={confirmedEmail.trim() === ''}
          className="self-start"
        >
          {submitting ? t('profile.delete_account.submitting') : t('profile.delete_account.submit')}
        </Button>
      </form>
    </Card>
  );
}

function mapDeleteAccountError(code: string, fallback: string): string {
  switch (code) {
    case 'user.deleteConfirmationMismatch':
      return t('profile.delete_account.email_mismatch');
    case 'user.cannotDeleteWithInFlightOrders':
      return t('profile.delete_account.in_flight_orders');
    default:
      return fallback;
  }
}
