import { redirect } from 'next/navigation';
import { createServerClient } from '@/lib/supabase/server';
import { MakerRegistrationForm } from '@/components/forms/maker-registration-form';

export const metadata = {
  title: 'Registrace makera',
};

export default async function MakerProfilePage() {
  const supabase = await createServerClient();
  const { data: { user } } = await supabase.auth.getUser();

  if (!user) {
    redirect('/auth/login');
  }

  const { data: existingMaker } = await supabase
    .from('makers')
    .select('id')
    .eq('user_id', user.id)
    .maybeSingle();

  if (existingMaker) {
    redirect('/dashboard/maker');
  }

  const { data: categories } = await supabase
    .from('categories')
    .select('id, name, slug, icon, description, sort_order')
    .order('sort_order');

  return (
    <div className="mx-auto max-w-3xl px-4 py-12 sm:px-6 lg:px-8">
      <p className="text-sm font-semibold uppercase tracking-widest text-brand-400">Registrace</p>
      <h1 className="mt-2 text-3xl font-bold tracking-tight text-white">Dokončení profilu makera</h1>
      <p className="mt-3 text-zinc-500">
        Zadejte IČO a vyplňte údaje pro váš profil na Makables.
      </p>

      <div className="mt-10">
        <MakerRegistrationForm categories={categories ?? []} />
      </div>
    </div>
  );
}
