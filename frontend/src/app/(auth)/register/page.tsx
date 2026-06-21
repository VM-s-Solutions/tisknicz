import Link from 'next/link';
import { RegisterForm } from './register-form';
import { t } from '@/lib/i18n';

export const metadata = {
  title: 'Vytvořit účet — Makables',
};

export default function RegisterPage() {
  return (
    <>
      <h1 className="text-2xl font-semibold">{t('auth.register.title')}</h1>
      <RegisterForm />
      <p className="text-center text-sm text-zinc-400">
        <Link href="/register/maker" className="text-brand-400 hover:underline">
          {t('auth.register.maker_link')}
        </Link>
      </p>
    </>
  );
}
