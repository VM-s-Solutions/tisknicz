import { redirect, notFound } from 'next/navigation';
import Link from 'next/link';
import { createServerClient } from '@/lib/supabase/server';
import { Icon } from '@/components/ui/icon';
import { Badge } from '@/components/ui/badge';
import { formatCurrency } from '@/lib/utils/pricing';
import { ORDER_STATUSES } from '@/lib/constants';
import { OrderActions } from '@/components/dashboard/order-actions';
import { OrderMessages } from '@/components/dashboard/order-messages';

interface CustomerOrderPageProps {
  params: Promise<{ id: string }>;
}

export async function generateMetadata({ params }: CustomerOrderPageProps) {
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

function statusIcon(status: string): 'clock' | 'creditCard' | 'check' | 'truck' | 'checkCircle' | 'xCircle' | 'alertCircle' {
  switch (status) {
    case 'pending_payment': return 'clock';
    case 'paid': return 'creditCard';
    case 'accepted': return 'check';
    case 'shipped': return 'truck';
    case 'delivered': return 'checkCircle';
    case 'completed': return 'checkCircle';
    case 'cancelled': return 'xCircle';
    case 'refunded': return 'xCircle';
    case 'disputed': return 'alertCircle';
    default: return 'clock';
  }
}

export default async function CustomerOrderDetailPage({ params }: CustomerOrderPageProps) {
  const { id } = await params;
  const supabase = await createServerClient();
  const { data: { user } } = await supabase.auth.getUser();

  if (!user) {
    redirect('/auth/login');
  }

  const { data: order } = await supabase
    .from('orders')
    .select('id, order_number, title, description, quantity, product_price, shipping_price, total_price, status, shipping_method, zasilkovna_branch_name, customer_name, created_at, accepted_at, shipped_at, delivered_at, maker_id')
    .eq('id', id)
    .eq('customer_id', user.id)
    .maybeSingle();

  if (!order) {
    notFound();
  }

  const { data: maker } = await supabase
    .from('makers')
    .select('id, company_name, city, is_verified')
    .eq('id', order.maker_id)
    .maybeSingle();

  const { data: messages } = await supabase
    .from('messages')
    .select('id, sender_id, content, created_at')
    .eq('order_id', id)
    .order('created_at', { ascending: true });

  // Status progress steps
  const steps = [
    { key: 'pending_payment', label: 'Platba', icon: statusIcon('pending_payment') },
    { key: 'paid', label: 'Zaplaceno', icon: statusIcon('paid') },
    { key: 'accepted', label: 'Přijato', icon: statusIcon('accepted') },
    { key: 'shipped', label: 'Odesláno', icon: statusIcon('shipped') },
    { key: 'delivered', label: 'Doručeno', icon: statusIcon('delivered') },
  ] as const;

  const statusOrder = ['pending_payment', 'paid', 'accepted', 'shipped', 'delivered', 'completed'];
  const currentIndex = statusOrder.indexOf(order.status);

  return (
    <div className="mx-auto max-w-4xl px-4 py-12 sm:px-6 lg:px-8">
      <Link href="/dashboard/zakaznik" className="inline-flex items-center gap-1 text-sm text-zinc-500 hover:text-zinc-300 transition-colors mb-6">
        <Icon name="arrowLeft" size={14} />
        Moje objednávky
      </Link>

      {/* Header */}
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <h1 className="text-2xl font-bold tracking-tight text-white">{order.title}</h1>
          <div className="mt-2 flex items-center gap-3">
            <span className="text-sm text-zinc-500 font-mono">{order.order_number}</span>
            <Badge variant={statusVariant(order.status)}>
              {ORDER_STATUSES[order.status as OrderStatus] ?? order.status}
            </Badge>
          </div>
        </div>

        <OrderActions
          orderId={order.id}
          status={order.status}
          role="customer"
        />
      </div>

      {/* Progress bar */}
      <div className="mt-8 rounded-2xl border border-zinc-800 bg-surface-card p-6">
        <div className="flex items-center justify-between">
          {steps.map((step, i) => {
            const isActive = statusOrder.indexOf(step.key) <= currentIndex;
            const isCurrent = step.key === order.status;
            return (
              <div key={step.key} className="flex flex-1 items-center">
                <div className="flex flex-col items-center">
                  <div className={`flex h-10 w-10 items-center justify-center rounded-full transition-colors ${
                    isCurrent
                      ? 'bg-brand-400/20 text-brand-400 ring-2 ring-brand-400/50'
                      : isActive
                        ? 'bg-brand-400/10 text-brand-400'
                        : 'bg-zinc-800 text-zinc-600'
                  }`}>
                    <Icon name={step.icon} size={18} />
                  </div>
                  <span className={`mt-2 text-xs font-medium ${
                    isCurrent ? 'text-brand-400' : isActive ? 'text-zinc-400' : 'text-zinc-600'
                  }`}>
                    {step.label}
                  </span>
                </div>
                {i < steps.length - 1 && (
                  <div className={`mx-2 h-0.5 flex-1 rounded-full ${
                    statusOrder.indexOf(step.key) < currentIndex ? 'bg-brand-400/50' : 'bg-zinc-800'
                  }`} />
                )}
              </div>
            );
          })}
        </div>
      </div>

      <div className="mt-8 grid grid-cols-1 gap-8 lg:grid-cols-5">
        {/* Main content */}
        <div className="lg:col-span-3 space-y-6">
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
              <div>
                <dt className="text-xs font-medium uppercase tracking-wider text-zinc-600">Vytvořeno</dt>
                <dd className="mt-1 text-sm text-zinc-200">
                  {new Date(order.created_at).toLocaleDateString('cs-CZ')}
                </dd>
              </div>
            </dl>
          </section>

          {/* Maker card */}
          {maker && (
            <section className="rounded-2xl border border-zinc-800 bg-surface-card p-6">
              <h2 className="font-semibold text-white flex items-center gap-2">
                <Icon name="users" size={18} className="text-brand-400" />
                Maker
              </h2>
              <div className="mt-4 flex items-center gap-4">
                <div className="flex h-12 w-12 shrink-0 items-center justify-center rounded-xl bg-gradient-to-br from-zinc-700 to-zinc-800 text-lg font-bold text-white">
                  {maker.company_name.charAt(0)}
                </div>
                <div>
                  <div className="flex items-center gap-2">
                    <span className="font-semibold text-zinc-200">{maker.company_name}</span>
                    {maker.is_verified && <Icon name="verified" size={16} className="text-brand-400" />}
                  </div>
                  <p className="text-sm text-zinc-500">{maker.city}</p>
                </div>
              </div>
            </section>
          )}

          {/* Messages */}
          <OrderMessages
            orderId={order.id}
            currentUserId={user.id}
            initialMessages={messages ?? []}
          />
        </div>

        {/* Sidebar — pricing */}
        <div className="lg:col-span-2">
          <div className="sticky top-24 rounded-2xl border border-zinc-800 bg-surface-card p-6">
            <h2 className="font-semibold text-white">Platba</h2>
            <div className="mt-4 space-y-3">
              <div className="flex justify-between text-sm">
                <span className="text-zinc-500">Cena produktu</span>
                <span className="text-zinc-300">{formatCurrency(order.product_price)}</span>
              </div>
              <div className="flex justify-between text-sm">
                <span className="text-zinc-500">Doprava</span>
                <span className="text-zinc-300">
                  {order.shipping_price > 0 ? formatCurrency(order.shipping_price) : 'Zdarma'}
                </span>
              </div>
              <div className="border-t border-zinc-800 pt-3 flex justify-between text-base font-semibold">
                <span className="text-white">Celkem</span>
                <span className="text-brand-400">{formatCurrency(order.total_price)}</span>
              </div>
            </div>

            {order.status === 'pending_payment' && (
              <div className="mt-6 rounded-xl bg-amber-950/50 border border-amber-900/50 p-4">
                <div className="flex items-start gap-2">
                  <Icon name="clock" size={16} className="text-amber-400 mt-0.5 shrink-0" />
                  <div>
                    <p className="text-sm font-medium text-amber-300">Čeká na platbu</p>
                    <p className="mt-1 text-xs text-amber-400/70">
                      Po zaplacení maker obdrží objednávku a začne s výrobou.
                    </p>
                  </div>
                </div>
              </div>
            )}

            {order.status === 'shipped' && (
              <div className="mt-6 rounded-xl bg-brand-400/5 border border-brand-400/20 p-4">
                <div className="flex items-start gap-2">
                  <Icon name="truck" size={16} className="text-brand-400 mt-0.5 shrink-0" />
                  <div>
                    <p className="text-sm font-medium text-brand-300">Zásilka na cestě</p>
                    <p className="mt-1 text-xs text-brand-400/70">
                      Po obdržení zásilky potvrďte doručení tlačítkem nahoře.
                    </p>
                  </div>
                </div>
              </div>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}
