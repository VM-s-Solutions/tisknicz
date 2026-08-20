import { Suspense } from 'react';
import { Spinner } from '@/components/ui/spinner';
import { LoginForm } from './login-form';
import { AlreadySignedIn } from '../already-signed-in';
import { AuthBackButton } from '../auth-back-button';
import { AuthShell } from '../auth-shell';
import { SwitchAccountButton } from '../switch-account-button';
import { getDisplaySession } from '@/lib/auth/display-session';
import { safeRedirectTarget } from '@/lib/auth';
import { t } from '@/lib/i18n';

export const metadata = {
  title: 'Přihlášení — Makables',
};

interface PageProps {
  readonly searchParams: Promise<Record<string, string | string[] | undefined>>;
}

export default async function LoginPage({ searchParams }: PageProps) {
  // A live session must never be answered with a bare login form: an
  // account is bound to one audience, so a maker bounced here from a
  // customer-only route (checkout, /dashboard/zakaznik/*) could log in
  // forever without ever passing the guard. Browser Back hit the same
  // wall. Both now land on the "already signed in" panel instead.
  const session = await getDisplaySession();
  if (session) {
    const sp = await searchParams;
    const raw = sp.redirect;
    const redirect = safeRedirectTarget(Array.isArray(raw) ? raw[0] : raw);
    return (
      <>
        <AuthBackButton />
        <AuthShell title={t('auth.signedIn.title')} subtitle={t('auth.signedIn.subtitle')}>
          <AlreadySignedIn
            session={session}
            redirect={redirect}
            switchAccount={<SwitchAccountButton audience={session.audience} />}
          />
        </AuthShell>
      </>
    );
  }

  return (
    <>
      <AuthBackButton />
      <AuthShell title={t('auth.login.title')} subtitle={t('auth.login.subtitle')}>
        {/* Suspense boundary required by Next.js 16: LoginForm reads useSearchParams() for the post-login redirect target, which bails static prerender — wrap so the heading + layout prerender and only the form streams in. */}
        <Suspense fallback={<FormSkeleton />}>
          <LoginForm />
        </Suspense>
      </AuthShell>
    </>
  );
}

function FormSkeleton() {
  return (
    <div className="flex items-center justify-center gap-3 py-10 text-sm text-zinc-300">
      <Spinner />
      <span>{t('common.loading')}</span>
    </div>
  );
}
