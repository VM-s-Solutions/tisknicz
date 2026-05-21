'use client';

import { Suspense } from 'react';
import { useSearchParams } from 'next/navigation';
import Link from 'next/link';
import { Icon } from '@/components/ui/icon';

export default function OrderConfirmationPage() {
  return (
    <Suspense fallback={
      <div className="mx-auto max-w-2xl px-4 py-20 text-center">
        <div className="mx-auto h-20 w-20 animate-pulse rounded-full bg-zinc-800" />
        <div className="mt-6 h-10 w-64 mx-auto animate-pulse rounded bg-zinc-800" />
      </div>
    }>
      <ConfirmationContent />
    </Suspense>
  );
}

function ConfirmationContent() {
  const searchParams = useSearchParams();
  const orderId = searchParams.get('id');
  const orderNumber = searchParams.get('cislo');

  return (
    <div className="mx-auto max-w-2xl px-4 py-20 text-center sm:px-6">
      <div className="mx-auto flex h-20 w-20 items-center justify-center rounded-full bg-brand-400/10 border border-brand-400/20">
        <Icon name="checkCircle" size={40} className="text-brand-400" />
      </div>

      <h1 className="mt-6 text-3xl font-bold tracking-tight text-white">
        Objednávka vytvořena
      </h1>

      {orderNumber && (
        <p className="mt-3 text-lg text-zinc-400">
          Číslo objednávky: <span className="font-semibold text-white">{orderNumber}</span>
        </p>
      )}

      <div className="mt-8 rounded-2xl border border-zinc-800 bg-surface-card p-6 text-left">
        <h2 className="font-semibold text-white">Co bude následovat?</h2>
        <ol className="mt-4 space-y-4">
          <li className="flex gap-3">
            <span className="flex h-7 w-7 shrink-0 items-center justify-center rounded-full bg-brand-400/10 text-sm font-bold text-brand-400">1</span>
            <div>
              <p className="font-medium text-zinc-200">Platba</p>
              <p className="text-sm text-zinc-500">Budete přesměrováni na platební bránu (Comgate). Po zaplacení se objednávka automaticky potvrdí.</p>
            </div>
          </li>
          <li className="flex gap-3">
            <span className="flex h-7 w-7 shrink-0 items-center justify-center rounded-full bg-brand-400/10 text-sm font-bold text-brand-400">2</span>
            <div>
              <p className="font-medium text-zinc-200">Výroba</p>
              <p className="text-sm text-zinc-500">Maker přijme objednávku a začne s výrobou. Stav můžete sledovat v dashboardu.</p>
            </div>
          </li>
          <li className="flex gap-3">
            <span className="flex h-7 w-7 shrink-0 items-center justify-center rounded-full bg-brand-400/10 text-sm font-bold text-brand-400">3</span>
            <div>
              <p className="font-medium text-zinc-200">Doručení</p>
              <p className="text-sm text-zinc-500">Maker odešle zásilku přes Zásilkovnu. Obdržíte notifikaci s číslem zásilky.</p>
            </div>
          </li>
        </ol>
      </div>

      <div className="mt-8 flex flex-col gap-3 sm:flex-row sm:justify-center">
        {orderId && (
          <Link
            href={`/dashboard/zakaznik/objednavka/${orderId}`}
            className="inline-flex items-center justify-center gap-2 rounded-xl border border-brand-400 bg-brand-400/10 px-6 py-3 text-sm font-semibold text-brand-400 transition-all duration-200 hover:bg-brand-400/20"
          >
            Sledovat objednávku
            <Icon name="arrowRight" size={16} />
          </Link>
        )}
        <Link
          href="/katalog"
          className="inline-flex items-center justify-center gap-2 rounded-xl border border-zinc-700 px-6 py-3 text-sm font-semibold text-zinc-400 transition-all duration-200 hover:border-zinc-500 hover:text-white"
        >
          Pokračovat v nákupu
        </Link>
      </div>
    </div>
  );
}
