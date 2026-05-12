import { redirect } from 'next/navigation';
import Link from 'next/link';
import { createServerClient } from '@/lib/supabase/server';
import { Icon } from '@/components/ui/icon';
import { Badge } from '@/components/ui/badge';
import { formatCurrency } from '@/lib/utils/pricing';
import { ORDER_STATUSES } from '@/lib/constants';

export const metadata = {
  title: 'Moje objednávky',
};

type OrderStatus = keyof typeof ORDER_STATUSES;

function statusVariant(status: string): 'brand' | 'success' | 'warning' | 'error' | 'default' {
  switch (status) {
    case 'pending_payment': return 'warning';
    case 'paid': return 'brand';
    case 'accepted': return 'brand';
    case 'shipped': return 'default';
    case 'delivered': return 'success';
    case 'completed': return 'success';
    case 'cancelled': return 'error';
    case 'refunded': return 'error';
    default: return 'default';
  }
}

export default async function CustomerDashboardPage() {
  const supabase = await createServerClient();
  const { data: { user } } = await supabase.auth.getUser();

  if (!user) {
    redirect('/auth/login');
  }

  const { data: profile } = await supabase
    .from('profiles')
    .select('full_name, role')
    .eq('id', user.id)
    .maybeSingle();

  const { data: orders } = await supabase
    .from('orders')
    .select('id, order_number, title, total_price, status, created_at, shipping_method')
    .eq('customer_id', user.id)
    .order('created_at', { ascending: false })
    .range(0, 49);

  const orderList = orders ?? [];

  return (
    <div className="mx-auto max-w-5xl px-4 py-12 sm:px-6 lg:px-8">
      <p className="text-sm font-semibold uppercase tracking-wider text-brand-600">Dashboard</p>
      <h1 className="mt-1 text-3xl font-bold tracking-tight text-zinc-900">
        Vítejte, {profile?.full_name ?? 'zákazníku'}
      </h1>

      <div className="mt-10">
        {orderList.length === 0 ? (
          <div className="flex flex-col items-center justify-center rounded-2xl border border-dashed border-zinc-200 bg-white py-16">
            <div className="flex h-16 w-16 items-center justify-center rounded-2xl bg-zinc-100">
              <Icon name="shoppingBag" size={28} className="text-zinc-400" />
            </div>
            <h2 className="mt-4 font-semibold text-zinc-900">Moje objednávky</h2>
            <p className="mt-2 text-sm text-zinc-500">
              Zatím nemáte žádné objednávky.
            </p>
            <Link
              href="/katalog"
              className="mt-6 inline-flex items-center gap-2 rounded-xl bg-gradient-to-r from-brand-600 to-brand-500 px-6 py-3 text-sm font-semibold text-white shadow-md transition-all duration-200 hover:shadow-lg hover:scale-[1.02]"
            >
              Prohlédnout katalog
              <Icon name="arrowRight" size={16} />
            </Link>
          </div>
        ) : (
          <div className="space-y-3">
            <h2 className="font-semibold text-zinc-900">Moje objednávky</h2>
            {orderList.map((order) => (
              <Link
                key={order.id}
                href={`/dashboard/zakaznik/objednavka/${order.id}`}
                className="flex items-center gap-4 rounded-2xl border border-zinc-100 bg-white p-5 shadow-md transition-all duration-200 hover:shadow-lg hover:border-brand-200 hover:-translate-y-0.5"
              >
                <div className="flex-1 min-w-0">
                  <p className="font-medium text-zinc-900 truncate">{order.title}</p>
                  <div className="mt-1 flex items-center gap-2 text-sm text-zinc-500">
                    <span className="font-mono text-xs">{order.order_number}</span>
                    <span>&middot;</span>
                    <span>{new Date(order.created_at).toLocaleDateString('cs-CZ')}</span>
                  </div>
                </div>
                <div className="flex items-center gap-3 shrink-0">
                  <Badge variant={statusVariant(order.status)}>
                    {ORDER_STATUSES[order.status as OrderStatus] ?? order.status}
                  </Badge>
                  <span className="text-sm font-semibold text-zinc-900">{formatCurrency(order.total_price)}</span>
                  <Icon name="arrowRight" size={16} className="text-zinc-400" />
                </div>
              </Link>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}
