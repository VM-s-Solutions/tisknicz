import { CustomerProfileClient } from './profile-client';
import { t } from '@/lib/i18n';

export const metadata = {
  title: 'Můj profil — Makables',
};

export default function CustomerProfilePage() {
  return (
    <div className="mx-auto flex max-w-2xl flex-col gap-6 px-6 py-8">
      <h1 className="text-2xl font-semibold">{t('dashboard.customer.profile.title')}</h1>
      <CustomerProfileClient />
    </div>
  );
}
