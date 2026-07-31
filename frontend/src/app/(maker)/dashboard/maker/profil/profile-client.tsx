'use client';

import { useEffect, useState, type FormEvent } from 'react';
import { Alert } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Icon } from '@/components/ui/icon';
import { Input } from '@/components/ui/input';
import { Spinner } from '@/components/ui/spinner';
import { DeleteAccountSection } from '@/components/shared/delete-account-section';
import { Switch } from '@/components/ui/switch';
import { Textarea } from '@/components/ui/textarea';
import {
  getMyMakerProfile,
  type MyMakerProfile,
  updateMyMakerProfile,
} from '@/lib/api-client-helpers/profile';
import { t } from '@/lib/i18n';

const Host = 'maker' as const;

/**
 * Maker self-service profile page. Surfaces:
 *   - Read-only ARES snapshot (company name, IČO, DIČ, legal form).
 *   - Verification + stale-snapshot status badges.
 *   - Editable: bio, bank account (Czech ČNB format), personal pickup
 *     toggle + note.
 *
 * Categories are deferred (out of scope per T-0034 — needs Category
 * entity from T-0040).
 */
export function MakerProfileClient() {
  const [profile, setProfile] = useState<MyMakerProfile | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    void (async () => {
      const result = await getMyMakerProfile(Host);
      if (cancelled) return;
      if (result.success) {
        setProfile(result.value);
      } else {
        setLoadError(result.error.message);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  if (loadError) {
    return <Alert variant="error">{loadError}</Alert>;
  }
  if (!profile) {
    return (
      <Card variant="elevated" padding="lg" className="flex items-center gap-3 text-sm text-zinc-300">
        <Spinner />
        <span>{t('common.loading')}</span>
      </Card>
    );
  }

  return (
    <>
      <CompanySection profile={profile} />
      <EditableSection profile={profile} onUpdated={setProfile} />
      <DeleteAccountSection host={Host} />
    </>
  );
}

function CompanySection({ profile }: { profile: MyMakerProfile }) {
  return (
    <Card variant="accent" padding="lg" className="flex flex-col gap-4">
      <div className="flex items-start justify-between gap-3">
        <div className="flex items-center gap-3">
          <span className="icon-tile h-9 w-9" aria-hidden="true">
            <Icon name="verified" size={16} />
          </span>
          <h2 className="text-lg font-semibold">{t('dashboard.maker.profile.section_company')}</h2>
        </div>
        <div className="flex flex-col items-end gap-1">
          {profile.isVerified ? (
            <Badge variant="success">{t('dashboard.maker.profile.verified')}</Badge>
          ) : (
            <Badge variant="warning">{t('dashboard.maker.profile.not_verified')}</Badge>
          )}
        </div>
      </div>
      {profile.snapshotIsStale && (
        <Alert variant="warning">{t('dashboard.maker.profile.snapshot_stale')}</Alert>
      )}
      <dl className="grid grid-cols-1 gap-3 text-sm sm:grid-cols-2">
        <ReadonlyField label={t('dashboard.maker.profile.company_name')} value={profile.companyName} />
        <ReadonlyField label={t('dashboard.maker.profile.ico')} value={profile.registrationNumber} />
        <ReadonlyField label={t('dashboard.maker.profile.vat_id')} value={profile.vatId ?? '—'} />
        <ReadonlyField label={t('dashboard.maker.profile.legal_form')} value={profile.legalForm ?? '—'} />
      </dl>
      <p className="text-xs text-zinc-500">{t('dashboard.maker.profile.readonly_hint')}</p>
    </Card>
  );
}

function ReadonlyField({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex flex-col gap-1">
      <dt className="text-xs uppercase tracking-wide text-zinc-500">{label}</dt>
      <dd className="text-sm text-zinc-100">{value}</dd>
    </div>
  );
}

function EditableSection({
  profile,
  onUpdated,
}: {
  profile: MyMakerProfile;
  onUpdated: (next: MyMakerProfile) => void;
}) {
  const [bio, setBio] = useState(profile.bio ?? '');
  const [bankAccount, setBankAccount] = useState(profile.bankAccount ?? '');
  const [personalPickupEnabled, setPersonalPickupEnabled] = useState(profile.personalPickupEnabled);
  const [pickupNote, setPickupNote] = useState(profile.pickupNote ?? '');
  const [submitting, setSubmitting] = useState(false);
  const [serverError, setServerError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setServerError(null);
    setSaved(false);
    setSubmitting(true);
    const result = await updateMyMakerProfile(Host, {
      bio: bio,
      bankAccount: bankAccount,
      personalPickupEnabled,
      pickupNote: pickupNote,
    });
    setSubmitting(false);
    if (result.success) {
      setSaved(true);
      onUpdated({
        ...profile,
        bio: bio.trim() === '' ? null : bio.trim(),
        bankAccount: bankAccount.trim() === '' ? null : bankAccount.trim(),
        personalPickupEnabled,
        pickupNote: pickupNote.trim() === '' ? null : pickupNote.trim(),
      });
      return;
    }
    setServerError(mapMakerProfileError(result.error.code, result.error.message));
  }

  return (
    <form onSubmit={handleSubmit} className="flex flex-col gap-6" noValidate>
      {saved && <Alert variant="success">{t('dashboard.maker.profile.saved')}</Alert>}
      {serverError && <Alert variant="error">{serverError}</Alert>}

      <Card variant="elevated" padding="lg">
        <section className="flex flex-col gap-3">
          <div className="flex items-center gap-3">
            <span className="icon-tile h-9 w-9" aria-hidden="true">
              <Icon name="user" size={16} />
            </span>
            <h2 className="text-lg font-semibold">{t('dashboard.maker.profile.section_about')}</h2>
          </div>
          <Textarea
            label={t('dashboard.maker.profile.bio')}
            value={bio}
            onChange={(e) => setBio(e.target.value)}
            maxLength={500}
            rows={4}
            disabled={submitting}
          />
        </section>
      </Card>

      <Card variant="elevated" padding="lg">
        <section className="flex flex-col gap-3">
          <div className="flex items-center gap-3">
            <span className="icon-tile h-9 w-9" aria-hidden="true">
              <Icon name="creditCard" size={16} />
            </span>
            <h2 className="text-lg font-semibold">{t('dashboard.maker.profile.section_bank')}</h2>
          </div>
          <Input
            type="text"
            label={t('dashboard.maker.profile.bank_account')}
            value={bankAccount}
            onChange={(e) => setBankAccount(e.target.value)}
            placeholder={t('dashboard.maker.profile.bank_account_placeholder')}
            disabled={submitting}
          />
        </section>
      </Card>

      <Card variant="elevated" padding="lg">
        <section className="flex flex-col gap-3">
          <div className="flex items-center gap-3">
            <span className="icon-tile h-9 w-9" aria-hidden="true">
              <Icon name="mapPin" size={16} />
            </span>
            <h2 className="text-lg font-semibold">{t('dashboard.maker.profile.section_pickup')}</h2>
          </div>
          <Switch
            checked={personalPickupEnabled}
            onChange={(e) => setPersonalPickupEnabled(e.target.checked)}
            disabled={submitting}
            label={t('dashboard.maker.profile.pickup_enabled')}
          />
          <Textarea
            label={t('dashboard.maker.profile.pickup_note')}
            value={pickupNote}
            onChange={(e) => setPickupNote(e.target.value)}
            maxLength={500}
            rows={3}
            disabled={submitting || !personalPickupEnabled}
          />
        </section>
      </Card>

      <Button type="submit" loading={submitting} className="self-start">
        {!submitting ? <Icon name="save" size={16} /> : null}
        {submitting ? t('dashboard.maker.profile.saving') : t('dashboard.maker.profile.save')}
      </Button>
    </form>
  );
}

function mapMakerProfileError(code: string, fallback: string): string {
  switch (code) {
    case 'validation.bankAccountFormat':
      return t('dashboard.maker.profile.bank_account_invalid');
    default:
      return fallback;
  }
}
