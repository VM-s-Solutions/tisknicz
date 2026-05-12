'use client';

import { Suspense, useState } from 'react';
import Link from 'next/link';
import { useRouter, useSearchParams } from 'next/navigation';
import { createBrowserClient } from '@/lib/supabase/client';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Alert } from '@/components/ui/alert';
import { Spinner } from '@/components/ui/spinner';
import { Icon } from '@/components/ui/icon';

function RegisterForm() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const isMaker = searchParams.get('role') === 'maker';

  const [fullName, setFullName] = useState('');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [wantsMaker, setWantsMaker] = useState(isMaker);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError('');
    setLoading(true);

    try {
      const supabase = createBrowserClient();

      const { data, error: signUpError } = await supabase.auth.signUp({
        email,
        password,
        options: {
          data: {
            full_name: fullName,
            role: wantsMaker ? 'maker' : 'customer',
          },
        },
      });

      if (signUpError) {
        if (signUpError.message.includes('already registered')) {
          setError('Tento email je již zaregistrovaný.');
        } else {
          setError('Chyba při registraci. Zkuste to znovu.');
        }
        return;
      }

      if (data.user) {
        if (wantsMaker) {
          router.push('/dashboard/maker/profil');
        } else {
          router.push('/dashboard/zakaznik');
        }
        router.refresh();
      }
    } catch {
      setError('Nastala neočekávaná chyba. Zkuste to znovu.');
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="flex min-h-[calc(100vh-64px)] w-full">
      {/* Left — branding panel */}
      <div className="relative hidden w-1/2 overflow-hidden border-r border-zinc-800/50 bg-surface-secondary lg:flex lg:flex-col lg:items-center lg:justify-center">
        <div className="absolute inset-0 bg-dot-pattern opacity-5" />
        <div className="absolute -top-20 -left-20 h-80 w-80 rounded-full bg-brand-400/5 blur-3xl" />
        <div className="absolute -bottom-20 -right-20 h-64 w-64 rounded-full bg-brand-400/5 blur-3xl" />

        <div className="relative z-10 px-12 text-center">
          <Link href="/" className="text-4xl font-bold tracking-tight text-white">
            Makables
          </Link>
          <p className="mt-4 text-xl font-light tracking-wide text-zinc-400">
            Where Ideas Take Shape.
          </p>

          <div className="mt-12 space-y-4">
            <FeatureItem icon="check" text="Vlastní profil a portfolio" />
            <FeatureItem icon="check" text="Zásilkovna integrace" />
            <FeatureItem icon="check" text="Bezpečné online platby" />
          </div>
        </div>
      </div>

      {/* Right — register form */}
      <div className="flex w-full items-center justify-center bg-surface-primary px-6 py-12 lg:w-1/2">
        <div className="w-full max-w-md">
          {/* Mobile logo */}
          <div className="mb-8 lg:hidden">
            <Link href="/" className="text-2xl font-bold tracking-tight text-white">
              Makables
            </Link>
          </div>

          <h1 className="text-3xl font-bold tracking-tight text-white">Registrace</h1>
          <p className="mt-2 text-sm text-zinc-500">
            Již máte účet?{' '}
            <Link href="/auth/login" className="font-medium text-brand-400 transition-colors hover:text-brand-300">
              Přihlaste se
            </Link>
          </p>

          {error && <Alert variant="error" className="mt-6">{error}</Alert>}

          <form onSubmit={handleSubmit} className="mt-8 flex flex-col gap-5">
            <Input
              label="Celé jméno"
              type="text"
              value={fullName}
              onChange={(e) => setFullName(e.target.value)}
              placeholder="Jan Novák"
              required
            />
            <Input
              label="Email"
              type="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              placeholder="vas@email.cz"
              required
              autoComplete="email"
            />
            <Input
              label="Heslo"
              type="password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              placeholder="Min. 6 znaků"
              required
              minLength={6}
              autoComplete="new-password"
            />

            <label className="group flex cursor-pointer items-center gap-3 rounded-xl border border-zinc-700 bg-zinc-900 p-4 transition-all hover:border-brand-400/50 hover:bg-zinc-800">
              <input
                type="checkbox"
                checked={wantsMaker}
                onChange={(e) => setWantsMaker(e.target.checked)}
                className="h-5 w-5 rounded-md border-zinc-600 bg-zinc-800 text-brand-400 focus:ring-brand-400"
              />
              <div>
                <span className="text-sm font-semibold text-zinc-200">Chci prodávat jako maker</span>
                <p className="text-xs text-zinc-500">Po registraci vyplníte profil a IČO</p>
              </div>
            </label>

            <Button type="submit" loading={loading} className="mt-2 w-full">
              <Icon name="arrowRight" size={18} />
              Zaregistrovat se
            </Button>
          </form>

          <p className="mt-6 text-center text-xs text-zinc-600">
            Registrací souhlasíte s{' '}
            <Link href="/vop" className="text-brand-400 transition-colors hover:text-brand-300 hover:underline">obchodními podmínkami</Link>
            {' '}a{' '}
            <Link href="/gdpr" className="text-brand-400 transition-colors hover:text-brand-300 hover:underline">ochranou osobních údajů</Link>.
          </p>
        </div>
      </div>
    </div>
  );
}

function FeatureItem({ icon, text }: { icon: 'check'; text: string }) {
  return (
    <div className="flex items-center gap-3 text-left">
      <div className="flex h-8 w-8 shrink-0 items-center justify-center rounded-lg bg-brand-400/10">
        <Icon name={icon} size={18} className="text-brand-400" />
      </div>
      <span className="text-sm text-zinc-400">{text}</span>
    </div>
  );
}

export default function RegisterPage() {
  return (
    <Suspense fallback={<div className="flex min-h-[60vh] items-center justify-center"><Spinner size="lg" /></div>}>
      <RegisterForm />
    </Suspense>
  );
}
