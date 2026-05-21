import { NextResponse, type NextRequest } from 'next/server';
import { createServerClient } from '@/lib/supabase/server';
import { z } from 'zod';

const productSchema = z.object({
  title: z.string().min(1, 'Název je povinný'),
  description: z.string().optional(),
  price: z.number().int().min(0),
  price_type: z.enum(['fixed', 'from', 'on_request']),
  category_id: z.string().uuid().optional(),
  images: z.array(z.string()).optional(),
  weight_grams: z.number().int().positive().optional(),
  is_active: z.boolean().optional(),
});

export async function POST(request: NextRequest) {
  const supabase = await createServerClient();
  const { data: { user } } = await supabase.auth.getUser();

  if (!user) {
    return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });
  }

  const { data: maker } = await supabase
    .from('makers')
    .select('id')
    .eq('user_id', user.id)
    .maybeSingle();

  if (!maker) {
    return NextResponse.json({ error: 'Nejste registrovaný maker' }, { status: 403 });
  }

  const body = await request.json();
  const validated = productSchema.safeParse(body);

  if (!validated.success) {
    return NextResponse.json({ error: validated.error.flatten() }, { status: 400 });
  }

  const { data: product, error } = await supabase
    .from('products')
    .insert({
      maker_id: maker.id,
      ...validated.data,
      category_id: validated.data.category_id ?? null,
    })
    .select('id, title')
    .single();

  if (error) {
    return NextResponse.json({ error: 'Chyba při vytváření produktu' }, { status: 500 });
  }

  return NextResponse.json(product, { status: 201 });
}

export async function GET(request: NextRequest) {
  const supabase = await createServerClient();
  const { searchParams } = new URL(request.url);

  const makerId = searchParams.get('maker_id');
  const categorySlug = searchParams.get('category');
  const page = parseInt(searchParams.get('page') ?? '1', 10);
  const limit = 12;
  const from = (page - 1) * limit;
  const to = from + limit - 1;

  let query = supabase
    .from('products')
    .select(
      'id, title, description, price, price_type, images, is_active, created_at, maker_id, category_id',
      { count: 'exact' }
    )
    .eq('is_active', true)
    .order('created_at', { ascending: false })
    .range(from, to);

  if (makerId) {
    query = query.eq('maker_id', makerId);
  }

  if (categorySlug) {
    const { data: cat } = await supabase
      .from('categories')
      .select('id')
      .eq('slug', categorySlug)
      .maybeSingle();

    if (cat) {
      query = query.eq('category_id', cat.id);
    }
  }

  const { data, count, error } = await query;

  if (error) {
    return NextResponse.json({ error: 'Chyba při načítání produktů' }, { status: 500 });
  }

  return NextResponse.json({ data, count });
}
