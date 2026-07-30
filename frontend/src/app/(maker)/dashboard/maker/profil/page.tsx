import { PageHeader } from '@/components/shared/page-header';
import { t } from '@/lib/i18n';
import { MakerProfileClient } from './profile-client';

export const metadata = {
  title: 'Profil výrobce — Makables',
};

export default function MakerProfilePage() {
  return (
    <section className="bg-surface-primary py-12 lg:py-16">
      <div className="mx-auto flex max-w-3xl flex-col gap-6 px-4 sm:px-6 lg:px-8">
        <PageHeader title={t('dashboard.maker.profile.title')} />
        <MakerProfileClient />
      </div>
    </section>
  );
}
