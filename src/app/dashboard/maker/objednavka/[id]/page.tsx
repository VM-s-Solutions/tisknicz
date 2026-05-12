import { redirect, notFound } from 'next/navigation';
import Link from 'next/link';
import { createServerClient } from '@/lib/supabase/server';
import { Icon } from '@/components/ui/icon';
import { Badge } from '@/components/ui/badge';
import { formatCurrency } from '@/lib/utils/pricing';
import { ORDER_STATUSES } from '@/lib/constants';
import { OrderActions } from '@/components/dashboard/order-actions';
import { OrderMessages } from '@/components/dashboard/order-messages';

interface MakerOrderPageProps {
  params: Promise<{ id: string }>;
}

export async function generateMetadata({ params }: MakerOrderPageProps) {
  const { id } = await params;
  return { title: `Objednávka ${id.slice(0, 8)}` };
}

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

export default async function MakerOrderDetailPage({ params }: MakerOrderPageProps) {
  const { id } = await params;
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

  const { data: order } = await supabase
    .from('orders')
    .select('id, order_number, title, description, quantity, product_price, shipping_price, platform_fee, maker_payout, total_price, status, shipping_method, zasilkovna_branch_name, customer_name, customer_email, customer_phone, created_at, accepted_at, shipped_at, delivered_at')
    .eq('id', id)
    .eq('maker_id', maker.id)
    .maybeSingle();

  if (!order) {
    notFound();
  }

  const { data: messages } = await supabase
    .from('messages')
    .select('id, sender_id, content, created_at')
    .eq('order_id', id)
    .order('created_at', { ascending: true });

  return (
    <div className="mx-auto max-w-5xl px-4 py-12 sm:px-6 lg:px-8">
      <Link href="/dashboard/maker/objednavky" className="inline-flex items-center gap-1 text-sm text-zinc-500 hover:text-zinc-300 transition-colors mb-6">
        <Icon name="arrowLeft" size={14} />
        Zpět na objednávky
      </Link>

      <div className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <div className="flex items-center gap-3">
            <h1 className="text-2xl font-bold tracking-tight text-white">{order.title}</h1>
            <Badge variant={statusVariant(order.status)}>
              {ORDER_STATUSES[order.status as OrderStatus] ?? order.status}
            </Badge>
          </div>
          <p className="mt-1 text-sm text-zinc-500 font-mono">{order.order_number}</p>
        </div>

        <OrderActions
          orderId={order.id}
          status={order.status}
          role="maker"
        />
      </div>

      <div className="mt-8 grid grid-cols-1 gap-8 lg:grid-cols-3">
        {/* Main info */}
        <div className="lg:col-span-2 space-y-6">
          {/* Order details */}
          <section className="rounded-2xl border border-zinc-800 bg-surface-card p-6">
            <h2 className="font-semibold text-white flex items-center gap-2">
              <Icon name="file" size={18} className="text-brand-400" />
              Detail objednávky
            </h2>
            {order.description && (
              <p className="mt-3 text-sm text-zinc-400 leading-relaxed">{order.description}</p>
            )}
            <dl className="mt-4 grid grid-cols-2 gap-4">
              <div>
                <dt className="text-xs font-medium uppercase tracking-wider text-zinc-600">Množství</dt>
                <dd className="mt-1 text-sm font-semibold text-zinc-200">{order.quantity} ks</dd>
              </div>
              <div>
                <dt className="text-xs font-medium uppercase tracking-wider text-zinc-600">Doručení</dt>
                <dd className="mt-1 text-sm font-semibold text-zinc-200">
                  {order.shipping_method === 'zasilkovna' ? 'Zásilkovna' : 'Osobní odběr'}
                </dd>
              </div>
              {order.zasilkovna_branch_name && (
                <div className="col-span-2">
                  <dt className="text-xs font-medium uppercase tracking-wider text-zinc-600">Výdejní místo</dt>
                  <dd className="mt-1 text-sm text-zinc-200">{order.zasilkovna_branch_name}</dd>
                </div>
              )}
            </dl>
          </section>

          {/* Customer info */}
          <section className="rounded-2xl border border-zinc-800 bg-surface-card p-6">
            <h2 className="font-semibold text-white flex items-center gap-2">
              <Icon name="users" size={18} className="text-brand-400" />
              Zákazník
            </h2>
            <dl className="mt-4 grid grid-cols-1 gap-3 sm:grid-cols-3">
              <div>
                <dt className="text-xs font-medium uppercase tracking-wider text-zinc-600">Jméno</dt>
                <dd className="mt-1 text-sm text-zinc-200">{order.customer_name}</dd>
              </div>
              <div>
                <dt className="text-xs font-medium uppercase tracking-wider text-zinc-600">E-mail</dt>
                <dd className="mt-1 text-sm text-zinc-200">{order.customer_email}</dd>
              </div>
              <div>
                <dt className="text-xs font-medium uppercase tracking-wider text-zinc-600">Telefon</dt>
                <dd className="mt-1 text-sm text-zinc-200">{order.customer_phone}</dd>
              </div>
            </dl>
          </section>

          {/* Timeline */}
          <section className="rounded-2xl border border-zinc-800 bg-surface-card p-6">
            <h2 className="font-semibold text-white flex items-center gap-2">
              <Icon name="clock" size={18} className="text-brand-400" />
              Průběh
            </h2>
            <ol className="mt-4 space-y-3">
              <TimelineItem
                label="Vytvořeno"
                date={order.created_at}
                active
              />
              <TimelineItem
                label="Přijato makerem"
                date={order.accepted_at}
                active={!!order.accepted_at}
              />
              <TimelineItem
                label="Odesláno"
                date={order.shipped_at}
                active={!!order.shipped_at}
              />
              <TimelineItem
                label="Doručeno"
                date={order.delivered_at}
                active={!!order.delivered_at}
              />
            </ol>
          </section>

          {/* Messages */}
          <OrderMessages
            orderId={order.id}
            currentUserId={user.id}
            initialMessages={messages ?? []}
          />
        </div>

        {/* Sidebar — pricing */}
        <div>
          <div className="sticky top-24 rounded-2xl border border-zinc-800 bg-surface-card p-6">
            <h2 className="font-semibold text-white">Finanční přehled</h2>
            <div className="mt-4 space-y-3">
              <div className="flex justify-between text-sm">
                <span className="text-zinc-500">Cena produktu</span>
                <span className="text-zinc-300">{formatCurrency(order.product_price)}</span>
              </div>
              <div className="flex justify-between text-sm">
                <span className="text-zinc-500">Doprava</span>
                <span className="text-zinc-300">{formatCurrency(order.shipping_price)}</span>
              </div>
              <div className="border-t border-zinc-800 pt-3 flex justify-between text-sm">
                <span className="text-zinc-500">Celkem zákazník</span>
                <span className="font-semibold text-zinc-200">{formatCurrency(order.total_price)}</span>
              </div>
              <div className="flex justify-between text-sm">
                <span className="text-zinc-500">Poplatek platformy</span>
                <span className="text-red-400">-{formatCurrency(order.platform_fee)}</span>
              </div>
              <div className="border-t border-zinc-800 pt-3 flex justify-between">
                <span className="font-semibold text-white">Vaše výplata</span>
                <span className="text-lg font-bold text-brand-400">{formatCurrency(order.maker_payout)}</span>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

function TimelineItem({ label, date, active }: { label: string; date: string | null; active: boolean }) {
  return (
    <li className="flex items-center gap-3">
      <div className={`flex h-6 w-6 shrink-0 items-center justify-center rounded-full ${active ? 'bg-brand-400/10' : 'bg-zinc-800'}`}>
        {active ? (
          <Icon name="check" size={12} className="text-brand-400" />
        ) : (
          <div className="h-2 w-2 rounded-full bg-zinc-600" />
        )}
      </div>
      <div className="flex-1">
        <span className={`text-sm ${active ? 'font-medium text-zinc-200' : 'text-zinc-600'}`}>{label}</span>
      </div>
      {date && (
        <span className="text-xs text-zinc-500">
          {new Date(date).toLocaleDateString('cs-CZ')} {new Date(date).toLocaleTimeString('cs-CZ', { hour: '2-digit', minute: '2-digit' })}
        </span>
      )}
    </li>
  );
}
