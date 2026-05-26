import { MakerProfileClient } from './profile-client';
import { t } from '@/lib/i18n';

export const metadata = {
  title: 'Profil výrobce — Makables',
};

export default function MakerProfilePage() {
  return (
    <div className="mx-auto flex max-w-3xl flex-col gap-6 px-6 py-8">
      <h1 className="text-2xl font-semibold">{t('dashboard.maker.profile.title')}</h1>
      <MakerProfileClient />
    </div>
  );
}
