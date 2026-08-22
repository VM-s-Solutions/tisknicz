import { EmailConfirmationBanner } from '@/components/shared/email-confirmation-banner';
import { PageHeader } from '@/components/shared/page-header';
import { ProfileLoadError } from '@/components/shared/profile-load-error';
import { Alert } from '@/components/ui/alert';
import { getMyProfile } from '@/lib/api-client-helpers/profile';
import { redirect } from 'next/navigation';
import { t } from '@/lib/i18n';
import { CustomerProfileClient } from './profile-client';

export const metadata = {
  title: `${t('dashboard.customer.profile.title')} — ${t('common.app_name')}`,
};

// Always render fresh — the profile reflects edits the user just saved.
export const dynamic = 'force-dynamic';

/**
 * Server Component fetch (T-0158 perf pass): the profile used to load
 * client-side in a `useEffect` (against the project's own no-effect-fetch
 * rule), which cost a full render → hydrate → fetch → re-render
 * waterfall plus a spinner. The SSR fetch rides the T-0154
 * middleware-refreshed cookie via apiFetch's cookie forwarding, so the
 * page arrives populated in one round trip.
 */
export default async function CustomerProfilePage() {
  const result = await getMyProfile('customer');

  // T-0173 (audit CUST-M1): an expired session rendered a dead-end alert
  // carrying the RAW backend message — against the project's own rule that
  // `error.message` never reaches the UI — while the orders page one route
  // over redirected correctly. Parity restored.
  if (!result.success && result.error.type === 'Unauthorized') {
    redirect(`/login?redirect=${encodeURIComponent('/dashboard/zakaznik/profile')}`);
  }

  return (
    <div className="mx-auto flex max-w-3xl flex-col gap-6 px-6 py-8">
      <PageHeader title={t('dashboard.customer.profile.title')} />
      {result.success ? (
        <>
          {/* T-0168 (audit CUST-H2): checkout's unconfirmed-email error
              says "resend from your profile" — this banner is that
              affordance; it existed but was mounted NOWHERE. */}
          {!result.value.emailConfirmed ? (
            <EmailConfirmationBanner email={result.value.email} />
          ) : null}
          <CustomerProfileClient initialProfile={result.value} />
        </>
      ) : (
        <ProfileLoadError error={result.error} />
      )}
    </div>
  );
}
