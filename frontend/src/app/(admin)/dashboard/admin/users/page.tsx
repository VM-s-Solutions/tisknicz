import type { Metadata } from 'next';
import { Alert } from '@/components/ui/alert';
import { t } from '@/lib/i18n';
import { DeleteUserPanel } from './delete-user-panel';

/**
 * Admin GDPR delete-user (T-0118c §4, US-admin-0016) — THE DANGEROUS
 * SCREEN, built last (risk-ascending). Server Component, `force-dynamic`.
 * The only frontend that drives an irreversible hard-delete of user data.
 *
 * The double interlock the backend enforces (T-0110) is surfaced
 * PROACTIVELY on the client where possible: the type-the-exact-email
 * confirmation (A.1, the strongest friction, RESERVED for this screen) and
 * the always-visible irreversibility banner (A.2a). The in-flight-order
 * block (A.2b — `user.cannotDeleteWithInFlightOrders`) is server-enforced;
 * see the panel for how it surfaces.
 *
 * CONTRACT GAP (flagged): the admin contract exposes ONLY `erase` — there
 * is NO user-lookup read and NO per-user-order read. The operator therefore
 * enters the user id + email manually (the lookup-by-list deep-link from
 * T-0118a's read would carry these), and the in-flight block surfaces as
 * the backend's post-call verdict rather than a pre-disabled button. A
 * user-lookup + per-user-order read is the clean fix and is logged as a
 * backend follow-up.
 *
 * The PR carries §secops Gate 3 + architect sign-off (the only hard-delete
 * affordance + the control-plane mutation surface).
 */

export function generateMetadata(): Metadata {
  return {
    title: t('dashboard.admin.ops.users.metadata.title'),
    description: t('dashboard.admin.ops.users.metadata.description'),
  };
}

export const dynamic = 'force-dynamic';

export default function AdminUsersPage() {
  return (
    <section className="py-12 lg:py-16">
      <div className="mx-auto max-w-2xl px-4 sm:px-6 lg:px-8">
        <header className="mb-8">
          <h1 className="text-3xl font-bold tracking-tight text-white sm:text-4xl">
            {t('dashboard.admin.ops.users.title')}
          </h1>
          <p className="mt-3 max-w-2xl text-base text-zinc-400">
            {t('dashboard.admin.ops.users.subtitle')}
          </p>
        </header>

        <Alert variant="info" className="mb-6">
          <div>
            <p className="font-semibold">{t('dashboard.admin.ops.users.lookupGap.title')}</p>
            <p className="mt-1 text-sm opacity-90">
              {t('dashboard.admin.ops.users.lookupGap.body')}
            </p>
          </div>
        </Alert>

        <DeleteUserPanel />
      </div>
    </section>
  );
}
