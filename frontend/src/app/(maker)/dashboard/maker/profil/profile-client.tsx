'use client';

import { useState, type FormEvent } from 'react';
import { Alert } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Card } from '@/components/ui/card';
import { Input } from '@/components/ui/input';
import { SaveButton, type SaveState } from '@/components/ui/save-button';
import { Switch } from '@/components/ui/switch';
import { Textarea } from '@/components/ui/textarea';
import { ProfileImagePicker } from '@/components/shared/profile-image-picker';
import { SectionHeading } from '@/components/shared/section-heading';
import { buildMakerLogoUrl } from '@/lib/api-client-helpers/catalog';
import {
  deleteMyMakerLogo,
  type MyMakerProfile,
  updateMyMakerProfile,
  uploadMyMakerLogo,
} from '@/lib/api-client-helpers/profile';
import { t } from '@/lib/i18n';

const Host = 'maker' as const;

/**
 * Maker self-service profile page. Two cards:
 *   1. Company identity — logo (commits on selection) beside the
 *      read-only ARES snapshot and the verification badge.
 *   2. Editable profile — bio, bank account, personal pickup — as
 *      hairline-separated sections under one save button.
 *
 * The profile arrives as a prop from the Server Component page, so there
 * is no client fetch and no spinner pass.
 *
 * Categories are deferred (out of scope per T-0034 — needs Category
 * entity from T-0040).
 */
export function MakerProfileClient({ initialProfile }: { initialProfile: MyMakerProfile }) {
  const [profile, setProfile] = useState<MyMakerProfile>(initialProfile);

  return (
    <>
      <CompanySection profile={profile} onUpdated={setProfile} />
      <EditableSection profile={profile} onUpdated={setProfile} />
    </>
  );
}

/**
 * Company identity. The logo picker lives here rather than in its own
 * card: it commits on selection through a separate endpoint, so keeping
 * it out of the editable form is what matters — and next to the registry
 * data it reads as "this is who you are in the catalog" instead of a
 * lone card holding a single control.
 */
function CompanySection({
  profile,
  onUpdated,
}: {
  profile: MyMakerProfile;
  onUpdated: (next: MyMakerProfile) => void;
}) {
  return (
    <Card variant="accent" padding="lg" className="flex flex-col gap-5">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <SectionHeading icon="verified" title={t('dashboard.maker.profile.section_company')} />
        {profile.isVerified ? (
          <Badge variant="success">{t('dashboard.maker.profile.verified')}</Badge>
        ) : (
          <Badge variant="warning">{t('dashboard.maker.profile.not_verified')}</Badge>
        )}
      </div>

      {profile.snapshotIsStale && (
        <Alert variant="warning">{t('dashboard.maker.profile.snapshot_stale')}</Alert>
      )}

      <ProfileImagePicker
        currentUrl={buildMakerLogoUrl(profile.logoBlobPath)}
        name={profile.companyName}
        hint={t('dashboard.maker.profile.logo_hint')}
        heading={
          <p className="truncate text-lg font-semibold text-zinc-100">{profile.companyName}</p>
        }
        onUpload={(file) => uploadMyMakerLogo(Host, file)}
        onRemove={() => deleteMyMakerLogo(Host)}
        onChanged={(logoBlobPath) => onUpdated({ ...profile, logoBlobPath })}
      />

      <dl className="grid gap-x-8 gap-y-3 border-t border-zinc-800 pt-5 sm:grid-cols-2">
        <ReadonlyField label={t('dashboard.maker.profile.ico')} value={profile.registrationNumber} />
        <ReadonlyField label={t('dashboard.maker.profile.vat_id')} value={profile.vatId ?? '—'} />
        <ReadonlyField
          label={t('dashboard.maker.profile.legal_form')}
          value={profile.legalForm ?? '—'}
        />
      </dl>
      <p className="text-xs text-zinc-500">{t('dashboard.maker.profile.readonly_hint')}</p>
    </Card>
  );
}

