import type { Metadata } from 'next';
import { PageHeader } from '@/components/shared/page-header';
import { Alert } from '@/components/ui/alert';
import { getMyMakerProfile } from '@/lib/api-client-helpers/profile';
import { t } from '@/lib/i18n';
import { MakerProfileClient } from './profile-client';

export function generateMetadata(): Metadata {
  return {
    title: `${t('dashboard.maker.profile.title')} — ${t('common.app_name')}`,
  };
}

// Always render fresh — the profile reflects edits the maker just saved.
export const dynamic = 'force-dynamic';

/**
 * Server Component fetch, mirroring the customer profile (T-0158): the
 * profile used to load client-side in a `useEffect` against the
 * project's own no-effect-fetch rule, which cost a render → hydrate →
 * fetch → re-render waterfall plus a spinner card. The SSR fetch rides
 * the middleware-refreshed maker cookie through apiFetch's cookie
 * forwarding, so the page arrives populated in one round trip.
 */
export default async function MakerProfilePage() {
  const result = await getMyMakerProfile('maker');

  return (
    <section className="py-12 lg:py-16">
      <div className="mx-auto flex max-w-3xl flex-col gap-6 px-4 sm:px-6 lg:px-8">
        <PageHeader title={t('dashboard.maker.profile.title')} />
        {result.success ? (
          <MakerProfileClient initialProfile={result.value} />
        ) : (
          <Alert variant="error">{result.error.message}</Alert>
        )}
      </div>
    </section>
  );
}
