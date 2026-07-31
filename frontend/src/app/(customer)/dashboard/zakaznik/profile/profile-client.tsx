'use client';

import { useRouter } from 'next/navigation';
import { useState, type FormEvent } from 'react';
import { Alert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Icon } from '@/components/ui/icon';
import { Input } from '@/components/ui/input';
import { SaveButton, type SaveState } from '@/components/ui/save-button';
import { ProfileImagePicker } from '@/components/shared/profile-image-picker';
import { SectionHeading } from '@/components/shared/section-heading';
import { logout } from '@/lib/api-client-helpers/auth';
import { buildAvatarUrl } from '@/lib/api-client-helpers/catalog';
import {
  changePassword,
  deleteMyAvatar,
  type MyProfile,
  updateMyProfile,
  uploadMyAvatar,
} from '@/lib/api-client-helpers/profile';
import { t } from '@/lib/i18n';

const Host = 'customer' as const;

/**
 * Customer self-service profile page. Three sections:
 *   1. Identity — avatar (commits on selection), name, e-mail.
 *   2. Personal info (name + phone) — editable.
 *   3. Password change — current + new.
 * Plus a logout footer.
 *
 * Email is read-only; an email change needs re-confirmation and refresh-
 * family invalidation (separate flow, not in T-0036).
 *
 * The profile itself arrives as a prop from the Server Component page
 * (T-0158 perf pass) — no client-side fetch, no loading state; this
 * boundary exists only for the form interactivity.
 *
 * <para>
 * Each form owns its save feedback through <c>SaveButton</c> rather than
 * a success alert above the fields: the alert sat off-screen once the
 * page was scrolled to the button, so a save read as a no-op.
 * </para>
 */
export function CustomerProfileClient({ initialProfile }: { initialProfile: MyProfile }) {
  const router = useRouter();
  const [profile, setProfile] = useState<MyProfile>(initialProfile);

  async function handleLogout() {
    await logout(Host);
    router.push('/login');
    router.refresh();
  }

  return (
    <>
      <IdentitySection profile={profile} onUpdated={setProfile} />
      <PersonalInfoSection profile={profile} onUpdated={setProfile} />
      <PasswordSection />
      <div className="flex justify-end border-t border-zinc-800 pt-6">
        <Button variant="dangerGhost" onClick={handleLogout} className="w-full sm:w-auto">
          <span aria-hidden="true">
            <Icon name="logOut" size={16} />
          </span>
          {t('dashboard.customer.profile.logout')}
        </Button>
      </div>
    </>
  );
}

/**
 * Identity header: who the account belongs to, plus the avatar picker.
 * The picker renders the avatar itself, so the name and e-mail ride in
 * its heading slot — a separate hero card would show the same face
 * twice, one line above the other.
 *
 * The upload commits on selection through its own endpoint, hence the
 * separate card: inside the personal-info form it would wrongly imply
 * it saves with that form's button.
 */
function IdentitySection({
  profile,
  onUpdated,
}: {
  profile: MyProfile;
  onUpdated: (next: MyProfile) => void;
}) {
  return (
    <Card variant="accent" padding="lg">
      <ProfileImagePicker
        currentUrl={buildAvatarUrl(profile.avatarBlobPath)}
        name={profile.fullName}
        hint={t('dashboard.customer.profile.avatar_hint')}
        heading={
          <div className="min-w-0">
            <p className="truncate text-lg font-semibold text-zinc-100">{profile.fullName}</p>
            <p className="truncate text-sm text-zinc-400">{profile.email}</p>
          </div>
        }
        onUpload={(file) => uploadMyAvatar(Host, file)}
        onRemove={() => deleteMyAvatar(Host)}
        onChanged={(avatarBlobPath) => onUpdated({ ...profile, avatarBlobPath })}
      />
    </Card>
  );
}

