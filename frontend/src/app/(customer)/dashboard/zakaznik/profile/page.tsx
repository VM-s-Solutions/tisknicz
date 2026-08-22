import { EmailConfirmationBanner } from '@/components/shared/email-confirmation-banner';
import { PageHeader } from '@/components/shared/page-header';
import { Alert } from '@/components/ui/alert';
import { getMyProfile } from '@/lib/api-client-helpers/profile';
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
        <Alert variant="error">{result.error.message}</Alert>
      )}
    </div>
  );
}
