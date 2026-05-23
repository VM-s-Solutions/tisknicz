import Link from 'next/link';
import { Icon } from '@/components/ui/icon';
import type { Metadata } from 'next';

export const metadata: Metadata = {
  title: 'Pro makery',
  description: 'Prodávejte na Makables — marketplace pro tiskaře a makery v ČR. Začněte prodávat za pár minut.',
};

export default function ProMakeryPage() {
  return (
    <div className="bg-surface-primary">
      {/* Hero */}
      <section className="relative overflow-hidden py-20 sm:py-28">
        <div className="absolute inset-0 bg-dot-pattern opacity-5" />
        <div className="absolute -top-20 -right-20 h-80 w-80 rounded-full bg-brand-400/5 blur-3xl" />

        <div className="relative mx-auto max-w-7xl px-4 text-center sm:px-6 lg:px-8">
          <p className="text-sm font-semibold uppercase tracking-widest text-brand-400">Pro makery</p>
          <h1 className="mt-3 text-4xl font-bold tracking-tight text-white sm:text-5xl lg:text-6xl">
            Prodávejte na Makables
          </h1>
          <p className="mx-auto mt-6 max-w-2xl text-lg text-zinc-400">
            Zaregistrujte se, vytvořte si profil a začněte přijímat objednávky. Vše potřebné máme připravené — od plateb po doručení.
          </p>
          <div className="mt-10">
            <a
              href="#registrace"
              className="group inline-flex items-center gap-2 rounded-xl border border-brand-400 bg-brand-400/10 px-8 py-4 text-base font-semibold text-brand-400 transition-all duration-200 hover:bg-brand-400/20"
            >
              Registrovat se jako maker
              <Icon name="arrowRight" size={18} className="transition-transform group-hover:translate-x-1" />
            </a>
          </div>
        </div>
      </section>

      {/* Výhody */}
      <section className="bg-surface-secondary py-20 sm:py-28">
        <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8">
          <div className="text-center">
            <p className="text-sm font-semibold uppercase tracking-widest text-brand-400">Výhody</p>
            <h2 className="mt-3 text-3xl font-bold tracking-tight text-white sm:text-4xl">
              Proč prodávat na Makables?
            </h2>
          </div>

          <div className="mt-12 grid grid-cols-1 gap-6 sm:grid-cols-2 lg:grid-cols-4">
            <BenefitCard
              icon="users"
              title="Zákazníci z celé ČR"
              description="Vaše nabídka bude viditelná pro tisíce zákazníků, kteří hledají lokální makery."
            />
            <BenefitCard
              icon="creditCard"
              title="Bezpečné platby"
              description="O platby se staráme my. Zákazník platí kartou nebo převodem, vy dostanete peníze po doručení."
            />
            <BenefitCard
              icon="truck"
              title="Zásilkovna integrace"
              description="Odesílejte přes naši Zásilkovnu — stačí vytisknout štítek. Bez vlastní smlouvy."
            />
            <BenefitCard
              icon="file"
              title="Automatické faktury"
              description="Faktury se generují automaticky. Žádné papírování, žádné účetnictví navíc."
            />
          </div>
        </div>
      </section>

      {/* Jak začít */}
      <section className="py-20 sm:py-28">
        <div className="mx-auto max-w-5xl px-4 sm:px-6 lg:px-8">
          <div className="text-center">
            <p className="text-sm font-semibold uppercase tracking-widest text-brand-400">Začněte za 5 minut</p>
            <h2 className="mt-3 text-3xl font-bold tracking-tight text-white sm:text-4xl">
              Jak se stát makerem
            </h2>
          </div>

          <div className="mt-12 space-y-8">
            <OnboardingStep
              step={1}
              title="Zaregistrujte se"
              description="Vytvořte si účet a vyberte roli makera. Stačí email a heslo."
            />
            <OnboardingStep
              step={2}
              title="Vyplňte profil"
              description="Zadejte IČO — data se načtou automaticky z ARES. Přidejte popis, telefon a bankovní účet pro výplaty."
            />
            <OnboardingStep
              step={3}
              title="Přidejte produkty"
              description="Vytvořte nabídku produktů nebo služeb. Nastavte ceny, popisy a kategorie."
            />
            <OnboardingStep
              step={4}
              title="Začněte prodávat"
              description="Váš profil je online. Zákazníci vás najdou v katalogu a můžete přijímat objednávky."
            />
          </div>
        </div>
      </section>

      {/* Provize info */}
      <section className="bg-surface-secondary py-20 sm:py-28">
        <div className="mx-auto max-w-4xl px-4 text-center sm:px-6 lg:px-8">
          <p className="text-sm font-semibold uppercase tracking-widest text-brand-400">Transparentní ceny</p>
          <h2 className="mt-3 text-3xl font-bold tracking-tight text-white sm:text-4xl">
            Provize 15 %
          </h2>
          <p className="mx-auto mt-6 max-w-2xl text-lg text-zinc-400">
            Žádné měsíční poplatky, žádné skryté náklady. Platíte pouze provizi z uskutečněných objednávek.
          </p>

          <div className="mt-12 grid grid-cols-1 gap-6 sm:grid-cols-3">
            <div className="rounded-2xl border border-zinc-800 bg-surface-card p-6 text-center">
              <p className="text-3xl font-bold text-brand-400">0 Kč</p>
              <p className="mt-2 text-sm text-zinc-500">Měsíční poplatek</p>
            </div>
            <div className="rounded-2xl border border-zinc-800 bg-surface-card p-6 text-center">
              <p className="text-3xl font-bold text-brand-400">15 %</p>
              <p className="mt-2 text-sm text-zinc-500">Provize z objednávky</p>
            </div>
            <div className="rounded-2xl border border-zinc-800 bg-surface-card p-6 text-center">
              <p className="text-3xl font-bold text-brand-400">85 %</p>
              <p className="mt-2 text-sm text-zinc-500">Jde přímo vám</p>
            </div>
          </div>
        </div>
      </section>

      {/* Registrační formulář */}
      <section id="registrace" className="relative overflow-hidden py-20 sm:py-28">
        <div className="absolute inset-0 bg-gradient-to-br from-zinc-900 via-zinc-900 to-zinc-950" />
        <div className="absolute -bottom-20 -left-20 h-64 w-64 rounded-full bg-brand-400/5 blur-3xl" />
        <div className="absolute -top-20 -right-20 h-80 w-80 rounded-full bg-brand-400/5 blur-3xl" />

        <div className="relative mx-auto max-w-4xl px-4 sm:px-6 lg:px-8">
          <div className="mb-10 text-center">
            <p className="text-sm font-semibold uppercase tracking-widest text-brand-400">Registrace</p>
            <h2 className="mt-3 text-3xl font-bold tracking-tight text-white sm:text-4xl">
              Připraveni začít?
            </h2>
            <p className="mt-4 text-lg text-zinc-400">
              Registrace trvá méně než 5 minut. Stačí IČO a pár kliknutí.
            </p>
          </div>

          <p className="text-center text-sm text-zinc-500">
            Registrační formulář je v přípravě.
          </p>

          <p className="mt-6 text-center text-sm text-zinc-600">
            Už máte účet?{' '}
            <Link href="/auth/login" className="text-brand-400 transition-colors hover:text-brand-300 hover:underline">
              Přihlaste se
            </Link>
          </p>
        </div>
      </section>
    </div>
  );
}

function BenefitCard({
  icon,
  title,
  description,
}: {
  icon: 'users' | 'creditCard' | 'truck' | 'file';
  title: string;
  description: string;
}) {
  return (
    <div className="hover-glow rounded-2xl border border-zinc-800 bg-surface-card p-6 transition-all duration-300">
      <div className="flex h-12 w-12 items-center justify-center rounded-xl bg-zinc-800 text-brand-400">
        <Icon name={icon} size={24} />
      </div>
      <h3 className="mt-4 text-lg font-semibold text-white">{title}</h3>
      <p className="mt-2 text-sm leading-relaxed text-zinc-500">{description}</p>
    </div>
  );
}

function OnboardingStep({
  step,
  title,
  description,
}: {
  step: number;
  title: string;
  description: string;
}) {
  return (
    <div className="flex items-start gap-5">
      <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-brand-400/10 text-lg font-bold text-brand-400">
        {step}
      </div>
      <div>
        <h3 className="text-lg font-semibold text-white">{title}</h3>
        <p className="mt-1 text-sm text-zinc-500">{description}</p>
      </div>
    </div>
  );
}
