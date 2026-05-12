import { NextRequest, NextResponse } from 'next/server';
import { createServerClient } from '@/lib/supabase/server';

export async function POST(
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
    .select('id, status, maker_id')
    .eq('id', id)
    .single();

  if (!order) {
    return NextResponse.json({ error: 'Objednávka nenalezena' }, { status: 404 });
  }

  const { data: maker } = await supabase
    .from('makers')
    .select('id')
    .eq('user_id', user.id)
    .eq('id', order.maker_id)
    .maybeSingle();

  if (!maker) {
    return NextResponse.json({ error: 'Přístup odepřen' }, { status: 403 });
  }

  if (order.status !== 'accepted') {
    return NextResponse.json({ error: 'Objednávku nelze odeslat v tomto stavu' }, { status: 400 });
  }

  const now = new Date();
  const autoDeliverAt = new Date(now.getTime() + 7 * 24 * 60 * 60 * 1000);

  const { error } = await supabase
    .from('orders')
    .update({
      status: 'shipped',
      shipped_at: now.toISOString(),
      auto_deliver_at: autoDeliverAt.toISOString(),
    })
    .eq('id', id);

  if (error) {
    return NextResponse.json({ error: 'Chyba při aktualizaci' }, { status: 500 });
  }

  return NextResponse.json({ status: 'shipped' });
}