function ReadonlyField({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex min-w-0 flex-col gap-0.5">
      <dt className="text-xs text-zinc-500">{label}</dt>
      <dd className="break-words text-sm text-zinc-100">{value}</dd>
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
  const [state, setState] = useState<SaveState>('idle');
  const [serverError, setServerError] = useState<string | null>(null);

  const saving = state === 'saving';
  const dirty =
    bio !== (profile.bio ?? '') ||
    bankAccount !== (profile.bankAccount ?? '') ||
    personalPickupEnabled !== profile.personalPickupEnabled ||
    pickupNote !== (profile.pickupNote ?? '');

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setServerError(null);
    setState('saving');

    // Empty string clears the field on the backend; null means "leave
    // unchanged" (UpdateMakerProfile field semantics), so an emptied
    // input must ride the wire as '' — never as null.
    const nextBio = bio.trim();
    const nextBankAccount = bankAccount.trim();
    const nextPickupNote = pickupNote.trim();

    const result = await updateMyMakerProfile(Host, {
      bio: nextBio,
      bankAccount: nextBankAccount,
      personalPickupEnabled,
      pickupNote: nextPickupNote,
    });

    if (!result.success) {
      setState('idle');
      setServerError(mapMakerProfileError(result.error.code, result.error.message));
      return;
    }

    // Mirror the stored (trimmed) values into both the inputs and the
    // profile so the dirty check settles at clean after a save.
    setBio(nextBio);
    setBankAccount(nextBankAccount);
    setPickupNote(nextPickupNote);
    setState('saved');
    onUpdated({
      ...profile,
      bio: nextBio === '' ? null : nextBio,
      bankAccount: nextBankAccount === '' ? null : nextBankAccount,
      personalPickupEnabled,
      pickupNote: nextPickupNote === '' ? null : nextPickupNote,
    });
  }

  return (
    <form onSubmit={handleSubmit} noValidate>
      <Card variant="elevated" padding="lg" className="flex flex-col gap-6">
        <section className="flex flex-col gap-4">
          <SectionHeading icon="user" title={t('dashboard.maker.profile.section_about')} />
          <Textarea
            label={t('dashboard.maker.profile.bio')}
            value={bio}
            onChange={(e) => setBio(e.target.value)}
            maxLength={500}
            rows={4}
            disabled={saving}
          />
        </section>

        <section className="flex flex-col gap-4 border-t border-zinc-800 pt-6">
          <SectionHeading icon="creditCard" title={t('dashboard.maker.profile.section_bank')} />
          <div className="sm:max-w-sm">
            <Input
              type="text"
              label={t('dashboard.maker.profile.bank_account')}
              value={bankAccount}
              onChange={(e) => setBankAccount(e.target.value)}
              placeholder={t('dashboard.maker.profile.bank_account_placeholder')}
              disabled={saving}
            />
          </div>
        </section>

        <section className="flex flex-col gap-4 border-t border-zinc-800 pt-6">
          <SectionHeading icon="mapPin" title={t('dashboard.maker.profile.section_pickup')} />
          <Switch
            checked={personalPickupEnabled}
            onChange={(e) => setPersonalPickupEnabled(e.target.checked)}
            disabled={saving}
            label={t('dashboard.maker.profile.pickup_enabled')}
          />
          <Textarea
            label={t('dashboard.maker.profile.pickup_note')}
            value={pickupNote}
            onChange={(e) => setPickupNote(e.target.value)}
            maxLength={500}
            rows={3}
            disabled={saving || !personalPickupEnabled}
          />
        </section>

        <div className="flex flex-col gap-4 border-t border-zinc-800 pt-6">
          {serverError && <Alert variant="error">{serverError}</Alert>}
          <div className="flex justify-end">
            <SaveButton state={state} dirty={dirty} />
          </div>
        </div>
      </Card>
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
