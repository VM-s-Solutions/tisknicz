import { redirect } from 'next/navigation';
import Link from 'next/link';
import { createServerClient } from '@/lib/supabase/server';
import { Icon } from '@/components/ui/icon';
import { Badge } from '@/components/ui/badge';
import { formatCurrency } from '@/lib/utils/pricing';
import { ORDER_STATUSES } from '@/lib/constants';

export const metadata = {
  title: 'Objednávky — Maker Dashboard',
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
    case 'disputed': return 'error';
    default: return 'default';
  }
}

export default async function MakerOrdersPage() {
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

  const { data: orders } = await supabase
    .from('orders')
    .select('id, order_number, title, total_price, status, created_at, customer_name, shipping_method')
    .eq('maker_id', maker.id)
    .order('created_at', { ascending: false })
    .range(0, 49);

  const orderList = orders ?? [];
  const pendingCount = orderList.filter((o) => o.status === 'paid').length;

  return (
    <div className="mx-auto max-w-6xl px-4 py-12 sm:px-6 lg:px-8">
      <div className="flex items-center justify-between">
        <div>
          <Link href="/dashboard/maker" className="inline-flex items-center gap-1 text-sm text-zinc-400 hover:text-zinc-600 transition-colors mb-2">
            <Icon name="arrowLeft" size={14} />
            Dashboard
          </Link>
          <h1 className="text-3xl font-bold tracking-tight text-zinc-900">Objednávky</h1>
          {pendingCount > 0 && (
            <p className="mt-1 text-sm text-amber-600 font-medium">
              {pendingCount} {pendingCount === 1 ? 'objednávka čeká' : pendingCount < 5 ? 'objednávky čekají' : 'objednávek čeká'} na přijetí
            </p>
          )}
        </div>
      </div>

      {orderList.length === 0 ? (
        <div className="mt-10 flex flex-col items-center justify-center rounded-2xl border border-dashed border-zinc-200 bg-white py-16">
          <div className="flex h-16 w-16 items-center justify-center rounded-2xl bg-zinc-100">
            <Icon name="shoppingBag" size={28} className="text-zinc-400" />
          </div>
          <h2 className="mt-4 font-semibold text-zinc-900">Zatím žádné objednávky</h2>
          <p className="mt-2 text-sm text-zinc-500">Objednávky se objeví, jakmile si zákazník objedná váš produkt.</p>
        </div>
      ) : (
        <div className="mt-8 overflow-hidden rounded-2xl border border-zinc-100 bg-white shadow-md">
          {/* Desktop table */}
          <div className="hidden sm:block">
            <table className="w-full text-left">
              <thead>
                <tr className="border-b border-zinc-100 bg-zinc-50">
                  <th className="px-6 py-3 text-xs font-semibold uppercase tracking-wider text-zinc-500">Číslo</th>
                  <th className="px-6 py-3 text-xs font-semibold uppercase tracking-wider text-zinc-500">Produkt</th>
                  <th className="px-6 py-3 text-xs font-semibold uppercase tracking-wider text-zinc-500">Zákazník</th>
                  <th className="px-6 py-3 text-xs font-semibold uppercase tracking-wider text-zinc-500">Cena</th>
                  <th className="px-6 py-3 text-xs font-semibold uppercase tracking-wider text-zinc-500">Stav</th>
                  <th className="px-6 py-3 text-xs font-semibold uppercase tracking-wider text-zinc-500">Datum</th>
                  <th className="px-6 py-3" />
                </tr>
              </thead>
              <tbody className="divide-y divide-zinc-50">
                {orderList.map((order) => (
                  <tr key={order.id} className="transition-colors hover:bg-zinc-50">
                    <td className="px-6 py-4 text-sm font-mono text-zinc-600">{order.order_number}</td>
                    <td className="px-6 py-4 text-sm font-medium text-zinc-900 max-w-[200px] truncate">{order.title}</td>
                    <td className="px-6 py-4 text-sm text-zinc-600">{order.customer_name}</td>
                    <td className="px-6 py-4 text-sm font-semibold text-zinc-900">{formatCurrency(order.total_price)}</td>
                    <td className="px-6 py-4">
                      <Badge variant={statusVariant(order.status)}>
                        {ORDER_STATUSES[order.status as OrderStatus] ?? order.status}
                      </Badge>
                    </td>
                    <td className="px-6 py-4 text-sm text-zinc-500">
                      {new Date(order.created_at).toLocaleDateString('cs-CZ')}
                    </td>
                    <td className="px-6 py-4 text-right">
                      <Link
                        href={`/dashboard/maker/objednavka/${order.id}`}
                        className="inline-flex items-center gap-1 text-sm font-medium text-brand-600 hover:text-brand-700 transition-colors"
                      >
                        Detail
                        <Icon name="arrowRight" size={14} />
                      </Link>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          {/* Mobile cards */}
          <div className="sm:hidden divide-y divide-zinc-100">
            {orderList.map((order) => (
              <Link
                key={order.id}
                href={`/dashboard/maker/objednavka/${order.id}`}
                className="flex items-center gap-4 p-4 transition-colors hover:bg-zinc-50"
              >
                <div className="flex-1 min-w-0">
                  <p className="text-sm font-medium text-zinc-900 truncate">{order.title}</p>
                  <div className="mt-1 flex items-center gap-2 text-xs text-zinc-500">
                    <span className="font-mono">{order.order_number}</span>
                    <span>&middot;</span>
                    <span>{order.customer_name}</span>
                  </div>
                  <div className="mt-2 flex items-center gap-2">
                    <Badge variant={statusVariant(order.status)}>
                      {ORDER_STATUSES[order.status as OrderStatus] ?? order.status}
                    </Badge>
                    <span className="text-sm font-semibold text-zinc-900">{formatCurrency(order.total_price)}</span>
                  </div>
                </div>
                <Icon name="arrowRight" size={16} className="text-zinc-400 shrink-0" />
              </Link>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}
