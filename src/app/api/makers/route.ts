import { NextResponse, type NextRequest } from 'next/server';
import { createServerClient } from '@/lib/supabase/server';
import { z } from 'zod';

const makerSchema = z.object({
  ico: z.string().length(8),
  dic: z.string().nullable().optional(),
  company_name: z.string().min(1),
  legal_form: z.string().nullable().optional(),
  street: z.string().min(1),
  city: z.string().min(1),
  zip: z.string().min(3),
  bio: z.string().max(500).optional(),
  website: z.string().url().optional().or(z.literal('')),
  bank_account: z.string().regex(/^(\d{1,6}-)?\d{2,10}\/\d{4}$/, 'Neplatný formát bankovního účtu'),
  accepts_custom_orders: z.boolean().optional(),
  personal_pickup: z.boolean().optional(),
  pickup_address: z.string().optional(),
  pickup_note: z.string().optional(),
  category_ids: z.array(z.string().uuid()).min(1, 'Vyberte alespoň jednu kategorii'),
  phone: z.string().optional(),
});

export async function POST(request: NextRequest) {
  const supabase = await createServerClient();
  const { data: { user } } = await supabase.auth.getUser();

  if (!user) {
    return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });
  }

  const body = await request.json();
  const validated = makerSchema.safeParse(body);

  if (!validated.success) {
    return NextResponse.json({ error: validated.error.flatten() }, { status: 400 });
  }

  const { category_ids, phone, ...makerData } = validated.data;

  const { data: existingMaker } = await supabase
    .from('makers')
    .select('id')
    .eq('user_id', user.id)
    .maybeSingle();

  if (existingMaker) {
    return NextResponse.json({ error: 'Již máte zaregistrovaný profil makera' }, { status: 409 });
  }

  if (phone) {
    await supabase
      .from('profiles')
      .update({ phone, role: 'maker' })
      .eq('id', user.id);
  } else {
    await supabase
      .from('profiles')
      .update({ role: 'maker' })
      .eq('id', user.id);
  }

  const { data: maker, error: makerError } = await supabase
    .from('makers')
    .insert({
      user_id: user.id,
      ...makerData,
      website: makerData.website || null,
    })
    .select('id')
    .single();

  if (makerError) {
    return NextResponse.json({ error: 'Chyba při vytváření profilu makera' }, { status: 500 });
  }

  if (category_ids.length > 0) {
    const makerCategories = category_ids.map((categoryId) => ({
      maker_id: maker.id,
      category_id: categoryId,
    }));

    await supabase.from('maker_categories').insert(makerCategories);
  }

  return NextResponse.json({ id: maker.id }, { status: 201 });
}

export async function GET(request: NextRequest) {
  const supabase = await createServerClient();
  const { searchParams } = new URL(request.url);

  const category = searchParams.get('category');
  const city = searchParams.get('city');
  const page = parseInt(searchParams.get('page') ?? '1', 10);
  const limit = 12;
  const from = (page - 1) * limit;
  const to = from + limit - 1;

  let query = supabase
    .from('makers')
    .select('id, company_name, city, bio, rating_avg, rating_count, total_orders, is_verified, ico, created_at', { count: 'exact' })
    .eq('is_active', true)
    .order('rating_avg', { ascending: false })
    .range(from, to);

  if (city) {
    query = query.ilike('city', `%${city}%`);
  }

  if (category) {
    const { data: makerIds } = await supabase
      .from('maker_categories')
      .select('maker_id, categories!inner(slug)')
      .eq('categories.slug', category);

    if (makerIds && makerIds.length > 0) {
      const ids = makerIds.map((m) => m.maker_id);
      query = query.in('id', ids);
    } else {
      return NextResponse.json({ data: [], count: 0 });
    }
  }

  const { data, count, error } = await query;

  if (error) {
    return NextResponse.json({ error: 'Chyba při načítání makerů' }, { status: 500 });
  }

  return NextResponse.json({ data, count });
}
