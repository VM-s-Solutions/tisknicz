import Link from 'next/link';
import { AuthBackButton } from '../auth-back-button';
import { AuthShell } from '../auth-shell';
import { RegisterForm } from './register-form';
import { RegisterMakerForm } from './maker/register-maker-form';
import { t } from '@/lib/i18n';

export const metadata = {
  title: 'Registrace — Makables',
};

interface RegisterPageProps {
  readonly searchParams: Promise<Record<string, string | string[] | undefined>>;
}

function readString(value: string | string[] | undefined): string {
  if (Array.isArray(value)) return value[0] ?? '';
  return value ?? '';
}

export default async function RegisterPage({ searchParams }: RegisterPageProps) {
  const sp = await searchParams;
  const selectedType = readString(sp.type) === 'maker' ? 'maker' : 'customer';

  return (
    <>
      <AuthBackButton />
      <AuthShell title={t('auth.register.page_title')} subtitle={t('auth.register.page_intro')}>
        <div className="mb-6 space-y-4">
          <div className="flex justify-center gap-8 border-b border-zinc-800 text-sm">
            <Link
              href="/register?type=customer"
              className={`-mb-px border-b pb-2.5 transition-colors ${
                selectedType === 'customer'
                  ? 'border-brand-400 font-semibold text-zinc-50'
                  : 'border-transparent text-zinc-400 hover:text-zinc-200'
              }`}
              aria-current={selectedType === 'customer' ? 'page' : undefined}
            >
              {t('auth.register.type_customer')}
            </Link>
            <Link
              href="/register?type=maker"
              className={`-mb-px border-b pb-2.5 transition-colors ${
                selectedType === 'maker'
                  ? 'border-brand-400 font-semibold text-zinc-50'
                  : 'border-transparent text-zinc-400 hover:text-zinc-200'
              }`}
              aria-current={selectedType === 'maker' ? 'page' : undefined}
            >
              {t('auth.register.type_maker')}
            </Link>
          </div>

          <p className="text-center text-sm text-zinc-400">
            {selectedType === 'maker' ? t('auth.register.maker_description') : t('auth.register.customer_description')}
          </p>
        </div>

        {selectedType === 'maker' ? <RegisterMakerForm /> : <RegisterForm />}
      </AuthShell>
    </>
  );
}