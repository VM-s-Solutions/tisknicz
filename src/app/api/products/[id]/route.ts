import { NextResponse, type NextRequest } from 'next/server';
import { createServerClient } from '@/lib/supabase/server';
import { z } from 'zod';

const updateProductSchema = z.object({
  title: z.string().min(1).optional(),
  description: z.string().optional(),
  price: z.number().int().min(0).optional(),
  price_type: z.enum(['fixed', 'from', 'on_request']).optional(),
  category_id: z.string().uuid().nullable().optional(),
  images: z.array(z.string()).optional(),
  weight_grams: z.number().int().positive().nullable().optional(),
  is_active: z.boolean().optional(),
});

export async function GET(
  _request: NextRequest,
  { params }: { params: Promise<{ id: string }> }
) {
  const { id } = await params;
  const supabase = await createServerClient();

  const { data: product, error } = await supabase
    .from('products')
    .select('id, title, description, price, price_type, images, weight_grams, is_active, created_at, maker_id, category_id')
    .eq('id', id)
    .maybeSingle();

  if (error || !product) {
    return NextResponse.json({ error: 'Produkt nenalezen' }, { status: 404 });
  }

  return NextResponse.json(product);
}

export async function PATCH(
  request: NextRequest,
  { params }: { params: Promise<{ id: string }> }
) {
  const { id } = await params;
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

  const { data: existingProduct } = await supabase
    .from('products')
    .select('id, maker_id')
    .eq('id', id)
    .maybeSingle();

  if (!existingProduct || existingProduct.maker_id !== maker.id) {
    return NextResponse.json({ error: 'Produkt nenalezen nebo nemáte oprávnění' }, { status: 404 });
  }

  const body = await request.json();
  const validated = updateProductSchema.safeParse(body);

  if (!validated.success) {
    return NextResponse.json({ error: validated.error.flatten() }, { status: 400 });
  }

  const { data: product, error } = await supabase
    .from('products')
    .update({ ...validated.data, updated_at: new Date().toISOString() })
    .eq('id', id)
    .select('id, title')
    .single();

  if (error) {
    return NextResponse.json({ error: 'Chyba při aktualizaci produktu' }, { status: 500 });
  }

  return NextResponse.json(product);
}

export async function DELETE(
  _request: NextRequest,
  { params }: { params: Promise<{ id: string }> }
) {
  const { id } = await params;
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

  const { data: product } = await supabase
    .from('products')
    .select('id, maker_id')
    .eq('id', id)
    .maybeSingle();

  if (!product || product.maker_id !== maker.id) {
    return NextResponse.json({ error: 'Produkt nenalezen nebo nemáte oprávnění' }, { status: 404 });
  }

  const { error } = await supabase
    .from('products')
    .delete()
    .eq('id', id);

  if (error) {
    return NextResponse.json({ error: 'Chyba při mazání produktu' }, { status: 500 });
  }

  return NextResponse.json({ success: true });
}
