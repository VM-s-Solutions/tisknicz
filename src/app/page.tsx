import Link from 'next/link';
import { Icon } from '@/components/ui/icon';
import { HeroSceneWrapper } from '@/components/shared/hero-scene-wrapper';

const CATEGORIES = [
  { name: '3D tisk', slug: '3d-tisk', icon: 'printer' as const, description: 'FDM, SLA, resin tisk na zakázku' },
  { name: 'Klasický tisk', slug: 'klasicky-tisk', icon: 'image' as const, description: 'Vizitky, letáky, brožury, plakáty' },
  { name: 'Potisk textilu', slug: 'potisk-textilu', icon: 'tshirt' as const, description: 'DTF, DTG, sítotisk, sublimace' },
  { name: 'Laser & CNC', slug: 'laser-cnc', icon: 'laser' as const, description: 'Gravírování, řezání, frézování' },
  { name: 'Velkoformát', slug: 'velkoformat', icon: 'frame' as const, description: 'Bannery, rollupy, samolepky' },
  { name: 'Handmade', slug: 'handmade', icon: 'palette' as const, description: 'Originální výrobky, dekorace' },
];

export default function HomePage() {
  return (
    <>
      {/* Hero — dark premium */}
      <section className="relative overflow-hidden bg-surface-primary">
        {/* 3D Scene */}
        <HeroSceneWrapper />
        {/* Vignette */}
        <div className="absolute inset-0 z-[1] bg-gradient-to-t from-surface-primary/80 via-transparent to-surface-primary/40 pointer-events-none" />

        <div className="relative z-[2] mx-auto max-w-7xl px-4 py-28 sm:px-6 lg:px-8 lg:py-40">
          <div className="mx-auto max-w-4xl text-center">
            <h1 className="text-6xl font-bold tracking-tight text-white sm:text-7xl lg:text-8xl">
              Where Ideas
              <br />
              <span className="text-gradient">Take Shape.</span>
            </h1>

            <p className="mt-8 text-xl font-light tracking-wide text-zinc-400 sm:text-2xl">
              Tvůj nápad. Náš maker. Hotovo.
            </p>

            <p className="mt-4 text-base text-zinc-500 max-w-xl mx-auto leading-relaxed">
              Marketplace pro makery a tiskaře v ČR. Najdi tvůrce, objednej, nech si doručit.
            </p>

            <div className="mt-12 flex flex-col items-center gap-4 sm:flex-row sm:justify-center">
              <Link
                href="/katalog"
                className="group inline-flex items-center gap-2 rounded-xl border border-brand-400/50 px-8 py-4 text-base font-semibold text-brand-400 transition-all duration-200 hover:bg-brand-400/10 hover:border-brand-400"
              >
                Prohlédnout katalog
                <Icon name="arrowRight" size={18} className="transition-transform group-hover:translate-x-1" />
              </Link>
              <Link
                href="/pro-tiskare"
                className="inline-flex items-center gap-2 rounded-xl border border-zinc-700 px-8 py-4 text-base font-semibold text-zinc-400 transition-all duration-200 hover:border-zinc-500 hover:text-white"
              >
                Začít prodávat
              </Link>
            </div>

            {/* Stats */}
            <div className="mt-20 flex items-center justify-center gap-8 sm:gap-16">
              <Stat value="250+" label="Makerů" />
              <div className="h-8 w-px bg-zinc-800" />
              <Stat value="6" label="Kategorií" />
              <div className="h-8 w-px bg-zinc-800" />
              <Stat value="15%" label="Provize" />
            </div>
          </div>
        </div>
      </section>

      {/* Jak to funguje — dark */}
      <section className="bg-surface-secondary py-28">
        <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8">
          <div className="text-center">
            <p className="text-sm font-semibold uppercase tracking-widest text-brand-400">Jednoduché jako 1-2-3</p>
            <h2 className="mt-3 text-3xl font-bold tracking-tight text-white sm:text-4xl">
              Jak to funguje
            </h2>
          </div>

          <div className="relative mt-16">
            {/* Connecting line */}
            <div className="absolute top-12 left-0 right-0 hidden h-px bg-gradient-to-r from-transparent via-zinc-700 to-transparent lg:block" />

            <div className="grid grid-cols-1 gap-8 md:grid-cols-3">
              <StepCard
                step={1}
                icon="search"
                title="Vyber si makera"
                description="Prohlédni si katalog makerů ve tvém okolí. Filtruj podle kategorie, města nebo hodnocení."
              />
              <StepCard
                step={2}
                icon="creditCard"
                title="Objednej a zaplať"
                description="Vyber produkt nebo napiš vlastní požadavek. Zaplať bezpečně kartou nebo převodem."
              />
              <StepCard
                step={3}
                icon="package"
                title="Vyzvedni si"
                description="Maker vyrobí a odešle přes Zásilkovnu. Sleduj stav objednávky v reálném čase."
              />
            </div>
          </div>
        </div>
      </section>

      {/* Kategorie — dark */}
      <section className="bg-surface-primary py-28">
        <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8">
          <div className="text-center">
            <p className="text-sm font-semibold uppercase tracking-widest text-brand-400">Služby</p>
            <h2 className="mt-3 text-3xl font-bold tracking-tight text-white sm:text-4xl">
              Co tu najdeš
            </h2>
          </div>

          <div className="mt-12 grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-6">
            {CATEGORIES.map((cat) => (
              <Link
                key={cat.slug}
                href={`/katalog?kategorie=${cat.slug}`}
                className="hover-glow group flex flex-col items-center gap-4 rounded-2xl border border-zinc-800 bg-surface-card p-6 text-center transition-all duration-300 hover:-translate-y-1"
              >
                <div className="flex h-12 w-12 items-center justify-center rounded-xl bg-zinc-800 text-brand-400 transition-colors group-hover:bg-brand-400/10">
                  <Icon name={cat.icon} size={24} />
                </div>
                <div>
                  <span className="text-sm font-semibold text-zinc-200">{cat.name}</span>
                  <p className="mt-1 text-xs text-zinc-500 hidden sm:block">{cat.description}</p>
                </div>
              </Link>
            ))}
          </div>
        </div>
      </section>

      {/* CTA pro makery — dark gradient */}
      <section className="relative overflow-hidden bg-surface-secondary py-28">
        <div className="absolute inset-0 bg-gradient-to-br from-zinc-900 via-zinc-900 to-zinc-950" />
        <div className="absolute inset-0 bg-dot-pattern opacity-5" />
        <div className="absolute -top-20 -right-20 h-80 w-80 rounded-full bg-brand-400/5 blur-3xl" />
        <div className="absolute -bottom-20 -left-20 h-64 w-64 rounded-full bg-brand-400/5 blur-3xl" />

        <div className="relative mx-auto max-w-4xl px-4 text-center sm:px-6 lg:px-8">
          <h2 className="text-3xl font-bold tracking-tight text-white sm:text-4xl">
            Jsi maker?
          </h2>
          <p className="mt-4 text-lg text-zinc-400">
            Zaregistruj se a začni prodávat svou tvorbu. Stačí ti IČO a pár minut.
          </p>
          <div className="mt-10 flex flex-col items-center gap-4 sm:flex-row sm:justify-center">
            <Link
              href="/auth/register?role=maker"
              className="group inline-flex items-center gap-2 rounded-xl border border-brand-400 bg-brand-400/10 px-8 py-4 text-base font-semibold text-brand-400 transition-all duration-200 hover:bg-brand-400/20"
            >
              Registrovat se jako maker
              <Icon name="arrowRight" size={18} className="transition-transform group-hover:translate-x-1" />
            </Link>
            <Link
              href="/pro-tiskare"
              className="inline-flex items-center gap-2 rounded-xl border border-zinc-700 px-8 py-4 text-base font-semibold text-zinc-400 transition-all duration-200 hover:border-zinc-500 hover:text-white"
            >
              Zjistit více
            </Link>
          </div>
        </div>
      </section>
    </>
  );
}

function Stat({ value, label }: { value: string; label: string }) {
  return (
    <div className="text-center">
      <p className="text-2xl font-bold text-white sm:text-3xl">{value}</p>
      <p className="mt-1 text-sm text-zinc-500">{label}</p>
    </div>
  );
}

function StepCard({
  step,
  icon,
  title,
  description,
}: {
  step: number;
  icon: 'search' | 'creditCard' | 'package';
  title: string;
  description: string;
}) {
  return (
    <div className="hover-glow relative rounded-2xl border border-zinc-800 bg-surface-card p-8 text-center">
      <div className="mx-auto flex h-14 w-14 items-center justify-center rounded-2xl bg-zinc-800 text-brand-400">
        <Icon name={icon} size={28} />
      </div>
      <div className="absolute -top-3 left-1/2 -translate-x-1/2">
        <span className="text-3xl font-bold text-brand-400">{step}</span>
      </div>
      <h3 className="mt-5 text-lg font-semibold text-white">{title}</h3>
      <p className="mt-2 text-sm leading-relaxed text-zinc-500">{description}</p>
    </div>
  );
}
