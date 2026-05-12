import { notFound } from 'next/navigation';
import Link from 'next/link';
import { createServerClient } from '@/lib/supabase/server';
import { Icon } from '@/components/ui/icon';
import { Badge } from '@/components/ui/badge';
import { formatCurrency } from '@/lib/utils/pricing';
import { PRICE_TYPES } from '@/lib/constants';
import { DEMO_PRODUCTS, DEMO_MAKERS, DEMO_CATEGORIES } from '@/lib/demo-data';
import type { Metadata } from 'next';

interface ProductPageProps {
  params: Promise<{ id: string }>;
}

export async function generateMetadata({ params }: ProductPageProps): Promise<Metadata> {
  const { id } = await params;
  const demo = DEMO_PRODUCTS.find((p) => p.id === id);
  if (demo) return { title: demo.title };

  try {
    const supabase = await createServerClient();
    const { data } = await supabase.from('products').select('title').eq('id', id).maybeSingle();
    return { title: data?.title ?? 'Produkt' };
  } catch {
    return { title: 'Produkt' };
  }
}

export default async function ProductDetailPage({ params }: ProductPageProps) {
  const { id } = await params;

  let product: {
    id: string; title: string; description: string | null; price: number;
    price_type: string; images: string[]; maker_id: string; category_id: string | null;
  } | null = null;
  let maker: { id: string; company_name: string; city: string; ico: string; rating_avg: number; rating_count: number; is_verified: boolean } | null = null;
  let categoryName = '';

  // Try demo data first
  const demoProduct = DEMO_PRODUCTS.find((p) => p.id === id);
  if (demoProduct) {
    product = { ...demoProduct, description: demoProduct.description ?? null, images: demoProduct.images ?? [] };
    const demoMaker = DEMO_MAKERS.find((m) => m.id === demoProduct.maker_id);
    if (demoMaker) maker = demoMaker;
    const demoCat = DEMO_CATEGORIES.find((c) => c.id === demoProduct.category_id);
    if (demoCat) categoryName = demoCat.name;
  }

  if (!product) {
    try {
      const supabase = await createServerClient();
      const { data: dbProduct } = await supabase
        .from('products')
        .select('id, title, description, price, price_type, images, maker_id, category_id')
        .eq('id', id)
        .eq('is_active', true)
        .maybeSingle();

      if (dbProduct) {
        product = dbProduct;
        const { data: dbMaker } = await supabase
          .from('makers')
          .select('id, company_name, city, ico, rating_avg, rating_count, is_verified')
          .eq('id', dbProduct.maker_id)
          .maybeSingle();
        if (dbMaker) maker = dbMaker;

        if (dbProduct.category_id) {
          const { data: cat } = await supabase
            .from('categories')
            .select('name')
            .eq('id', dbProduct.category_id)
            .maybeSingle();
          if (cat) categoryName = cat.name;
        }
      }
    } catch {
      // Supabase not connected
    }
  }

  if (!product || !maker) notFound();

  const priceDisplay = product.price_type === 'on_request'
    ? PRICE_TYPES.on_request
    : `${product.price_type === 'from' ? 'od ' : ''}${formatCurrency(product.price)}`;

  return (
    <div className="mx-auto max-w-7xl px-4 py-12 sm:px-6 lg:px-8">
      {/* Breadcrumb */}
      <nav className="flex items-center gap-2 text-sm text-zinc-400 mb-8">
        <Link href="/katalog" className="hover:text-zinc-600 transition-colors">Katalog</Link>
        <Icon name="arrowRight" size={12} />
        <Link href={`/katalog/${maker.ico}`} className="hover:text-zinc-600 transition-colors">{maker.company_name}</Link>
        <Icon name="arrowRight" size={12} />
        <span className="text-zinc-700">{product.title}</span>
      </nav>

      <div className="grid grid-cols-1 gap-12 lg:grid-cols-2">
        {/* Left — image */}
        <div className="flex aspect-square items-center justify-center rounded-2xl bg-gradient-to-br from-brand-50 to-zinc-50 border border-zinc-100">
          {product.images.length > 0 ? (
            <img src={product.images[0]} alt={product.title} className="h-full w-full rounded-2xl object-cover" />
          ) : (
            <div className="text-center">
              <Icon name="image" size={64} className="text-brand-200 mx-auto" />
              <p className="mt-4 text-sm text-zinc-400">Fotka produktu</p>
            </div>
          )}
        </div>

        {/* Right — info */}
        <div>
          {categoryName && <Badge variant="brand" className="mb-4">{categoryName}</Badge>}

          <h1 className="text-3xl font-bold tracking-tight text-zinc-900">{product.title}</h1>

          {product.description && (
            <p className="mt-4 text-lg leading-relaxed text-zinc-600">{product.description}</p>
          )}

          <div className="mt-6 rounded-2xl bg-zinc-50 border border-zinc-100 p-6">
            <p className="text-3xl font-bold text-brand-600">{priceDisplay}</p>
            {product.price_type === 'from' && (
              <p className="mt-1 text-sm text-zinc-500">Finální cena závisí na specifikaci</p>
            )}
          </div>

          {/* Maker info */}
          <Link href={`/katalog/${maker.ico}`} className="mt-6 flex items-center gap-4 rounded-2xl border border-zinc-100 bg-white p-4 shadow-sm transition-all hover:shadow-md hover:border-brand-200">
            <div className="flex h-12 w-12 shrink-0 items-center justify-center rounded-xl bg-gradient-to-br from-brand-600 to-brand-500 text-lg font-bold text-white">
              {maker.company_name.charAt(0)}
            </div>
            <div className="flex-1">
              <div className="flex items-center gap-2">
                <span className="font-semibold text-zinc-900">{maker.company_name}</span>
                {maker.is_verified && <Icon name="verified" size={16} className="text-brand-500" />}
              </div>
              <div className="flex items-center gap-3 text-sm text-zinc-500">
                <span>{maker.city}</span>
                {maker.rating_count > 0 && (
                  <span className="flex items-center gap-1">
                    <Icon name="star" size={12} className="text-accent-400" />
                    {maker.rating_avg} ({maker.rating_count})
                  </span>
                )}
              </div>
            </div>
            <Icon name="arrowRight" size={18} className="text-zinc-400" />
          </Link>

          {/* CTA */}
          {product.price_type !== 'on_request' ? (
            <Link
              href={`/objednavka?produkt=${product.id}&maker=${maker.id}`}
              className="mt-6 flex w-full items-center justify-center gap-2 rounded-xl bg-gradient-to-r from-brand-600 to-brand-500 px-8 py-4 text-base font-semibold text-white shadow-lg transition-all duration-200 hover:shadow-xl hover:scale-[1.02] glow-teal-sm"
            >
              Objednat
              <Icon name="arrowRight" size={18} />
            </Link>
          ) : (
            <Link
              href={`/objednavka?produkt=${product.id}&maker=${maker.id}&custom=1`}
              className="mt-6 flex w-full items-center justify-center gap-2 rounded-xl border border-brand-300 bg-brand-50 px-8 py-4 text-base font-semibold text-brand-700 transition-all duration-200 hover:bg-brand-100"
            >
              Poptat cenovou nabídku
              <Icon name="messageCircle" size={18} />
            </Link>
          )}
        </div>
      </div>
    </div>
  );
}
