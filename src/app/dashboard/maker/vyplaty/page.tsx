import { redirect } from 'next/navigation';
import Link from 'next/link';
import { createServerClient } from '@/lib/supabase/server';
import { Icon } from '@/components/ui/icon';
import { formatCurrency } from '@/lib/utils/pricing';

export const metadata = {
  title: 'Výplaty',
};

export default async function MakerPayoutsPage() {
  const supabase = await createServerClient();
  const { data: { user } } = await supabase.auth.getUser();

  if (!user) {
    redirect('/auth/login');
  }

  const { data: maker } = await supabase
    .from('makers')
    .select('id, company_name, bank_account, total_revenue')
    .eq('user_id', user.id)
    .maybeSingle();

  if (!maker) {
    redirect('/dashboard/maker/profil');
  }

  const { data: completedOrders } = await supabase
    .from('orders')
    .select('id, order_number, total_price, platform_fee, created_at, status')
    .eq('maker_id', maker.id)
    .in('status', ['completed', 'delivered'])
    .order('created_at', { ascending: false })
    .limit(20);

  const totalEarnings = (completedOrders ?? []).reduce(
    (sum, order) => sum + (order.total_price - order.platform_fee),
    0
  );

  return (
    <div className="mx-auto max-w-5xl px-4 py-12 sm:px-6 lg:px-8">
      <div>
        <p className="text-sm font-semibold uppercase tracking-widest text-brand-400">Finance</p>
        <h1 className="mt-1 text-3xl font-bold tracking-tight text-white">Výplaty</h1>
      </div>

      {/* Stats */}
      <div className="mt-10 grid grid-cols-1 gap-5 sm:grid-cols-3">
        <div className="rounded-2xl border border-zinc-800 bg-surface-card p-6">
          <p className="text-sm text-zinc-500">Celkový příjem</p>
          <p className="mt-1 text-2xl font-bold tracking-tight text-white">{formatCurrency(maker.total_revenue)}</p>
        </div>
        <div className="rounded-2xl border border-zinc-800 bg-surface-card p-6">
          <p className="text-sm text-zinc-500">Vaše čistý výdělek</p>
          <p className="mt-1 text-2xl font-bold tracking-tight text-brand-400">{formatCurrency(totalEarnings)}</p>
        </div>
        <div className="rounded-2xl border border-zinc-800 bg-surface-card p-6">
          <p className="text-sm text-zinc-500">Bankovní účet</p>
          <p className="mt-1 text-lg font-semibold tracking-tight text-white">{maker.bank_account ?? '—'}</p>
        </div>
      </div>

      {/* Info */}
      <div className="mt-8 rounded-2xl border border-zinc-800 bg-surface-card p-6">
        <div className="flex items-start gap-3">
          <Icon name="info" size={20} className="mt-0.5 shrink-0 text-brand-400" />
          <div>
            <p className="text-sm font-medium text-zinc-300">Jak fungují výplaty?</p>
            <p className="mt-1 text-sm text-zinc-500">
              Výplaty probíhají automaticky po dokončení objednávky. Peníze jsou převedeny na váš
              bankovní účet po odečtení 15% provize platformy. Zpracování trvá 1–3 pracovní dny.
            </p>
          </div>
        </div>
      </div>

      {/* Tabulka objednávek */}
      <div className="mt-10">
        <h2 className="text-lg font-semibold text-white">Přehled dokončených objednávek</h2>

        {(!completedOrders || completedOrders.length === 0) ? (
          <div className="mt-6 flex flex-col items-center justify-center rounded-2xl border border-dashed border-zinc-700 py-12">
            <Icon name="creditCard" size={32} className="text-zinc-600" />
            <p className="mt-4 text-sm text-zinc-500">Zatím žádné dokončené objednávky.</p>
            <Link
              href="/dashboard/maker/objednavky"
              className="mt-3 text-sm font-medium text-brand-400 transition-colors hover:text-brand-300"
            >
              Zobrazit objednávky
            </Link>
          </div>
        ) : (
          <div className="mt-6 overflow-hidden rounded-2xl border border-zinc-800">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-zinc-800 bg-surface-card">
                  <th className="px-4 py-3 text-left font-medium text-zinc-400">Objednávka</th>
                  <th className="px-4 py-3 text-left font-medium text-zinc-400">Datum</th>
                  <th className="px-4 py-3 text-right font-medium text-zinc-400">Celkem</th>
                  <th className="px-4 py-3 text-right font-medium text-zinc-400">Provize</th>
                  <th className="px-4 py-3 text-right font-medium text-zinc-400">Výplata</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-zinc-800">
                {completedOrders.map((order) => (
                  <tr key={order.id} className="bg-surface-primary transition-colors hover:bg-surface-card">
                    <td className="px-4 py-3 font-medium text-zinc-200">{order.order_number}</td>
                    <td className="px-4 py-3 text-zinc-500">
                      {new Date(order.created_at).toLocaleDateString('cs-CZ')}
                    </td>
                    <td className="px-4 py-3 text-right text-zinc-300">{formatCurrency(order.total_price)}</td>
                    <td className="px-4 py-3 text-right text-zinc-500">-{formatCurrency(order.platform_fee)}</td>
                    <td className="px-4 py-3 text-right font-medium text-brand-400">
                      {formatCurrency(order.total_price - order.platform_fee)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  );
}
