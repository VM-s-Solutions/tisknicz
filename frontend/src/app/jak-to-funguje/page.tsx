import Link from 'next/link';
import { Icon } from '@/components/ui/icon';
import type { Metadata } from 'next';

export const metadata: Metadata = {
  title: 'Jak to funguje',
  description: 'Jednoduché kroky k objednávce na Makables — najdi makera, objednej, nech si doručit.',
};

export default function JakToFungujePage() {
  return (
    <div className="bg-surface-primary">
      {/* Hero */}
      <section className="py-20 sm:py-28">
        <div className="mx-auto max-w-7xl px-4 text-center sm:px-6 lg:px-8">
          <p className="text-sm font-semibold uppercase tracking-widest text-brand-400">Průvodce</p>
          <h1 className="mt-3 text-4xl font-bold tracking-tight text-white sm:text-5xl lg:text-6xl">
            Jak to funguje
          </h1>
          <p className="mx-auto mt-6 max-w-2xl text-lg text-zinc-400">
            Od nápadu k hotovému produktu ve třech jednoduchých krocích. Bez registrace, bez starostí.
          </p>
        </div>
      </section>

      {/* Kroky — rozšířená verze */}
      <section className="bg-surface-secondary py-20 sm:py-28">
        <div className="mx-auto max-w-5xl px-4 sm:px-6 lg:px-8">
          <div className="space-y-16">
            <DetailStep
              step={1}
              icon="search"
              title="Najdi svého makera"
              description="Prohlédni si katalog tvůrců ve tvém okolí. Každý maker má svůj profil s portfoliem, hodnocením a cenami."
              details={[
                'Filtruj podle kategorie — 3D tisk, laser, potisk textilu a další',
                'Vyhledávej podle města nebo PSČ',
                'Porovnej hodnocení a recenze od ostatních zákazníků',
                'Prohlédni si portfolio a nabídku produktů',
              ]}
            />

            <div className="mx-auto h-16 w-px bg-gradient-to-b from-zinc-700 to-transparent" />

            <DetailStep
              step={2}
              icon="creditCard"
              title="Objednej a zaplať"
              description="Vyber si hotový produkt z nabídky makera, nebo pošli vlastní požadavek na zakázkovou výrobu."
              details={[
                'Vyber produkt nebo pošli poptávku na míru',
                'Zvol doručení přes Zásilkovnu nebo osobní odběr',
                'Zaplať bezpečně kartou nebo bankovním převodem',
                'Platba je chráněna — maker dostane peníze až po doručení',
              ]}
            />

            <div className="mx-auto h-16 w-px bg-gradient-to-b from-zinc-700 to-transparent" />

            <DetailStep
              step={3}
              icon="package"
              title="Vyzvedni si hotový výrobek"
              description="Maker vyrobí tvůj výrobek a odešle ho přes Zásilkovnu na vybraný výdejní bod. Celý proces sleduješ online."
              details={[
                'Sleduj stav objednávky v reálném čase',
                'Komunikuj s makerem přímo přes platformu',
                'Vyzvedni si balíček na Zásilkovně nebo u makera',
                'Po převzetí ohodnoť makera a pomoz ostatním',
              ]}
            />
          </div>
        </div>
      </section>

      {/* FAQ */}
      <section className="py-20 sm:py-28">
        <div className="mx-auto max-w-3xl px-4 sm:px-6 lg:px-8">
          <div className="text-center">
            <p className="text-sm font-semibold uppercase tracking-widest text-brand-400">Časté otázky</p>
            <h2 className="mt-3 text-3xl font-bold tracking-tight text-white">
              Potřebuješ vědět víc?
            </h2>
          </div>

          <div className="mt-12 space-y-6">
            <FaqItem
              question="Kolik to stojí?"
              answer="Ceny si nastavuje každý maker sám. U každého produktu vidíš cenu předem. Platforma si účtuje 15% provizi, která je již zahrnuta v ceně."
            />
            <FaqItem
              question="Jak dlouho výroba trvá?"
              answer="Záleží na typu produktu a vytíženosti makera. Většina zakázek je hotová do 3–7 pracovních dnů. Přesný termín domluvíš s makerem."
            />
            <FaqItem
              question="Musím se registrovat?"
              answer="Pro objednávku ano — potřebujeme tvůj email pro notifikace o stavu objednávky. Registrace trvá méně než minutu."
            />
            <FaqItem
              question="Jak funguje platba?"
              answer="Platíš bezpečně kartou nebo bankovním převodem přes Comgate. Platba je chráněna — maker dostane peníze až po úspěšném doručení."
            />
            <FaqItem
              question="Co když nejsem spokojený?"
              answer="Kontaktuj nás nebo makera přímo přes platformu. Řešíme reklamace individuálně a férovost je pro nás priorita."
            />
            <FaqItem
              question="Mohu si nechat vyrobit něco na zakázku?"
              answer="Ano! Mnoho makerů přijímá zakázkovou výrobu. Pošli poptávku s popisem a maker ti pošle cenovou nabídku."
            />
          </div>
        </div>
      </section>

      {/* CTA */}
      <section className="bg-surface-secondary py-20">
        <div className="mx-auto max-w-4xl px-4 text-center sm:px-6 lg:px-8">
          <h2 className="text-3xl font-bold tracking-tight text-white">
            Připraveni začít?
          </h2>
          <p className="mt-4 text-lg text-zinc-400">
            Prohlédněte si katalog makerů a najděte toho pravého pro váš projekt.
          </p>
          <div className="mt-8 flex flex-col items-center gap-4 sm:flex-row sm:justify-center">
            <Link
              href="/katalog"
              className="group inline-flex items-center gap-2 rounded-xl border border-brand-400/50 px-8 py-4 text-base font-semibold text-brand-400 transition-all duration-200 hover:bg-brand-400/10 hover:border-brand-400"
            >
              Prohlédnout katalog
              <Icon name="arrowRight" size={18} className="transition-transform group-hover:translate-x-1" />
            </Link>
            <Link
              href="/pro-makery"
              className="inline-flex items-center gap-2 rounded-xl border border-zinc-700 px-8 py-4 text-base font-semibold text-zinc-400 transition-all duration-200 hover:border-zinc-500 hover:text-white"
            >
              Chci prodávat
            </Link>
          </div>
        </div>
      </section>
    </div>
  );
}

