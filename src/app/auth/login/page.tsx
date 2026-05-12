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

function LoginForm() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const redirect = searchParams.get('redirect') ?? '/dashboard';

  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError('');
    setLoading(true);

    try {
      const supabase = createBrowserClient();
      const { error: authError } = await supabase.auth.signInWithPassword({
        email,
        password,
      });

      if (authError) {
        setError('Nesprávný email nebo heslo.');
        return;
      }

      router.push(redirect);
      router.refresh();
    } catch {
      setError('Nastala neočekávaná chyba. Zkuste to znovu.');
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="flex min-h-[calc(100vh-64px)]">
      {/* Left — branding panel */}
      <div className="relative hidden w-1/2 overflow-hidden bg-gradient-to-br from-brand-700 via-brand-600 to-brand-500 lg:flex lg:flex-col lg:items-center lg:justify-center">
        <div className="absolute inset-0 bg-dot-pattern opacity-10" />
        <div className="absolute -top-20 -left-20 h-80 w-80 rounded-full bg-brand-400/20 blur-3xl" />
        <div className="absolute -bottom-20 -right-20 h-64 w-64 rounded-full bg-brand-300/15 blur-3xl" />

        <div className="relative z-10 px-12 text-center">
          <Link href="/" className="text-4xl font-extrabold tracking-tight">
            <span className="text-gradient-light">Tiskni</span>
            <span className="text-white">.cz</span>
          </Link>
          <p className="mt-6 text-lg leading-relaxed text-brand-100">
            Marketplace pro tiskaře a makery v České republice.
          </p>

          <div className="mt-12 flex justify-center gap-8">
            <div className="text-center">
              <p className="text-3xl font-bold text-white">250+</p>
              <p className="mt-1 text-sm text-brand-200">Makerů</p>
            </div>
            <div className="h-12 w-px bg-white/20" />
            <div className="text-center">
              <p className="text-3xl font-bold text-white">6</p>
              <p className="mt-1 text-sm text-brand-200">Kategorií</p>
            </div>
          </div>
        </div>
      </div>

      {/* Right — login form */}
      <div className="flex w-full items-center justify-center px-6 py-12 lg:w-1/2">
        <div className="w-full max-w-md">
          {/* Mobile logo */}
          <div className="mb-8 lg:hidden">
            <Link href="/" className="text-2xl font-extrabold tracking-tight">
              <span className="text-gradient">Tiskni</span>
              <span className="text-zinc-900">.cz</span>
            </Link>
          </div>

          <h1 className="text-3xl font-bold tracking-tight text-zinc-900">Přihlášení</h1>
          <p className="mt-2 text-sm text-zinc-500">
            Nemáte účet?{' '}
            <Link href="/auth/register" className="font-medium text-brand-600 transition-colors hover:text-brand-700">
              Zaregistrujte se
            </Link>
          </p>

          {error && <Alert variant="error" className="mt-6">{error}</Alert>}

          <form onSubmit={handleSubmit} className="mt-8 flex flex-col gap-5">
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
              placeholder="Vaše heslo"
              required
              autoComplete="current-password"
            />
            <Button type="submit" loading={loading} className="mt-2 w-full">
              <Icon name="arrowRight" size={18} />
              Přihlásit se
            </Button>
          </form>
        </div>
      </div>
    </div>
  );
}

export default function LoginPage() {
  return (
    <Suspense fallback={<div className="flex min-h-[60vh] items-center justify-center"><Spinner size="lg" /></div>}>
      <LoginForm />
    </Suspense>
  );
}
