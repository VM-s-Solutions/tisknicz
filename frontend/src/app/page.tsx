import Link from 'next/link';
import type { Metadata } from 'next';
import { Icon, type IconName } from '@/components/ui/icon';
import { HeroSceneWrapper } from '@/components/shared/hero-scene-wrapper';
import { PublicFooter } from '@/components/shared/public-footer';
import { PublicNavbar } from '@/components/shared/public-navbar';
import { getDisplaySession } from '@/lib/auth/display-session';
import { getCachedCatalogCategories } from '@/lib/catalog/category-cache';
import { t } from '@/lib/i18n';
import { canonicalUrl } from '@/lib/seo/site-url';

export function generateMetadata(): Metadata {
  const title = t('home.metadata.title');
  const description = t('home.metadata.description');
  const url = canonicalUrl('/');
  return {
    title,
    description,
    alternates: { canonical: url },
    openGraph: { title, description, url, type: 'website' },
    twitter: { card: 'summary', title, description },
  };
}

/**
 * Icon + blurb per category slug. The NAMES and the list itself are
 * data-driven (T-0171, audit PUB-M5): the tiles used to be fully
 * hardcoded, so an admin renaming or deactivating a category left a
 * landing tile pointing at a slug the catalog silently ignores — the
 * visitor got the FULL unfiltered catalog with nothing checked.
 * Presentation for a slug we have no art for falls back gracefully.
 */
const CATEGORY_PRESENTATION: Record<string, { readonly icon: IconName; readonly description: string }> = {
  '3d-tisk': { icon: 'printer', description: 'FDM, SLA, resin tisk na zakázku' },
  'klasicky-tisk': { icon: 'image', description: 'Vizitky, letáky, brožury, plakáty' },
  'potisk-textilu': { icon: 'tshirt', description: 'DTF, DTG, sítotisk, sublimace' },
  'laser-cnc': { icon: 'laser', description: 'Gravírování, řezání, frézování' },
  'velkoformat': { icon: 'frame', description: 'Bannery, rollupy, samolepky' },
  'handmade': { icon: 'palette', description: 'Originální výrobky, dekorace' },
};

const FALLBACK_PRESENTATION = { icon: 'package' as const, description: '' };

const LEGACY_CATEGORIES = [
  { name: '3D tisk', slug: '3d-tisk', icon: 'printer' as const, description: 'FDM, SLA, resin tisk na zakázku' },
  { name: 'Klasický tisk', slug: 'klasicky-tisk', icon: 'image' as const, description: 'Vizitky, letáky, brožury, plakáty' },
  { name: 'Potisk textilu', slug: 'potisk-textilu', icon: 'tshirt' as const, description: 'DTF, DTG, sítotisk, sublimace' },
  { name: 'Laser & CNC', slug: 'laser-cnc', icon: 'laser' as const, description: 'Gravírování, řezání, frézování' },
  { name: 'Velkoformát', slug: 'velkoformat', icon: 'frame' as const, description: 'Bannery, rollupy, samolepky' },
  { name: 'Handmade', slug: 'handmade', icon: 'palette' as const, description: 'Originální výrobky, dekorace' },
];

