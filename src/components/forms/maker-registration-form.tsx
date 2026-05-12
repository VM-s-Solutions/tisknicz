'use client';

import { useState } from 'react';
import { useRouter } from 'next/navigation';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Textarea } from '@/components/ui/textarea';
import { Alert } from '@/components/ui/alert';
import { Icon } from '@/components/ui/icon';
import { validateICO, validateBankAccount } from '@/lib/utils/validation';
import type { Category } from '@/types';

interface MakerRegistrationFormProps {
  categories: Category[];
}

interface AresData {
  ico: string;
  companyName: string;
  street: string;
  city: string;
  zip: string;
  legalForm: string | null;
  dic: string | null;
}

export function MakerRegistrationForm({ categories }: MakerRegistrationFormProps) {
  const router = useRouter();

  const [ico, setIco] = useState('');
  const [aresLoading, setAresLoading] = useState(false);
  const [aresData, setAresData] = useState<AresData | null>(null);
  const [aresError, setAresError] = useState('');

  const [companyName, setCompanyName] = useState('');
  const [street, setStreet] = useState('');
  const [city, setCity] = useState('');
  const [zip, setZip] = useState('');
  const [dic, setDic] = useState('');
  const [legalForm, setLegalForm] = useState('');
  const [bio, setBio] = useState('');
  const [phone, setPhone] = useState('');
  const [bankAccount, setBankAccount] = useState('');
  const [website, setWebsite] = useState('');
  const [acceptsCustomOrders, setAcceptsCustomOrders] = useState(true);
  const [personalPickup, setPersonalPickup] = useState(false);
  const [pickupAddress, setPickupAddress] = useState('');
  const [pickupNote, setPickupNote] = useState('');
  const [selectedCategories, setSelectedCategories] = useState<string[]>([]);

  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);

  async function handleAresLookup() {
    if (!validateICO(ico)) {
      setAresError('Neplatné IČO. Zadejte 8 číslic.');
      return;
    }

    setAresError('');
    setAresLoading(true);

    try {
      const res = await fetch(`/api/ares/${ico}`);
      const data = await res.json();

      if (!res.ok) {
        setAresError(data.error);
        return;
      }

      setAresData(data);
      setCompanyName(data.companyName);
      setStreet(data.street);
      setCity(data.city);
      setZip(data.zip);
      setDic(data.dic ?? '');
      setLegalForm(data.legalForm ?? '');
    } catch {
      setAresError('Chyba při komunikaci s ARES. Zkuste to znovu.');
    } finally {
      setAresLoading(false);
    }
  }

  function toggleCategory(categoryId: string) {
    setSelectedCategories((prev) =>
      prev.includes(categoryId)
        ? prev.filter((id) => id !== categoryId)
        : [...prev, categoryId]
    );
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError('');

    if (!validateBankAccount(bankAccount)) {
      setError('Neplatný formát bankovního účtu. Použijte formát: 123456789/0100');
      return;
    }

    if (selectedCategories.length === 0) {
      setError('Vyberte alespoň jednu kategorii.');
      return;
    }

    setLoading(true);

    try {
      const res = await fetch('/api/makers', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          ico,
          dic: dic || null,
          company_name: companyName,
          legal_form: legalForm || null,
          street,
          city,
          zip,
          bio: bio || undefined,
          website: website || undefined,
          bank_account: bankAccount,
          accepts_custom_orders: acceptsCustomOrders,
          personal_pickup: personalPickup,
          pickup_address: personalPickup ? pickupAddress : undefined,
          pickup_note: personalPickup ? pickupNote : undefined,
          category_ids: selectedCategories,
          phone: phone || undefined,
        }),
      });

      if (!res.ok) {
        const data = await res.json();
        setError(typeof data.error === 'string' ? data.error : 'Chyba při registraci. Zkontrolujte údaje.');
        return;
      }

      router.push('/dashboard/maker');
      router.refresh();
    } catch {
      setError('Nastala neočekávaná chyba. Zkuste to znovu.');
    } finally {
      setLoading(false);
    }
  }

  return (
    <form onSubmit={handleSubmit} className="space-y-8">
      {/* ARES Lookup */}
      <SectionCard step={1} title="Údaje z ARES" description="Zadejte IČO a data se načtou automaticky z obchodního rejstříku.">
        <div className="mt-4 flex gap-3">
          <div className="flex-1">
            <Input
              label="IČO"
              value={ico}
              onChange={(e) => setIco(e.target.value.replace(/\D/g, '').slice(0, 8))}
              placeholder="12345678"
              maxLength={8}
            />
          </div>
          <div className="flex items-end">
            <Button
              type="button"
              variant="secondary"
              loading={aresLoading}
              onClick={handleAresLookup}
              disabled={ico.length !== 8}
            >
              Načíst z ARES
            </Button>
          </div>
        </div>

        {aresError && <Alert variant="error" className="mt-3">{aresError}</Alert>}

        {aresData && (
          <Alert variant="success" className="mt-3">
            Nalezeno: {aresData.companyName}
          </Alert>
        )}
      </SectionCard>

      {/* Firemní údaje */}
      {aresData && (
        <>
          <SectionCard step={2} title="Firemní údaje" description="Předvyplněno z ARES. Zkontrolujte a případně opravte.">
            <div className="mt-4 grid grid-cols-1 gap-4 sm:grid-cols-2">
              <Input label="Název firmy" value={companyName} onChange={(e) => setCompanyName(e.target.value)} required />
              <Input label="Právní forma" value={legalForm} onChange={(e) => setLegalForm(e.target.value)} />
              <Input label="DIČ" value={dic} onChange={(e) => setDic(e.target.value)} placeholder="CZ12345678" />
              <Input label="Ulice a číslo" value={street} onChange={(e) => setStreet(e.target.value)} required />
              <Input label="Město" value={city} onChange={(e) => setCity(e.target.value)} required />
              <Input label="PSČ" value={zip} onChange={(e) => setZip(e.target.value)} required />
            </div>
          </SectionCard>

          {/* Profil */}
          <SectionCard step={3} title="Váš profil">
            <div className="mt-4 flex flex-col gap-4">
              <Textarea
                label="O vás (bio)"
                value={bio}
                onChange={(e) => setBio(e.target.value)}
                placeholder="Popište své zaměření, vybavení, zkušenosti..."
                maxLength={500}
                rows={3}
              />
              <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                <Input label="Telefon" value={phone} onChange={(e) => setPhone(e.target.value)} placeholder="+420 123 456 789" />
                <Input label="Web" value={website} onChange={(e) => setWebsite(e.target.value)} placeholder="https://..." />
              </div>
              <Input
                label="Bankovní účet"
                value={bankAccount}
                onChange={(e) => setBankAccount(e.target.value)}
                placeholder="123456789/0100"
                required
              />
            </div>
          </SectionCard>

          {/* Kategorie */}
          <SectionCard step={4} title="Kategorie" description="Vyberte, co nabízíte. Lze vybrat více kategorií.">
            <div className="mt-4 grid grid-cols-2 gap-3 sm:grid-cols-3">
              {categories.map((cat) => (
                <button
                  key={cat.id}
                  type="button"
                  onClick={() => toggleCategory(cat.id)}
                  className={`flex items-center gap-2 rounded-xl border p-3 text-left text-sm font-medium transition-all duration-200 ${
                    selectedCategories.includes(cat.id)
                      ? 'border-brand-500 bg-brand-50 text-brand-700 shadow-sm'
                      : 'border-zinc-200 text-zinc-600 hover:border-zinc-300 hover:bg-zinc-50'
                  }`}
                >
                  <span>{cat.name}</span>
                </button>
              ))}
            </div>
          </SectionCard>

          {/* Doručení */}
          <SectionCard step={5} title="Nastavení">
            <div className="mt-4 flex flex-col gap-4">
              <label className="group flex cursor-pointer items-center gap-3 rounded-xl border border-zinc-200 p-4 transition-all hover:border-brand-300 hover:bg-brand-50/50">
                <input
                  type="checkbox"
                  checked={acceptsCustomOrders}
                  onChange={(e) => setAcceptsCustomOrders(e.target.checked)}
                  className="h-5 w-5 rounded-md border-zinc-300 text-brand-600 focus:ring-brand-500"
                />
                <span className="text-sm font-medium text-zinc-700">Přijímám zakázkovou výrobu na míru</span>
              </label>

              <label className="group flex cursor-pointer items-center gap-3 rounded-xl border border-zinc-200 p-4 transition-all hover:border-brand-300 hover:bg-brand-50/50">
                <input
                  type="checkbox"
                  checked={personalPickup}
                  onChange={(e) => setPersonalPickup(e.target.checked)}
                  className="h-5 w-5 rounded-md border-zinc-300 text-brand-600 focus:ring-brand-500"
                />
                <span className="text-sm font-medium text-zinc-700">Nabízím osobní odběr</span>
              </label>

              {personalPickup && (
                <div className="ml-6 flex flex-col gap-3">
                  <Input
                    label="Adresa pro odběr"
                    value={pickupAddress}
                    onChange={(e) => setPickupAddress(e.target.value)}
                    placeholder="Ulice, město"
                  />
                  <Input
                    label="Poznámka k odběru"
                    value={pickupNote}
                    onChange={(e) => setPickupNote(e.target.value)}
                    placeholder="Po-Pá 9-17, nebo po domluvě"
                  />
                </div>
              )}
            </div>
          </SectionCard>

          {error && <Alert variant="error">{error}</Alert>}

          <Button type="submit" loading={loading} size="lg" className="w-full">
            <Icon name="check" size={20} />
            Dokončit registraci makera
          </Button>
        </>
      )}
    </form>
  );
}

function SectionCard({
  step,
  title,
  description,
  children,
}: {
  step: number;
  title: string;
  description?: string;
  children: React.ReactNode;
}) {
  return (
    <div className="rounded-2xl border border-zinc-100 bg-white p-6 shadow-md sm:p-8">
      <div className="flex items-center gap-3">
        <div className="flex h-8 w-8 shrink-0 items-center justify-center rounded-lg bg-brand-600 text-sm font-bold text-white">
          {step}
        </div>
        <h2 className="text-lg font-semibold text-zinc-900">{title}</h2>
      </div>
      {description && <p className="mt-2 text-sm text-zinc-500">{description}</p>}
      {children}
    </div>
  );
}
