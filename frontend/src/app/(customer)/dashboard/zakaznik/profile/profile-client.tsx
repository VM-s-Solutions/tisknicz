'use client';

import { useRouter } from 'next/navigation';
import { useState, type FormEvent } from 'react';
import { Alert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Icon } from '@/components/ui/icon';
import { Input } from '@/components/ui/input';
import { DeleteAccountSection } from '@/components/shared/delete-account-section';
import { logout } from '@/lib/api-client-helpers/auth';
import {
  changePassword,
  type MyProfile,
  updateMyProfile,
} from '@/lib/api-client-helpers/profile';
import { t } from '@/lib/i18n';

const Host = 'customer' as const;

/**
 * Customer self-service profile page. Three sections:
 *   1. Personal info (name + phone) — editable.
 *   2. Password change — current + new.
 *   3. Logout — clears session cookies, routes to /login.
 *
 * Email is read-only; an email change needs re-confirmation and refresh-
 * family invalidation (separate flow, not in T-0036).
 *
 * The profile itself arrives as a prop from the Server Component page
 * (T-0158 perf pass) — no client-side fetch, no loading state; this
 * boundary exists only for the form interactivity.
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
      <ProfileHero profile={profile} />
      <PersonalInfoSection profile={profile} onUpdated={setProfile} />
      <PasswordSection />
      <Card variant="elevated" padding="lg">
        <Button variant="outline" onClick={handleLogout} className="w-full">
          <span aria-hidden="true">
            <Icon name="logOut" size={16} />
          </span>
          {t('dashboard.customer.profile.logout')}
        </Button>
      </Card>
      <DeleteAccountSection host={Host} />
    </>
  );
}

/** Presentation-only initials for the avatar tile (first + last word of the name). */
function avatarInitials(fullName: string): string {
  const words = fullName.trim().split(/\s+/).filter(Boolean);
  if (words.length === 0) return '';
  const first = words[0].charAt(0);
  const last = words.length > 1 ? words[words.length - 1].charAt(0) : '';
  return `${first}${last}`.toUpperCase();
}

function ProfileHero({ profile }: { profile: MyProfile }) {
  const initials = avatarInitials(profile.fullName);
  return (
    <Card variant="accent" padding="md" className="flex items-center gap-4">
      <span className="icon-tile h-14 w-14 shrink-0 text-lg font-semibold" aria-hidden="true">
        {initials !== '' ? initials : <Icon name="user" size={24} />}
      </span>
      <div className="min-w-0">
        <p className="truncate text-lg font-semibold text-white">{profile.fullName}</p>
        <p className="truncate text-sm text-zinc-400">{profile.email}</p>
      </div>
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
  const [submitting, setSubmitting] = useState(false);
  const [serverError, setServerError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setServerError(null);
    setSaved(false);
    setSubmitting(true);
    const result = await updateMyProfile(Host, {
      fullName,
      phone: phone.trim() === '' ? null : phone.trim(),
    });
    setSubmitting(false);
    if (result.success) {
      setSaved(true);
      onUpdated({ ...profile, fullName, phone: phone.trim() === '' ? null : phone.trim() });
      return;
    }
    setServerError(result.error.message);
  }

  return (
    <Card variant="elevated" padding="lg" className="flex flex-col gap-5">
      <h2 className="flex items-center gap-3 text-lg font-semibold">
        <span className="icon-tile h-9 w-9 shrink-0" aria-hidden="true">
          <Icon name="user" size={16} />
        </span>
        {t('dashboard.customer.profile.section_personal')}
      </h2>
      <form onSubmit={handleSubmit} className="flex flex-col gap-4" noValidate>
        {saved && <Alert variant="success">{t('dashboard.customer.profile.saved')}</Alert>}
        {serverError && <Alert variant="error">{serverError}</Alert>}
        <Input
          type="email"
          icon="mail"
          label={t('dashboard.customer.profile.email_readonly')}
          value={profile.email}
          readOnly
          disabled
        />
        <p className="-mt-2 text-xs text-zinc-500">{t('dashboard.customer.profile.email_change_hint')}</p>
        <Input
          type="text"
          icon="user"
          label={t('dashboard.customer.profile.full_name')}
          value={fullName}
          onChange={(e) => setFullName(e.target.value)}
          autoComplete="name"
          required
          disabled={submitting}
        />
        <Input
          type="tel"
          icon="phone"
          label={t('dashboard.customer.profile.phone')}
          value={phone}
          onChange={(e) => setPhone(e.target.value)}
          autoComplete="tel"
          placeholder={t('dashboard.customer.profile.phone_placeholder')}
          disabled={submitting}
        />
        <Button type="submit" loading={submitting} className="mt-2 self-start">
          {submitting ? t('dashboard.customer.profile.saving') : t('dashboard.customer.profile.save')}
        </Button>
      </form>
    </Card>
  );
}

function PasswordSection() {
  const [currentPassword, setCurrentPassword] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [serverError, setServerError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setServerError(null);
    setSaved(false);
    setSubmitting(true);
    const result = await changePassword(Host, { currentPassword, newPassword });
    setSubmitting(false);
    if (result.success) {
      setSaved(true);
      setCurrentPassword('');
      setNewPassword('');
      return;
    }
    setServerError(mapPasswordError(result.error.code, result.error.message));
  }

  return (
    <Card variant="elevated" padding="lg" className="flex flex-col gap-5">
      <h2 className="flex items-center gap-3 text-lg font-semibold">
        <span className="icon-tile h-9 w-9 shrink-0" aria-hidden="true">
          <Icon name="settings" size={16} />
        </span>
        {t('dashboard.customer.profile.section_password')}
      </h2>
      <form onSubmit={handleSubmit} className="flex flex-col gap-4" noValidate>
        {saved && <Alert variant="success">{t('dashboard.customer.profile.password_changed')}</Alert>}
        {serverError && <Alert variant="error">{serverError}</Alert>}
        <Input
          type="password"
          icon="lock"
          label={t('dashboard.customer.profile.current_password')}
          value={currentPassword}
          onChange={(e) => setCurrentPassword(e.target.value)}
          autoComplete="current-password"
          required
          disabled={submitting}
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
          disabled={submitting}
        />
        <p className="text-xs text-zinc-500">{t('auth.register.password_hint')}</p>
        <Button type="submit" loading={submitting} className="mt-2 self-start">
          {t('dashboard.customer.profile.change_password')}
        </Button>
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
