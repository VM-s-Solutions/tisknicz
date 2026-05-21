import { NextRequest, NextResponse } from 'next/server';
import { createServerClient } from '@/lib/supabase/server';
import { z } from 'zod';

const messageSchema = z.object({
  content: z.string().min(1).max(2000),
});

export async function GET(
  _req: NextRequest,
  { params }: { params: Promise<{ id: string }> }
) {
  const { id } = await params;
  const supabase = await createServerClient();
  const { data: { user } } = await supabase.auth.getUser();

  if (!user) {
    return NextResponse.json({ error: 'Nepřihlášený uživatel' }, { status: 401 });
  }

  const { data: order } = await supabase
    .from('orders')
    .select('id, customer_id, maker_id')
    .eq('id', id)
    .single();

  if (!order) {
    return NextResponse.json({ error: 'Objednávka nenalezena' }, { status: 404 });
  }

  const isCustomer = order.customer_id === user.id;
  const { data: maker } = await supabase
    .from('makers')
    .select('id')
    .eq('user_id', user.id)
    .eq('id', order.maker_id)
    .maybeSingle();

  if (!isCustomer && !maker) {
    return NextResponse.json({ error: 'Přístup odepřen' }, { status: 403 });
  }

  const { data: messages } = await supabase
    .from('messages')
    .select('id, sender_id, content, created_at')
    .eq('order_id', id)
    .order('created_at', { ascending: true });

  return NextResponse.json(messages ?? []);
}

export async function POST(
  req: NextRequest,
  { params }: { params: Promise<{ id: string }> }
) {
  const { id } = await params;
  const supabase = await createServerClient();
  const { data: { user } } = await supabase.auth.getUser();

  if (!user) {
    return NextResponse.json({ error: 'Nepřihlášený uživatel' }, { status: 401 });
  }

  const body = await req.json();
  const validated = messageSchema.safeParse(body);

  if (!validated.success) {
    return NextResponse.json({ error: 'Neplatná zpráva' }, { status: 400 });
  }

  const { data: order } = await supabase
    .from('orders')
    .select('id, customer_id, maker_id')
    .eq('id', id)
    .single();

  if (!order) {
    return NextResponse.json({ error: 'Objednávka nenalezena' }, { status: 404 });
  }

  const isCustomer = order.customer_id === user.id;
  const { data: maker } = await supabase
    .from('makers')
    .select('id')
    .eq('user_id', user.id)
    .eq('id', order.maker_id)
    .maybeSingle();

  if (!isCustomer && !maker) {
    return NextResponse.json({ error: 'Přístup odepřen' }, { status: 403 });
  }

  const { data: message, error } = await supabase
    .from('messages')
    .insert({
      order_id: id,
      sender_id: user.id,
      content: validated.data.content,
    })
    .select('id, sender_id, content, created_at')
    .single();

  if (error) {
    return NextResponse.json({ error: 'Chyba při odesílání zprávy' }, { status: 500 });
  }

  return NextResponse.json(message, { status: 201 });
}
