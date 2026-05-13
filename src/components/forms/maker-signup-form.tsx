'use client';

import { useState } from 'react';
import { useRouter } from 'next/navigation';
import { createBrowserClient } from '@/lib/supabase/client';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Alert } from '@/components/ui/alert';
import { Icon } from '@/components/ui/icon';

export function MakerSignupForm() {
  const router = useRouter();

  const [fullName, setFullName] = useState('');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [ico, setIco] = useState('');
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);

  function isValidIco(value: string): boolean {
    return /^\d{8}$/.test(value);
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError('');

    if (!isValidIco(ico)) {
      setError('IČO musí obsahovat přesně 8 číslic.');
      return;
    }

    setLoading(true);

    try {
      const supabase = createBrowserClient();

      const { data, error: signUpError } = await supabase.auth.signUp({
        email,
        password,
        options: {
          data: {
            full_name: fullName,
            role: 'maker',
            ico,
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
        router.push('/dashboard/maker/profil');
        router.refresh();
      }
    } catch {
      setError('Nastala neočekávaná chyba. Zkuste to znovu.');
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="mx-auto w-full max-w-md">
      <div className="rounded-2xl border border-zinc-800 bg-surface-card p-8">
        <div className="mb-6 text-center">
          <div className="mx-auto flex h-14 w-14 items-center justify-center rounded-2xl bg-brand-400/10">
            <Icon name="users" size={28} className="text-brand-400" />
          </div>
          <h3 className="mt-4 text-xl font-bold text-white">Registrace makera</h3>
          <p className="mt-1 text-sm text-zinc-500">Vyplňte údaje a začněte prodávat</p>
        </div>

        {error && <Alert variant="error" className="mb-5">{error}</Alert>}

        <form onSubmit={handleSubmit} className="flex flex-col gap-4">
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
          <Input
            label="IČO"
            type="text"
            value={ico}
            onChange={(e) => setIco(e.target.value.replace(/\D/g, '').slice(0, 8))}
            placeholder="12345678"
            required
            maxLength={8}
            pattern="\d{8}"
            error={ico.length > 0 && !isValidIco(ico) ? 'IČO musí mít 8 číslic' : undefined}
          />

          <Button type="submit" loading={loading} className="mt-2 w-full">
            <Icon name="arrowRight" size={18} />
            Zaregistrovat se jako maker
          </Button>
        </form>

        <p className="mt-5 text-center text-xs text-zinc-600">
          Po registraci vyplníte profil s daty z ARES.
        </p>

        <p className="mt-3 text-center text-xs text-zinc-600">
          Registrací souhlasíte s{' '}
          <a href="/vop" className="text-brand-400 transition-colors hover:text-brand-300 hover:underline">obchodními podmínkami</a>
          {' '}a{' '}
          <a href="/gdpr" className="text-brand-400 transition-colors hover:text-brand-300 hover:underline">ochranou osobních údajů</a>.
        </p>
      </div>
    </div>
  );
}
