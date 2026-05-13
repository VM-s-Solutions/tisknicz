import { redirect } from 'next/navigation';
import { createServerClient } from '@/lib/supabase/server';
import { Icon } from '@/components/ui/icon';

export const metadata = {
  title: 'Admin Dashboard',
};

export default async function AdminDashboardPage() {
  const supabase = await createServerClient();
  const { data: { user } } = await supabase.auth.getUser();

  if (!user) {
    redirect('/auth/login');
  }

  const { data: profile } = await supabase
    .from('profiles')
    .select('role')
    .eq('id', user.id)
    .maybeSingle();

  if (profile?.role !== 'admin') {
    redirect('/dashboard');
  }

  let makerCount = 0;
  let orderCount = 0;
  let userCount = 0;

  try {
    const { count: mc } = await supabase
      .from('makers')
      .select('id', { count: 'exact', head: true });
    makerCount = mc ?? 0;

    const { count: oc } = await supabase
      .from('orders')
      .select('id', { count: 'exact', head: true });
    orderCount = oc ?? 0;

    const { count: uc } = await supabase
      .from('profiles')
      .select('id', { count: 'exact', head: true });
    userCount = uc ?? 0;
  } catch {
    // Tables might not exist yet
  }

  return (
    <div className="mx-auto max-w-7xl px-4 py-12 sm:px-6 lg:px-8">
      <div>
        <p className="text-sm font-semibold uppercase tracking-widest text-brand-400">Administrace</p>
        <h1 className="mt-1 text-3xl font-bold tracking-tight text-white">Admin Dashboard</h1>
      </div>

      {/* Stats */}
      <div className="mt-10 grid grid-cols-1 gap-5 sm:grid-cols-3">
        <StatCard icon="users" label="Uživatelé" value={String(userCount)} />
        <StatCard icon="package" label="Makeři" value={String(makerCount)} />
        <StatCard icon="shoppingBag" label="Objednávky" value={String(orderCount)} />
      </div>

      {/* Placeholder content */}
      <div className="mt-10 rounded-2xl border border-zinc-800 bg-surface-card p-8">
        <div className="flex items-start gap-4">
          <div className="flex h-12 w-12 shrink-0 items-center justify-center rounded-xl bg-brand-400/10 text-brand-400">
            <Icon name="info" size={24} />
          </div>
          <div>
            <h2 className="text-lg font-semibold text-white">Rozšířená administrace</h2>
            <p className="mt-2 text-sm text-zinc-500">
              Kompletní admin panel s ověřováním makerů, správou objednávek, přehledem tržeb
              a systémovým nastavením bude k dispozici v další fázi vývoje.
            </p>
            <div className="mt-4 space-y-2">
              <Feature text="Ověřování a schvalování makerů" />
              <Feature text="Přehled všech objednávek a tržeb" />
              <Feature text="Správa kategorií a nastavení provize" />
              <Feature text="Monitoring plateb a výplat" />
              <Feature text="Systémové logy a notifikace" />
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

function StatCard({ icon, label, value }: { icon: 'users' | 'package' | 'shoppingBag'; label: string; value: string }) {
  return (
    <div className="rounded-2xl border border-zinc-800 bg-surface-card p-6">
      <div className="flex h-10 w-10 items-center justify-center rounded-xl bg-zinc-800 text-brand-400">
        <Icon name={icon} size={20} />
      </div>
      <p className="mt-4 text-sm text-zinc-500">{label}</p>
      <p className="mt-1 text-2xl font-bold tracking-tight text-white">{value}</p>
    </div>
  );
}

function Feature({ text }: { text: string }) {
  return (
    <div className="flex items-center gap-2 text-sm text-zinc-400">
      <Icon name="check" size={14} className="shrink-0 text-brand-400" />
      {text}
    </div>
  );
}
