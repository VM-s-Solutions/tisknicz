import { RegisterMakerForm } from './register-maker-form';
import { t } from '@/lib/i18n';

export const metadata = {
  title: 'Registrace výrobce — Makables',
};

export default function RegisterMakerPage() {
  return (
    <>
      <h1 className="text-2xl font-semibold">{t('auth.register_maker.title')}</h1>
      <p className="text-sm text-zinc-400">{t('auth.register_maker.intro')}</p>
      <RegisterMakerForm />
    </>
  );
}
