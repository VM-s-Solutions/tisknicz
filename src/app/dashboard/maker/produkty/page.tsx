import { redirect } from 'next/navigation';
import { createServerClient } from '@/lib/supabase/server';
import { Badge } from '@/components/ui/badge';
import { Icon } from '@/components/ui/icon';
import { formatCurrency } from '@/lib/utils/pricing';
import { PRICE_TYPES } from '@/lib/constants';
import { ProductActions } from '@/components/dashboard/product-actions';

export const metadata = {
  title: 'Moje produkty',
};

export default async function MakerProductsPage() {
  const supabase = await createServerClient();
  const { data: { user } } = await supabase.auth.getUser();

  if (!user) {
    redirect('/auth/login');
  }

  const { data: maker } = await supabase
    .from('makers')
    .select('id')
    .eq('user_id', user.id)
    .maybeSingle();

  if (!maker) {
    redirect('/dashboard/maker/profil');
  }

  const { data: products } = await supabase
    .from('products')
    .select('id, maker_id, title, description, price, price_type, images, is_active, created_at, category_id, weight_grams')
    .eq('maker_id', maker.id)
    .order('created_at', { ascending: false });

  const { data: categories } = await supabase
    .from('categories')
    .select('id, name, slug, icon, description, sort_order')
    .order('sort_order');

  const categoryMap = new Map((categories ?? []).map((c) => [c.id, c]));

  return (
    <div className="mx-auto max-w-5xl px-4 py-12 sm:px-6 lg:px-8">
      <div className="flex items-center justify-between">
        <div>
          <p className="text-sm font-semibold uppercase tracking-widest text-brand-400">Produkty</p>
          <h1 className="mt-1 text-3xl font-bold tracking-tight text-white">Moje produkty</h1>
        </div>
        <ProductActions categories={categories ?? []} />
      </div>

      {(!products || products.length === 0) ? (
        <div className="mt-10 flex flex-col items-center justify-center rounded-2xl border border-dashed border-zinc-700 py-16">
          <div className="flex h-16 w-16 items-center justify-center rounded-2xl bg-zinc-800">
            <Icon name="package" size={28} className="text-zinc-500" />
          </div>
          <p className="mt-4 text-sm font-medium text-zinc-400">Zatím nemáte žádné produkty.</p>
          <p className="mt-1 text-xs text-zinc-600">Klikněte na &quot;Přidat produkt&quot; a začněte prodávat.</p>
        </div>
      ) : (
        <div className="mt-8 space-y-4">
          {products.map((product) => {
            const category = product.category_id ? categoryMap.get(product.category_id) : null;
            return (
              <div key={product.id} className="hover-glow flex items-center justify-between rounded-2xl border border-zinc-800 bg-surface-card p-5 transition-all duration-200">
                <div>
                  <div className="flex items-center gap-2">
                    <h3 className="font-semibold text-zinc-200">{product.title}</h3>
                    {!product.is_active && <Badge variant="warning">Neaktivní</Badge>}
                  </div>
                  <div className="mt-1 flex items-center gap-3 text-sm text-zinc-500">
                    {category && <span>{category.name}</span>}
                    <span>
                      {product.price_type === 'on_request'
                        ? PRICE_TYPES.on_request
                        : `${product.price_type === 'from' ? 'od ' : ''}${formatCurrency(product.price)}`}
                    </span>
                  </div>
                </div>
                <ProductActions
                  productId={product.id}
                  product={product as import('@/types').Product}
                  categories={categories ?? []}
                  isEdit
                />
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
}
