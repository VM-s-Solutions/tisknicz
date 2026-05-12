import { redirect } from 'next/navigation';
import Link from 'next/link';
import { createServerClient } from '@/lib/supabase/server';
import { Badge } from '@/components/ui/badge';
import { Icon } from '@/components/ui/icon';
import { formatCurrency } from '@/lib/utils/pricing';

export const metadata = {
  title: 'Dashboard makera',
};

export default async function MakerDashboardPage() {
  const supabase = await createServerClient();
  const { data: { user } } = await supabase.auth.getUser();

  if (!user) {
    redirect('/auth/login');
  }

  const { data: maker } = await supabase
    .from('makers')
    .select('id, company_name, is_verified, rating_avg, rating_count, total_orders, total_revenue')
    .eq('user_id', user.id)
    .maybeSingle();

  if (!maker) {
    redirect('/dashboard/maker/profil');
  }

  const { count: productCount } = await supabase
    .from('products')
    .select('id', { count: 'exact', head: true })
    .eq('maker_id', maker.id);

  return (
    <div className="mx-auto max-w-7xl px-4 py-12 sm:px-6 lg:px-8">
      <div className="flex items-center justify-between">
        <div>
          <p className="text-sm font-semibold uppercase tracking-wider text-brand-600">Dashboard</p>
          <h1 className="mt-1 text-3xl font-bold tracking-tight text-zinc-900">{maker.company_name}</h1>
          <div className="mt-2 flex items-center gap-3">
            {maker.is_verified && <Badge variant="success">Ověřeno</Badge>}
            <span className="text-sm text-zinc-500">
              {maker.rating_avg > 0 ? `${maker.rating_avg} / 5` : 'Zatím bez hodnocení'}
            </span>
          </div>
        </div>
      </div>

      {/* Stats */}
      <div className="mt-10 grid grid-cols-1 gap-5 sm:grid-cols-2 lg:grid-cols-4">
        <StatCard icon="shoppingBag" label="Celkové objednávky" value={String(maker.total_orders)} />
        <StatCard icon="creditCard" label="Celkový příjem" value={formatCurrency(maker.total_revenue)} />
        <StatCard icon="star" label="Hodnocení" value={maker.rating_count > 0 ? String(maker.rating_avg) : '—'} />
        <StatCard icon="package" label="Produkty" value={String(productCount ?? 0)} />
      </div>

      {/* Quick links */}
      <div className="mt-10 grid grid-cols-1 gap-5 sm:grid-cols-3">
        <QuickLink
          href="/dashboard/maker/produkty"
          icon="package"
          title="Moje produkty"
          description="Přidat, upravit nebo smazat produkty"
        />
        <QuickLink
          href="/dashboard/maker/objednavky"
          icon="shoppingBag"
          title="Objednávky"
          description="Přijmout, vyrobit, odeslat"
        />
        <QuickLink
          href="/dashboard/maker/vyplaty"
          icon="creditCard"
          title="Výplaty"
          description="Přehled výplat a faktur"
        />
      </div>
    </div>
  );
}

function StatCard({ icon, label, value }: { icon: 'shoppingBag' | 'creditCard' | 'star' | 'package'; label: string; value: string }) {
  return (
    <div className="rounded-2xl border border-zinc-100 bg-white p-6 shadow-md">
      <div className="flex h-10 w-10 items-center justify-center rounded-xl bg-brand-50 text-brand-600">
        <Icon name={icon} size={20} />
      </div>
      <p className="mt-4 text-sm text-zinc-500">{label}</p>
      <p className="mt-1 text-2xl font-bold tracking-tight text-zinc-900">{value}</p>
    </div>
  );
}

function QuickLink({ href, icon, title, description }: { href: string; icon: 'package' | 'shoppingBag' | 'creditCard'; title: string; description: string }) {
  return (
    <Link href={href} className="group">
      <div className="rounded-2xl border border-zinc-100 bg-white p-6 shadow-md transition-all duration-300 group-hover:-translate-y-1 group-hover:shadow-xl group-hover:border-brand-200">
        <div className="flex h-10 w-10 items-center justify-center rounded-xl bg-brand-50 text-brand-600 transition-colors group-hover:bg-brand-100">
          <Icon name={icon} size={20} />
        </div>
        <h3 className="mt-4 font-semibold text-zinc-900">{title}</h3>
        <p className="mt-1 text-sm text-zinc-500">{description}</p>
      </div>
    </Link>
  );
}
