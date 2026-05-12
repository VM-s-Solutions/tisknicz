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
      {/* Hero — dark */}
      <section className="relative overflow-hidden bg-zinc-950">
        {/* 3D Scene */}
        <HeroSceneWrapper />
        {/* Subtle vignette for bottom text contrast */}
        <div className="absolute inset-0 z-[1] bg-gradient-to-t from-zinc-950/70 via-transparent to-zinc-950/30 pointer-events-none" />

        <div className="relative z-[2] mx-auto max-w-7xl px-4 py-24 sm:px-6 lg:px-8 lg:py-36">
          <div className="mx-auto max-w-4xl text-center">
            <div className="inline-flex items-center gap-2 rounded-full border border-zinc-800 bg-zinc-900 px-4 py-1.5 text-sm text-zinc-400 mb-8">
              <span className="h-2 w-2 rounded-full bg-brand-400 animate-pulse" />
              Marketplace pro tiskaře v ČR
            </div>

            <h1 className="text-5xl font-extrabold tracking-tight text-white sm:text-6xl lg:text-7xl">
              Najdi svého{' '}
              <span className="text-gradient-light">tiskaře</span>
            </h1>

            <p className="mt-6 text-lg leading-relaxed text-zinc-400 sm:text-xl">
              3D tisk, potisk textilu, gravírování a další.
              <br className="hidden sm:block" />
              Objednej od lokálních makerů, jednoduše online.
            </p>

            <div className="mt-10 flex flex-col items-center gap-4 sm:flex-row sm:justify-center">
              <Link
                href="/katalog"
                className="group inline-flex items-center gap-2 rounded-xl bg-gradient-to-r from-brand-600 to-brand-500 px-8 py-4 text-base font-semibold text-white shadow-lg transition-all duration-200 hover:shadow-xl hover:scale-[1.02] glow-teal-sm"
              >
                Prohlédnout katalog
                <Icon name="arrowRight" size={18} className="transition-transform group-hover:translate-x-1" />
              </Link>
              <Link
                href="/pro-tiskare"
                className="inline-flex items-center gap-2 rounded-xl border border-zinc-700 bg-zinc-900 px-8 py-4 text-base font-semibold text-zinc-300 transition-all duration-200 hover:border-zinc-600 hover:text-white"
              >
                Chci prodávat
              </Link>
            </div>

            {/* Stats */}
            <div className="mt-16 flex items-center justify-center gap-8 sm:gap-16">
              <Stat value="250+" label="Makerů" />
              <div className="h-8 w-px bg-zinc-800" />
              <Stat value="6" label="Kategorií" />
              <div className="h-8 w-px bg-zinc-800" />
              <Stat value="15%" label="Provize" />
            </div>
          </div>
        </div>
      </section>

      {/* Jak to funguje */}
      <section className="bg-white py-24">
        <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8">
          <div className="text-center">
            <p className="text-sm font-semibold uppercase tracking-wider text-brand-600">Jednoduché jako 1-2-3</p>
            <h2 className="mt-2 text-3xl font-bold text-zinc-900 sm:text-4xl">
              Jak to funguje
            </h2>
          </div>

          <div className="relative mt-16">
            {/* Connecting line */}
            <div className="absolute top-12 left-0 right-0 hidden h-0.5 bg-gradient-to-r from-transparent via-brand-200 to-transparent lg:block" />

            <div className="grid grid-cols-1 gap-8 md:grid-cols-3">
              <StepCard
                step={1}
                icon="search"
                title="Vyber si tiskaře"
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

      {/* Kategorie */}
      <section className="bg-zinc-50 py-24">
        <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8">
          <div className="text-center">
            <p className="text-sm font-semibold uppercase tracking-wider text-brand-600">Služby</p>
            <h2 className="mt-2 text-3xl font-bold text-zinc-900 sm:text-4xl">
              Co tu najdeš
            </h2>
          </div>

          <div className="mt-12 grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-6">
            {CATEGORIES.map((cat) => (
              <Link
                key={cat.slug}
                href={`/katalog?kategorie=${cat.slug}`}
                className="group flex flex-col items-center gap-4 rounded-2xl border border-zinc-200 bg-white p-6 text-center shadow-sm transition-all duration-300 hover:-translate-y-1 hover:shadow-lg hover:border-brand-200"
              >
                <div className="flex h-12 w-12 items-center justify-center rounded-xl bg-brand-50 text-brand-600 transition-colors group-hover:bg-brand-100">
                  <Icon name={cat.icon} size={24} />
                </div>
                <div>
                  <span className="text-sm font-semibold text-zinc-900">{cat.name}</span>
                  <p className="mt-1 text-xs text-zinc-500 hidden sm:block">{cat.description}</p>
                </div>
              </Link>
            ))}
          </div>
        </div>
      </section>

      {/* CTA pro makery */}
      <section className="relative overflow-hidden bg-gradient-to-br from-brand-700 via-brand-600 to-brand-500 py-24">
        <div className="absolute inset-0 bg-dot-pattern opacity-10" />
        <div className="absolute -top-20 -right-20 h-80 w-80 rounded-full bg-brand-400/20 blur-3xl" />

        <div className="relative mx-auto max-w-4xl px-4 text-center sm:px-6 lg:px-8">
          <h2 className="text-3xl font-bold text-white sm:text-4xl">
            Jsi tiskař nebo maker?
          </h2>
          <p className="mt-4 text-lg text-brand-100">
            Zaregistruj se a začni prodávat svou tvorbu. Stačí ti IČO a pár minut.
          </p>
          <div className="mt-8 flex flex-col items-center gap-4 sm:flex-row sm:justify-center">
            <Link
              href="/auth/register?role=maker"
              className="group inline-flex items-center gap-2 rounded-xl bg-white px-8 py-4 text-base font-semibold text-brand-700 shadow-lg transition-all duration-200 hover:shadow-xl hover:scale-[1.02]"
            >
              Registrovat se jako tiskař
              <Icon name="arrowRight" size={18} className="transition-transform group-hover:translate-x-1" />
            </Link>
            <Link
              href="/pro-tiskare"
              className="inline-flex items-center gap-2 rounded-xl border border-white/30 px-8 py-4 text-base font-semibold text-white transition-all duration-200 hover:bg-white/10"
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
    <div className="relative rounded-2xl border border-zinc-100 bg-white p-8 shadow-md text-center">
      <div className="mx-auto flex h-14 w-14 items-center justify-center rounded-2xl bg-brand-50 text-brand-600">
        <Icon name={icon} size={28} />
      </div>
      <div className="absolute -top-3 left-1/2 -translate-x-1/2 flex h-7 w-7 items-center justify-center rounded-full bg-brand-600 text-xs font-bold text-white shadow-md">
        {step}
      </div>
      <h3 className="mt-5 text-lg font-semibold text-zinc-900">{title}</h3>
      <p className="mt-2 text-sm leading-relaxed text-zinc-500">{description}</p>
    </div>
  );
}