export default async function HomePage() {
  const [session, liveCategories] = await Promise.all([
    getDisplaySession(),
    getCachedCatalogCategories(),
  ]);

  // Live taxonomy when the read succeeds; the launch six when it does not
  // (the landing page must never hard-fail on a category read).
  const categories = (liveCategories.length > 0
    ? liveCategories.map((c) => ({ name: c.name, slug: c.slug }))
    : LEGACY_CATEGORIES.map((c) => ({ name: c.name, slug: c.slug }))
  ).map((c) => ({
    ...c,
    ...(CATEGORY_PRESENTATION[c.slug] ?? FALLBACK_PRESENTATION),
  }));
  return (
    <div className="min-h-screen bg-surface-primary">
      <PublicNavbar session={session} />

      <section className="relative overflow-hidden border-b border-zinc-800 bg-surface-primary py-16 sm:py-20 lg:py-24">
        {/* Animated wireframe knot + black hole. Self-gating: the wrapper
            only mounts it on wide viewports, on idle, with motion allowed
            and enough cores — so it never costs mobile or LCP. */}
        <div className="pointer-events-none absolute inset-0 z-0 motion-reduce:hidden" aria-hidden="true">
          <HeroSceneWrapper />
        </div>

        <div className="relative z-10 mx-auto max-w-7xl px-4 sm:px-6 lg:px-8">
          <div className="max-w-4xl">
            <p className="reveal-up text-sm font-semibold text-brand-400">Makables</p>
            <h1 className="reveal-up reveal-delay-1 mt-5 text-4xl font-bold tracking-tight text-zinc-50 sm:text-5xl lg:text-6xl">
              Kde nápady dostávají tvar
            </h1>
            <p className="reveal-up reveal-delay-2 mt-6 max-w-3xl text-lg leading-relaxed text-zinc-300">
              Marketplace pro makery a tiskaře v ČR. Vybereš si tvůrce, odešleš poptávku nebo objednávku a my zajistíme bezpečnou platbu i doručení.
            </p>
            <div className="reveal-up reveal-delay-3 mt-10 flex flex-wrap items-center gap-x-6 gap-y-4">
              <Link
                href="/katalog"
                className="inline-flex items-center gap-2 rounded-lg border border-brand-line px-6 py-2.5 text-sm font-semibold text-brand-ink transition-colors duration-150 hover:border-brand-500 hover:bg-tint-brand hover:text-on-tint-brand"
              >
                Prohlédnout katalog
                <Icon name="arrowRight" size={16} />
              </Link>
              <Link
                href="/jak-to-funguje"
                className="inline-flex items-center gap-2 border-b border-brand-line pb-1 text-sm font-semibold text-brand-ink transition-colors hover:border-brand-300 hover:text-brand-200"
              >
                Jak to funguje
                <Icon name="arrowRight" size={16} />
              </Link>
            </div>
          </div>

          <div className="mt-12 grid grid-cols-1 border-y border-zinc-800 sm:grid-cols-3">
            <Metric value="Nová platforma" label="Buď mezi prvními makery" />
            <Metric value="6" label="Hlavních kategorií" />
            <Metric value="7 % > 3,5 %" label="Provize platformy" />
          </div>
        </div>
      </section>

      <section className="bg-surface-secondary py-16 sm:py-20">
        <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8">
          <div className="max-w-3xl">
            <div>
              <p className="text-sm font-semibold text-brand-400">Jak to funguje</p>
              <h2 className="mt-3 text-3xl font-bold tracking-tight text-zinc-50 sm:text-4xl">Od zadání po doručení</h2>
            </div>
          </div>

          <ol className="mt-10 border-y border-zinc-800">
            <StepLine
              step={1}
              icon="search"
              title="Vybereš makera"
              description="V katalogu si najdeš tvůrce podle kategorie, lokality a hodnocení."
            />
            <StepLine
              step={2}
              icon="creditCard"
              title="Objednáš a zaplatíš"
              description="Zadáš parametry zakázky a zaplatíš bezpečně online kartou nebo převodem."
            />
            <StepLine
              step={3}
              icon="package"
              title="Převezmeš zásilku"
              description="Maker vyrobí objednávku a odešle ji přes Zásilkovnu na tebou zvolené místo."
            />
          </ol>

          <div className="mt-8">
            <Link
              href="/jak-to-funguje"
              className="inline-flex items-center gap-2 border-b border-brand-line pb-1 text-sm font-semibold text-brand-ink transition-colors hover:border-brand-300 hover:text-brand-200"
            >
              Zobrazit celý postup
              <Icon name="arrowRight" size={16} />
            </Link>
          </div>
        </div>
      </section>

      <section className="bg-surface-primary py-16 sm:py-20">
        <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8">
          <p className="text-sm font-semibold text-brand-400">Kategorie</p>
          <h2 className="mt-3 text-3xl font-bold tracking-tight text-zinc-50 sm:text-4xl">Služby na jednom místě</h2>

          <div className="mt-10 grid grid-cols-1 divide-y divide-zinc-800 border-y border-zinc-800 sm:grid-cols-2 sm:divide-y-0">
            {categories.map((cat) => (
              <Link
                key={cat.slug}
                href={`/katalog?category=${cat.slug}`}
                className="group flex items-start gap-4 p-5 text-left transition-colors hover:bg-zinc-900/50"
              >
                <span className="mt-0.5 flex h-8 w-8 shrink-0 items-center justify-center text-brand-400">
                  <Icon name={cat.icon} size={18} />
                </span>
                <span className="min-w-0 flex-1">
                  <span className="block text-sm font-semibold text-zinc-100">{cat.name}</span>
                  <span className="mt-1 block text-xs leading-relaxed text-zinc-400">{cat.description}</span>
                </span>
                <span className="mt-0.5 text-zinc-500 group-hover:text-brand-300">
                  <Icon name="arrowRight" size={16} />
                </span>
              </Link>
            ))}
          </div>
        </div>
      </section>

      <section className="border-t border-zinc-800 bg-surface-secondary py-16 sm:py-20">
        <div className="mx-auto flex max-w-5xl flex-col gap-6 px-4 text-center sm:px-6 lg:px-8">
          <h2 className="text-3xl font-bold tracking-tight text-zinc-50 sm:text-4xl">Pro makery</h2>
          <p className="mx-auto max-w-2xl text-lg leading-relaxed text-zinc-300">
            Máš vlastní výrobu a chceš získávat nové zakázky bez budování vlastního e-shopu? Přidej se na Makables.
          </p>
          <Link
            href="/pro-makery"
            className="mx-auto inline-flex items-center gap-2 border-b border-brand-line pb-1 text-base font-semibold text-brand-ink transition-colors hover:border-brand-300 hover:text-brand-200"
          >
            Více informací pro makery
            <Icon name="arrowRight" size={18} />
          </Link>
        </div>
      </section>

      <PublicFooter />
    </div>
  );
}

function Metric({ value, label }: { value: string; label: string }) {
  return (
    <div className="px-5 py-6 text-left sm:px-6 sm:py-7 [&:not(:first-child)]:sm:border-l [&:not(:first-child)]:sm:border-zinc-800">
      <p className="text-2xl font-bold text-zinc-50 sm:text-3xl">{value}</p>
      <p className="mt-1 text-sm text-zinc-400">{label}</p>
    </div>
  );
}

function StepLine({
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
    <li className="px-4 py-5 first:border-t-0 sm:px-6 sm:py-6 [&:not(:first-child)]:border-t [&:not(:first-child)]:border-zinc-800">
      <div className="flex items-start gap-4">
        <span className="mt-0.5 inline-flex h-7 min-w-7 items-center justify-center rounded-md bg-zinc-800 px-2 text-xs font-bold text-brand-400">
          {step}
        </span>
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-3">
            <span className="flex h-7 w-7 items-center justify-center text-brand-400">
              <Icon name={icon} size={16} />
            </span>
            <h3 className="text-lg font-semibold text-zinc-50">{title}</h3>
          </div>
          <p className="mt-2 text-sm leading-relaxed text-zinc-400">{description}</p>
        </div>
      </div>
    </li>
  );
}