function DetailStep({
  step,
  icon,
  title,
  description,
  details,
}: {
  step: number;
  icon: 'search' | 'creditCard' | 'package';
  title: string;
  description: string;
  details: string[];
}) {
  return (
    <div className="flex flex-col items-center gap-6 sm:flex-row sm:items-start sm:gap-10">
      <div className="flex shrink-0 flex-col items-center gap-3">
        <span className="text-5xl font-bold text-brand-400">{step}</span>
        <div className="flex h-14 w-14 items-center justify-center rounded-2xl bg-surface-card border border-zinc-800 text-brand-400">
          <Icon name={icon} size={28} />
        </div>
      </div>

      <div className="text-center sm:text-left">
        <h3 className="text-2xl font-bold text-white">{title}</h3>
        <p className="mt-2 text-base text-zinc-400">{description}</p>
        <ul className="mt-4 space-y-2">
          {details.map((detail) => (
            <li key={detail} className="flex items-start gap-2 text-sm text-zinc-500">
              <Icon name="check" size={16} className="mt-0.5 shrink-0 text-brand-400" />
              {detail}
            </li>
          ))}
        </ul>
      </div>
    </div>
  );
}

function FaqItem({ question, answer }: { question: string; answer: string }) {
  return (
    <div className="rounded-2xl border border-zinc-800 bg-surface-card p-6">
      <h3 className="font-semibold text-zinc-200">{question}</h3>
      <p className="mt-2 text-sm leading-relaxed text-zinc-500">{answer}</p>
    </div>
  );
}
