import { notFound } from 'next/navigation';
import Link from 'next/link';
import { createServerClient } from '@/lib/supabase/server';
import { Badge } from '@/components/ui/badge';
import { Icon } from '@/components/ui/icon';
import { formatCurrency } from '@/lib/utils/pricing';
import { PRICE_TYPES } from '@/lib/constants';
import { DEMO_MAKERS, DEMO_PRODUCTS, DEMO_CATEGORIES } from '@/lib/demo-data';
import type { Metadata } from 'next';

interface MakerPageProps {
  params: Promise<{ slug: string }>;
}

export async function generateMetadata({ params }: MakerPageProps): Promise<Metadata> {
  const { slug } = await params;

  const demoMaker = DEMO_MAKERS.find((m) => m.ico === slug);
  if (demoMaker) {
    return {
      title: demoMaker.company_name,
      description: demoMaker.bio ?? `${demoMaker.company_name} — tiskař v ${demoMaker.city}`,
    };
  }

  try {
    const supabase = await createServerClient();
    const { data: maker } = await supabase
      .from('makers')
      .select('company_name, city, bio')
      .eq('ico', slug)
      .eq('is_active', true)
      .maybeSingle();

    if (!maker) return { title: 'Maker nenalezen' };

    return {
      title: maker.company_name,
      description: maker.bio ?? `${maker.company_name} — tiskař v ${maker.city}`,
    };
  } catch {
    return { title: 'Maker' };
  }
}

