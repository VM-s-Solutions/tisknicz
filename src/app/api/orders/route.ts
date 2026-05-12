import { NextRequest, NextResponse } from 'next/server';
import { createServerClient } from '@/lib/supabase/server';
import { calculateOrderPricing } from '@/lib/utils/pricing';
import { generateOrderNumber } from '@/lib/utils/order-number';
import { z } from 'zod';

const createOrderSchema = z.object({
  product_id: z.string().uuid().optional(),
  maker_id: z.string().uuid(),
  title: z.string().min(1).max(200),
  description: z.string().max(2000).optional(),
  quantity: z.number().int().min(1).default(1),
  product_price: z.number().int().min(0),
  shipping_method: z.enum(['zasilkovna', 'personal_pickup']),
  shipping_price: z.number().int().min(0).default(0),
  zasilkovna_branch_id: z.string().optional(),
  zasilkovna_branch_name: z.string().optional(),
  customer_name: z.string().min(1).max(100),
  customer_email: z.string().email(),
  customer_phone: z.string().min(9).max(20),
});

export async function POST(req: NextRequest) {
  const supabase = await createServerClient();
  const { data: { user }, error: authError } = await supabase.auth.getUser();

  if (!user || authError) {
    return NextResponse.json({ error: 'Nepřihlášený uživatel' }, { status: 401 });
  }

  const body = await req.json();
  const validated = createOrderSchema.safeParse(body);

  if (!validated.success) {
    return NextResponse.json({ error: 'Neplatná data', details: validated.error.flatten() }, { status: 400 });
  }

  const input = validated.data;
  const pricing = calculateOrderPricing(input.product_price, input.shipping_price);

  const { data: order, error } = await supabase
    .from('orders')
    .insert({
      order_number: generateOrderNumber(),
      customer_id: user.id,
      maker_id: input.maker_id,
      product_id: input.product_id ?? null,
      title: input.title,
      description: input.description ?? null,
      quantity: input.quantity,
      product_price: pricing.product_price,
      shipping_price: pricing.shipping_price,
      platform_fee: pricing.platform_fee,
      maker_payout: pricing.maker_payout,
      total_price: pricing.total_price,
      shipping_method: input.shipping_method,
      zasilkovna_branch_id: input.zasilkovna_branch_id ?? null,
      zasilkovna_branch_name: input.zasilkovna_branch_name ?? null,
      customer_name: input.customer_name,
      customer_email: input.customer_email,
      customer_phone: input.customer_phone,
      status: 'pending_payment',
    })
    .select('id, order_number, total_price, status')
    .single();

  if (error) {
    return NextResponse.json({ error: 'Chyba při vytváření objednávky' }, { status: 500 });
  }

  return NextResponse.json(order, { status: 201 });
}

export async function GET(req: NextRequest) {
  const supabase = await createServerClient();
  const { data: { user } } = await supabase.auth.getUser();

  if (!user) {
    return NextResponse.json({ error: 'Nepřihlášený uživatel' }, { status: 401 });
  }

  const { searchParams } = new URL(req.url);
  const role = searchParams.get('role');
  const status = searchParams.get('status');
  const page = parseInt(searchParams.get('page') ?? '1', 10);
  const limit = 20;
  const from = (page - 1) * limit;
  const to = from + limit - 1;

  let query = supabase
    .from('orders')
    .select('id, order_number, title, total_price, status, created_at, customer_name, shipping_method, zasilkovna_branch_name', { count: 'exact' })
    .order('created_at', { ascending: false })
    .range(from, to);

  if (role === 'maker') {
    const { data: maker } = await supabase
      .from('makers')
      .select('id')
      .eq('user_id', user.id)
      .maybeSingle();

    if (!maker) {
      return NextResponse.json({ error: 'Maker nenalezen' }, { status: 404 });
    }
    query = query.eq('maker_id', maker.id);
  } else {
    query = query.eq('customer_id', user.id);
  }

  if (status) {
    query = query.eq('status', status);
  }

  const { data: orders, count, error } = await query;

  if (error) {
    return NextResponse.json({ error: 'Chyba při načítání objednávek' }, { status: 500 });
  }

  return NextResponse.json({ orders: orders ?? [], total: count ?? 0, page, limit });
}
