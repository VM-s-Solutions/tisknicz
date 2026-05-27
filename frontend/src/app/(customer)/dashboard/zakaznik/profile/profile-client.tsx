'use client';

import { useRouter } from 'next/navigation';
import { useEffect, useState, type FormEvent } from 'react';
import { Alert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Input } from '@/components/ui/input';
import { Spinner } from '@/components/ui/spinner';
import { logout } from '@/lib/api-client-helpers/auth';
import {
  changePassword,
  getMyProfile,
  type MyProfile,
  updateMyProfile,
} from '@/lib/api-client-helpers/profile';
import { t } from '@/lib/i18n';

const Host = 'customer' as const;

/**
 * Customer self-service profile page. Three sections:
 *   1. Personal info (name + phone) — editable.
 *   2. Password change — current + new.
 *   3. Logout — clears session cookies, routes to /auth/login.
 *
 * Email is read-only; an email change needs re-confirmation and refresh-
 * family invalidation (separate flow, not in T-0036).
 */
export function CustomerProfileClient() {
  const router = useRouter();
  const [profile, setProfile] = useState<MyProfile | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    void (async () => {
      const result = await getMyProfile(Host);
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

  async function handleLogout() {
    await logout(Host);
    router.push('/auth/login');
  }

  if (loadError) {
    return <Alert variant="error">{loadError}</Alert>;
  }
  if (!profile) {
    return (
      <Card padding="lg" className="flex items-center gap-3 text-sm text-zinc-300">
        <Spinner />
        <span>{t('common.loading')}</span>
      </Card>
    );
  }

  return (
    <>
      <PersonalInfoSection profile={profile} onUpdated={setProfile} />
      <PasswordSection />
      <Card padding="lg">
        <Button variant="outline" onClick={handleLogout} className="w-full">
          {t('dashboard.customer.profile.logout')}
        </Button>
      </Card>
    </>
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
    <Card padding="lg" className="flex flex-col gap-5">
      <h2 className="text-lg font-semibold">{t('dashboard.customer.profile.section_personal')}</h2>
      <form onSubmit={handleSubmit} className="flex flex-col gap-4" noValidate>
        {saved && <Alert variant="success">{t('dashboard.customer.profile.saved')}</Alert>}
        {serverError && <Alert variant="error">{serverError}</Alert>}
        <Input
          type="email"
          label={t('dashboard.customer.profile.email_readonly')}
          value={profile.email}
          readOnly
          disabled
        />
        <p className="-mt-2 text-xs text-zinc-500">{t('dashboard.customer.profile.email_change_hint')}</p>
        <Input
          type="text"
          label={t('dashboard.customer.profile.full_name')}
          value={fullName}
          onChange={(e) => setFullName(e.target.value)}
          autoComplete="name"
          required
          disabled={submitting}
        />
        <Input
          type="tel"
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
    <Card padding="lg" className="flex flex-col gap-5">
      <h2 className="text-lg font-semibold">{t('dashboard.customer.profile.section_password')}</h2>
      <form onSubmit={handleSubmit} className="flex flex-col gap-4" noValidate>
        {saved && <Alert variant="success">{t('dashboard.customer.profile.password_changed')}</Alert>}
        {serverError && <Alert variant="error">{serverError}</Alert>}
        <Input
          type="password"
          label={t('dashboard.customer.profile.current_password')}
          value={currentPassword}
          onChange={(e) => setCurrentPassword(e.target.value)}
          autoComplete="current-password"
          required
          disabled={submitting}
        />
        <Input
          type="password"
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
