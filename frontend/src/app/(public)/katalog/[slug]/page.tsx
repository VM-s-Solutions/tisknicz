import Link from 'next/link';
import { notFound } from 'next/navigation';
import type { Metadata } from 'next';
import { Alert } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Card } from '@/components/ui/card';
import { Icon } from '@/components/ui/icon';
import { t } from '@/lib/i18n';
import { getMakerBySlug, type MakerProfile } from '@/lib/api-client-helpers/catalog';
import { PickupNote } from './pickup-note';
import { ProductCard } from './product-card';
import { ReviewsSection } from './reviews-section';
import { Stars } from './stars';

interface PageProps {
  readonly params: Promise<{ slug: string }>;
}

/** Truncate to a sentence-safe slice without breaking mid-word. */
function truncateForMeta(text: string, max = 160): string {
  const collapsed = text.replace(/\s+/g, ' ').trim();
  if (collapsed.length <= max) return collapsed;
  const slice = collapsed.slice(0, max);
  const lastSpace = slice.lastIndexOf(' ');
  return (lastSpace > 80 ? slice.slice(0, lastSpace) : slice).trimEnd() + '…';
}

export async function generateMetadata({ params }: PageProps): Promise<Metadata> {
  const { slug } = await params;
  const result = await getMakerBySlug(slug);
  if (!result.success) {
    // Only branch the title on NotFound — a transient backend error
    // shouldn't tell a search-engine indexer that the maker doesn't
    // exist (T-0047 code-quality review nit #1).
    const title =
      result.error.type === 'NotFound'
        ? `${t('catalog.maker.not_found.title')} — ${t('catalog.maker.metadata.title_suffix')}`
        : t('catalog.maker.metadata.title_suffix');
    return {
      title,
      description: t('catalog.maker.metadata.fallback_description'),
    };
  }
  const profile = result.value;
  const description = profile.bio?.trim()
    ? truncateForMeta(profile.bio, 160)
    : t('catalog.maker.metadata.fallback_description');
  return {
    title: `${profile.companyName} — ${t('catalog.maker.metadata.title_suffix')}`,
    description,
  };
}

export default async function MakerProfilePage({ params }: PageProps) {
  const { slug } = await params;
  const result = await getMakerBySlug(slug);

  if (!result.success) {
    if (result.error.type === 'NotFound') {
      notFound();
    }
    return (
      <main className="mx-auto flex max-w-5xl flex-col gap-6 px-4 py-10 sm:px-6 lg:px-8">
        <Alert variant="error">
          <p className="font-semibold">{t('catalog.maker.error.title')}</p>
          <p className="mt-1">{t('catalog.maker.error.body')}</p>
        </Alert>
      </main>
    );
  }

  const profile = result.value;
  const ratingDisplay = (profile.ratingAverageBp / 1000).toFixed(1);

  return (
    <main className="mx-auto flex max-w-5xl flex-col gap-8 px-4 py-10 sm:px-6 lg:px-8">
      <ProfileHeader profile={profile} ratingDisplay={ratingDisplay} />
      <PickupNote enabled={profile.personalPickupEnabled} note={profile.pickupNote ?? null} />
      <ProductsGrid profile={profile} />
      <ReviewsSection reviews={profile.reviews} />
      <div className="pt-2">
        <Link
          href="/katalog"
          className="inline-flex items-center gap-2 text-sm font-medium text-zinc-400 transition-colors hover:text-white"
        >
          <Icon name="arrowLeft" size={16} />
          {t('catalog.maker.not_found.back_to_catalog')}
        </Link>
      </div>
    </main>
  );
}

function ProfileHeader({
  profile,
  ratingDisplay,
}: {
  readonly profile: MakerProfile;
  readonly ratingDisplay: string;
}) {
  return (
    <Card padding="lg" className="flex flex-col gap-4">
      <div className="flex flex-col gap-3">
        <div className="flex flex-wrap items-center gap-3">
          <h1 className="text-3xl font-bold tracking-tight text-white sm:text-4xl">
            {profile.companyName}
          </h1>
          {profile.isVerified ? (
            <Badge variant="brand">
              <Icon name="verified" size={14} />
              {t('catalog.maker.verified')}
            </Badge>
          ) : null}
          {profile.personalPickupEnabled ? (
            <Badge variant="success">{t('catalog.maker.personal_pickup_badge')}</Badge>
          ) : null}
        </div>

        <div className="flex flex-wrap items-center gap-x-4 gap-y-2 text-sm text-zinc-400">
          <span className="inline-flex items-center gap-1">
            <Icon name="mapPin" size={14} />
            {profile.city}
          </span>
          {profile.legalForm ? <span>{profile.legalForm}</span> : null}
          <span className="inline-flex items-center gap-2">
            <Stars value={profile.ratingAverageBp / 1000} />
            {profile.ratingCount > 0 ? (
              <span>
                {t('catalog.maker.stats.rating', {
                  rating: ratingDisplay,
                  count: profile.ratingCount,
                })}
              </span>
            ) : (
              <span>{t('catalog.maker.stats.rating_none')}</span>
            )}
          </span>
          <span>{t('catalog.maker.stats.orders', { count: profile.totalOrders })}</span>
        </div>
      </div>

      {profile.bio?.trim() ? (
        <p className="whitespace-pre-line text-base text-zinc-300">{profile.bio.trim()}</p>
      ) : null}
    </Card>
  );
}

function ProductsGrid({ profile }: { readonly profile: MakerProfile }) {
  return (
    <section className="flex flex-col gap-4">
      <h2 className="text-xl font-semibold text-white">{t('catalog.maker.products.heading')}</h2>
      {profile.products.length === 0 ? (
        <Card padding="md">
          <p className="text-sm text-zinc-400">{t('catalog.maker.products.empty')}</p>
        </Card>
      ) : (
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
          {profile.products.map((item) => (
            <ProductCard key={item.productId} item={item} />
          ))}
        </div>
      )}
    </section>
  );
}
