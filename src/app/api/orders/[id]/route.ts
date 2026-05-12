import { NextRequest, NextResponse } from 'next/server';
import { createServerClient } from '@/lib/supabase/server';

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

  const { data: order, error } = await supabase
    .from('orders')
    .select('*')
    .eq('id', id)
    .single();

  if (error || !order) {
    return NextResponse.json({ error: 'Objednávka nenalezena' }, { status: 404 });
  }

  // Check access: customer or maker
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

  return NextResponse.json(order);
}
