import { Suspense } from 'react';
import { Card } from '@/components/ui/card';
import { Spinner } from '@/components/ui/spinner';
import { LoginForm } from './login-form';
import { t } from '@/lib/i18n';

export const metadata = {
  title: 'Přihlášení — Makables',
};

export default function LoginPage() {
  return (
    <>
      <h1 className="text-2xl font-semibold">{t('auth.login.title')}</h1>
      {/* Suspense boundary required by Next.js 16: LoginForm reads useSearchParams() for the post-login redirect target, which bails static prerender — wrap so the heading + layout prerender and only the form streams in. */}
      <Suspense fallback={<FormSkeleton />}>
        <LoginForm />
      </Suspense>
    </>
  );
}

function FormSkeleton() {
  return (
    <Card padding="lg" className="flex items-center gap-3 text-sm text-zinc-300">
      <Spinner />
      <span>{t('common.loading')}</span>
    </Card>
  );
}
