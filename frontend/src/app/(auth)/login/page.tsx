import { LoginForm } from './login-form';
import { t } from '@/lib/i18n';

export const metadata = {
  title: 'Přihlášení — Makables',
};

export default function LoginPage() {
  return (
    <>
      <h1 className="text-2xl font-semibold">{t('auth.login.title')}</h1>
      <LoginForm />
    </>
  );
}
