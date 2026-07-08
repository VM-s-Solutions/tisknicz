import { Suspense } from 'react';
import { Spinner } from '@/components/ui/spinner';
import { LoginForm } from './login-form';
import { AuthBackButton } from '../auth-back-button';
import { AuthShell } from '../auth-shell';
import { t } from '@/lib/i18n';

export const metadata = {
  title: 'Přihlášení — Makables',
};

export default function LoginPage() {
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
