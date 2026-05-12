import { redirect } from 'next/navigation';
import Link from 'next/link';
import { createServerClient } from '@/lib/supabase/server';
import { Icon } from '@/components/ui/icon';

export const metadata = {
  title: 'Moje objednávky',
};

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

  return (
    <div className="mx-auto max-w-5xl px-4 py-12 sm:px-6 lg:px-8">
      <p className="text-sm font-semibold uppercase tracking-wider text-brand-600">Dashboard</p>
      <h1 className="mt-1 text-3xl font-bold tracking-tight text-zinc-900">
        Vítejte, {profile?.full_name ?? 'zákazníku'}
      </h1>

      <div className="mt-10">
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
      </div>
    </div>
  );
}