function PersonalInfoSection({
  profile,
  onUpdated,
}: {
  profile: MyProfile;
  onUpdated: (next: MyProfile) => void;
}) {
  const [fullName, setFullName] = useState(profile.fullName);
  const [phone, setPhone] = useState(profile.phone ?? '');
  const [state, setState] = useState<SaveState>('idle');
  const [serverError, setServerError] = useState<string | null>(null);

  const saving = state === 'saving';
  const dirty = fullName !== profile.fullName || phone !== (profile.phone ?? '');

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setServerError(null);
    setState('saving');

    const nextName = fullName.trim();
    const nextPhone = phone.trim();
    const result = await updateMyProfile(Host, {
      fullName: nextName,
      phone: nextPhone === '' ? null : nextPhone,
    });

    if (!result.success) {
      setState('idle');
      setServerError(result.error.message);
      return;
    }

    // Write the trimmed values back into the inputs as well: the dirty
    // check compares the fields against the stored profile, so leftover
    // whitespace would keep the form looking unsaved right after a save.
    setFullName(nextName);
    setPhone(nextPhone);
    setState('saved');
    onUpdated({ ...profile, fullName: nextName, phone: nextPhone === '' ? null : nextPhone });
  }

  return (
    <Card variant="elevated" padding="lg" className="flex flex-col gap-5">
      <SectionHeading
        icon="user"
        title={t('dashboard.customer.profile.section_personal')}
        hint={t('dashboard.customer.profile.email_change_hint')}
      />
      <form onSubmit={handleSubmit} className="flex flex-col gap-5" noValidate>
        <div className="grid gap-4 sm:grid-cols-2">
          <Input
            type="text"
            icon="user"
            label={t('dashboard.customer.profile.full_name')}
            value={fullName}
            onChange={(e) => setFullName(e.target.value)}
            autoComplete="name"
            required
            disabled={saving}
          />
          <Input
            type="tel"
            icon="phone"
            label={t('dashboard.customer.profile.phone')}
            value={phone}
            onChange={(e) => setPhone(e.target.value)}
            autoComplete="tel"
            placeholder={t('dashboard.customer.profile.phone_placeholder')}
            disabled={saving}
          />
        </div>
        {serverError && <Alert variant="error">{serverError}</Alert>}
        <div className="flex justify-end border-t border-zinc-800 pt-4">
          <SaveButton state={state} dirty={dirty} />
        </div>
      </form>
    </Card>
  );
}

function PasswordSection() {
  const [currentPassword, setCurrentPassword] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [state, setState] = useState<SaveState>('idle');
  const [serverError, setServerError] = useState<string | null>(null);

  const saving = state === 'saving';
  // Nothing to submit until both fields carry something; on success both
  // are cleared, which is what flips the button into its "saved" face.
  const dirty = currentPassword !== '' && newPassword !== '';

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setServerError(null);
    setState('saving');

    const result = await changePassword(Host, { currentPassword, newPassword });
    if (!result.success) {
      setState('idle');
      setServerError(mapPasswordError(result.error.code, result.error.message));
      return;
    }

    setCurrentPassword('');
    setNewPassword('');
    setState('saved');
  }

  return (
    <Card variant="elevated" padding="lg" className="flex flex-col gap-5">
      <SectionHeading
        icon="lock"
        title={t('dashboard.customer.profile.section_password')}
        hint={t('auth.register.password_hint')}
      />
      <form onSubmit={handleSubmit} className="flex flex-col gap-5" noValidate>
        <div className="grid gap-4 sm:grid-cols-2">
          <Input
            type="password"
            icon="lock"
            label={t('dashboard.customer.profile.current_password')}
            value={currentPassword}
            onChange={(e) => setCurrentPassword(e.target.value)}
            autoComplete="current-password"
            required
            disabled={saving}
          />
          <Input
            type="password"
            icon="key"
            label={t('dashboard.customer.profile.new_password')}
            value={newPassword}
            onChange={(e) => setNewPassword(e.target.value)}
            autoComplete="new-password"
            minLength={10}
            required
            disabled={saving}
          />
        </div>
        {serverError && <Alert variant="error">{serverError}</Alert>}
        <div className="flex justify-end border-t border-zinc-800 pt-4">
          <SaveButton
            state={state}
            dirty={dirty}
            label={t('dashboard.customer.profile.change_password')}
            savedLabel={t('dashboard.customer.profile.password_changed')}
          />
        </div>
      </form>
    </Card>
  );
}

function mapPasswordError(code: string, fallback: string): string {
  switch (code) {
    case 'auth.currentPasswordWrong':
      return t('dashboard.customer.profile.password_wrong');
    default:
      return fallback;
  }
}
