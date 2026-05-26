import { MagicClient } from './magic-client';
import { t } from '@/lib/i18n';

export const metadata = {
  title: 'Přihlášení odkazem — Makables',
};

export default function MagicPage() {
  return (
    <>
      <h1 className="text-2xl font-semibold">{t('auth.magic.title')}</h1>
      <MagicClient />
    </>
  );
}
