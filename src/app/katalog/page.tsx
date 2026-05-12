import { createServerClient } from '@/lib/supabase/server';
import { MakerCard } from '@/components/catalog/maker-card';
import { CategoryFilter } from '@/components/catalog/category-filter';
import { CitySearch } from '@/components/catalog/city-search';
import { Icon } from '@/components/ui/icon';
import { DEMO_CATEGORIES, DEMO_MAKERS } from '@/lib/demo-data';

export const metadata = {
  title: 'Katalog makerů',
};

interface KatalogPageProps {
  searchParams: Promise<{ kategorie?: string; mesto?: string; page?: string }>;
}

export default async function KatalogPage({ searchParams }: KatalogPageProps) {
  const params = await searchParams;

  let categories = DEMO_CATEGORIES;
  let makers = DEMO_MAKERS;
  let usedDemo = true;

  try {
    const supabase = await createServerClient();

    const { data: dbCategories } = await supabase
      .from('categories')
      .select('id, name, slug, icon, description, sort_order')
      .order('sort_order');

    if (dbCategories && dbCategories.length > 0) {
      categories = dbCategories;
      usedDemo = false;

      const page = parseInt(params.page ?? '1', 10);
      const limit = 12;
      const from = (page - 1) * limit;
      const to = from + limit - 1;

      let query = supabase
        .from('makers')
        .select('id, company_name, city, bio, rating_avg, rating_count, total_orders, is_verified, ico, created_at', { count: 'exact' })
        .eq('is_active', true)
        .order('rating_avg', { ascending: false })
        .range(from, to);

      if (params.mesto) {
        query = query.ilike('city', `%${params.mesto}%`);
      }

      if (params.kategorie) {
        const { data: cat } = await supabase
          .from('categories')
          .select('id')
          .eq('slug', params.kategorie)
          .maybeSingle();

        if (cat) {
          const { data: makerIds } = await supabase
            .from('maker_categories')
            .select('maker_id')
            .eq('category_id', cat.id);

          if (makerIds && makerIds.length > 0) {
            query = query.in('id', makerIds.map((m) => m.maker_id));
          } else {
            return (
              <KatalogShell categories={categories} currentCity={params.mesto}>
                <EmptyState message="V této kategorii zatím nejsou žádní makeři." />
              </KatalogShell>
            );
          }
        }
      }

      const { data: dbMakers } = await query;
      if (dbMakers && dbMakers.length > 0) {
        makers = dbMakers;
      } else {
        makers = [];
      }
    }
  } catch {
    // Supabase not connected — use demo data
  }

  if (usedDemo) {
    let filteredMakers = makers;
    if (params.mesto) {
      filteredMakers = filteredMakers.filter((m) =>
        m.city.toLowerCase().includes(params.mesto!.toLowerCase())
      );
    }
    makers = filteredMakers;
  }

  return (
    <KatalogShell categories={categories} currentCity={params.mesto}>
      {makers.length === 0 ? (
        <EmptyState message="Zatím tu nejsou žádní makeři. Buďte první!" />
      ) : (
        <div className="grid grid-cols-1 gap-6 sm:grid-cols-2 lg:grid-cols-3">
          {makers.map((maker) => (
            <MakerCard
              key={maker.id}
              maker={maker}
              slug={maker.ico}
            />
          ))}
        </div>
      )}
    </KatalogShell>
  );
}

function KatalogShell({
  categories,
  currentCity,
  children,
}: {
  categories: { id: string; name: string; slug: string; icon: string | null; description: string | null; sort_order: number }[];
  currentCity?: string;
  children: React.ReactNode;
}) {
  return (
    <div className="mx-auto max-w-7xl px-4 py-12 sm:px-6 lg:px-8">
      <div>
        <p className="text-sm font-semibold uppercase tracking-wider text-brand-600">Katalog</p>
        <h1 className="mt-2 text-4xl font-bold tracking-tight text-zinc-900">Najděte svého makera</h1>
        <p className="mt-3 text-lg text-zinc-500">Prohlédněte si tiskaře a makery ve vašem okolí</p>
      </div>

      <div className="mt-8 flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
        <CategoryFilter categories={categories} />
        <CitySearch currentCity={currentCity} />
      </div>

      <div className="mt-10">{children}</div>
    </div>
  );
}

function EmptyState({ message }: { message: string }) {
  return (
    <div className="flex flex-col items-center justify-center rounded-2xl border border-dashed border-zinc-200 py-16">
      <div className="flex h-16 w-16 items-center justify-center rounded-2xl bg-zinc-100">
        <Icon name="users" size={28} className="text-zinc-400" />
      </div>
      <p className="mt-4 text-sm text-zinc-500">{message}</p>
    </div>
  );
}