export default async function MakerDetailPage({ params }: MakerPageProps) {
  const { slug } = await params;

  let maker: typeof DEMO_MAKERS[0] & {
    street?: string;
    zip?: string;
    website?: string | null;
    accepts_custom_orders?: boolean;
    personal_pickup?: boolean;
    pickup_address?: string | null;
    pickup_note?: string | null;
  } | null = null;
  let products: typeof DEMO_PRODUCTS = [];
  let makerCategoryNames: string[] = [];

  const demoMaker = DEMO_MAKERS.find((m) => m.ico === slug);
  if (demoMaker) {
    maker = { ...demoMaker, accepts_custom_orders: true, personal_pickup: false, website: null, pickup_address: null, pickup_note: null };
    products = DEMO_PRODUCTS.filter((p) => p.maker_id === demoMaker.id);
    const catIds = new Set(products.map((p) => p.category_id));
    makerCategoryNames = DEMO_CATEGORIES.filter((c) => catIds.has(c.id)).map((c) => c.name);
  }

  if (!maker) {
    try {
      const supabase = await createServerClient();
      const { data: dbMaker } = await supabase
        .from('makers')
        .select('id, company_name, city, street, zip, bio, website, rating_avg, rating_count, total_orders, is_verified, accepts_custom_orders, personal_pickup, pickup_address, pickup_note, ico, created_at')
        .eq('ico', slug)
        .eq('is_active', true)
        .maybeSingle();

      if (dbMaker) {
        maker = dbMaker;
        const { data: dbProducts } = await supabase
          .from('products')
          .select('id, title, description, price, price_type, images, category_id, created_at')
          .eq('maker_id', dbMaker.id)
          .eq('is_active', true)
          .order('created_at', { ascending: false });

        if (dbProducts) {
          products = dbProducts.map((p) => ({ ...p, maker_id: dbMaker.id, is_active: true }));
        }

        const { data: makerCategories } = await supabase
          .from('maker_categories')
          .select('category_id, categories(name)')
          .eq('maker_id', dbMaker.id);

        if (makerCategories) {
          makerCategoryNames = makerCategories.map((mc) => {
            const cat = mc.categories as unknown as { name: string };
            return cat?.name ?? '';
          }).filter(Boolean);
        }
      }
    } catch {
      // Supabase not connected
    }
  }

  if (!maker) {
    notFound();
  }

  return (
    <div className="mx-auto max-w-7xl px-4 py-12 sm:px-6 lg:px-8">
      {/* Maker header */}
      <div className="flex flex-col gap-6 sm:flex-row sm:items-start">
        <div className="flex h-20 w-20 shrink-0 items-center justify-center rounded-2xl bg-gradient-to-br from-brand-600 to-brand-500 text-3xl font-bold text-white shadow-lg">
          {maker.company_name.charAt(0)}
        </div>
        <div className="flex-1">
          <div className="flex items-center gap-3">
            <h1 className="text-3xl font-bold tracking-tight text-zinc-900">{maker.company_name}</h1>
            {maker.is_verified && (
              <Icon name="verified" size={22} className="text-brand-500" />
            )}
          </div>
          <div className="mt-1 flex items-center gap-1.5 text-zinc-500">
            <Icon name="mapPin" size={16} />
            <span>{maker.city}</span>
          </div>
          {maker.bio && <p className="mt-4 leading-relaxed text-zinc-600">{maker.bio}</p>}

          {makerCategoryNames.length > 0 && (
            <div className="mt-4 flex flex-wrap gap-2">
              {makerCategoryNames.map((name) => (
                <Badge key={name} variant="brand">{name}</Badge>
              ))}
            </div>
          )}

          <div className="mt-5 flex flex-wrap items-center gap-5 text-sm text-zinc-500">
            {maker.rating_count > 0 && (
              <div className="flex items-center gap-1.5">
                <div className="flex items-center gap-0.5">
                  {Array.from({ length: 5 }, (_, i) => (
                    <Icon
                      key={i}
                      name="star"
                      size={14}
                      className={i < Math.round(maker!.rating_avg) ? 'text-accent-400' : 'text-zinc-200'}
                    />
                  ))}
                </div>
                <span className="font-medium">{maker.rating_avg}</span>
                <span>({maker.rating_count})</span>
              </div>
            )}
            <div className="flex items-center gap-1.5">
              <Icon name="package" size={14} />
              <span>{maker.total_orders} objednávek</span>
            </div>
            {maker.accepts_custom_orders && (
              <div className="flex items-center gap-1.5">
                <Icon name="check" size={14} className="text-brand-500" />
                <span>Zakázky na míru</span>
              </div>
            )}
          </div>

          {maker.personal_pickup && maker.pickup_address && (
            <p className="mt-3 text-sm text-zinc-500">
              <span className="font-medium text-zinc-700">Osobní odběr:</span> {maker.pickup_address}
              {maker.pickup_note && ` (${maker.pickup_note})`}
            </p>
          )}

          {maker.website && (
            <a
              href={maker.website}
              target="_blank"
              rel="noopener noreferrer"
              className="mt-3 inline-flex items-center gap-1.5 text-sm font-medium text-brand-600 transition-colors hover:text-brand-700"
            >
              {maker.website}
              <Icon name="arrowRight" size={14} />
            </a>
          )}
        </div>
      </div>

      {/* Produkty */}
      <div className="mt-16">
        <h2 className="text-2xl font-bold tracking-tight text-zinc-900">Produkty a služby</h2>
        {products.length === 0 ? (
          <div className="mt-8 flex flex-col items-center justify-center rounded-2xl border border-dashed border-zinc-200 py-16">
            <div className="flex h-16 w-16 items-center justify-center rounded-2xl bg-zinc-100">
              <Icon name="package" size={28} className="text-zinc-400" />
            </div>
            <p className="mt-4 text-sm text-zinc-500">Tento maker zatím nemá žádné produkty.</p>
          </div>
        ) : (
          <div className="mt-8 grid grid-cols-1 gap-6 sm:grid-cols-2 lg:grid-cols-3">
            {products.map((product) => (
              <Link key={product.id} href={`/produkt/${product.id}`} className="group overflow-hidden rounded-2xl border border-zinc-100 bg-white shadow-md transition-all duration-300 hover:-translate-y-1 hover:shadow-xl hover:border-brand-200">
                <div className="flex aspect-video items-center justify-center bg-gradient-to-br from-brand-50 to-zinc-50 text-zinc-300">
                  <Icon name="image" size={32} className="text-brand-200" />
                </div>
                <div className="p-5">
                  <h3 className="font-semibold text-zinc-900 group-hover:text-brand-600 transition-colors">{product.title}</h3>
                  {product.description && (
                    <p className="mt-1.5 line-clamp-2 text-sm leading-relaxed text-zinc-500">{product.description}</p>
                  )}
                  <p className="mt-3 text-lg font-bold text-brand-600">
                    {product.price_type === 'on_request'
                      ? PRICE_TYPES.on_request
                      : `${product.price_type === 'from' ? 'od ' : ''}${formatCurrency(product.price)}`}
                  </p>
                </div>
              </Link>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}
