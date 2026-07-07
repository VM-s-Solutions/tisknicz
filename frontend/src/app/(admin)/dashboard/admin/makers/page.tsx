import type { Metadata } from 'next';
import { t } from '@/lib/i18n';
import { MakerLookupPanel } from './maker-lookup-panel';

/**
 * Admin makers section entry point (T-0140, US-admin-0018). There is no
 * admin maker LIST/detail READ endpoint yet (the backend shipped only the
 * `SetMakerFeeOverride` write for this ticket — see the read-gap note on
 * the detail page), so this mirrors the delete-user lookup precedent
 * (`dashboard/admin/users`): the admin already knows the maker id (from an
 * order, a support ticket, etc.) and types it in to reach the fee-override
 * form at `/dashboard/admin/makers/{id}`.
 */
export function generateMetadata(): Metadata {
  return {
    title: t('dashboard.admin.ops.makers.metadata.title'),
    description: t('dashboard.admin.ops.makers.metadata.description'),
  };
}

export default function AdminMakersLookupPage() {
  return (
    <section className="py-12 lg:py-16">
      <div className="mx-auto max-w-3xl px-4 sm:px-6 lg:px-8">
        <header className="mb-8">
          <h1 className="text-3xl font-bold tracking-tight text-white sm:text-4xl">
            {t('dashboard.admin.ops.makers.lookup.title')}
          </h1>
          <p className="mt-3 max-w-2xl text-base text-zinc-400">
            {t('dashboard.admin.ops.makers.lookup.subtitle')}
          </p>
        </header>

        <MakerLookupPanel />
      </div>
    </section>
  );
}
