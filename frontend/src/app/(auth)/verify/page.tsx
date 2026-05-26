import { VerifyClient } from './verify-client';
import { t } from '@/lib/i18n';

export const metadata = {
  title: 'Potvrzení e-mailu — Makables',
};

export default function VerifyPage() {
  return (
    <>
      <h1 className="text-2xl font-semibold">{t('auth.verify.title')}</h1>
      <VerifyClient />
    </>
  );
}
