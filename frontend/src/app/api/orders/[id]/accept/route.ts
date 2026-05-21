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

  // Verify maker ownership
  const { data: maker } = await supabase
    .from('makers')
    .select('id')
    .eq('user_id', user.id)
    .eq('id', order.maker_id)
    .maybeSingle();

  if (!maker) {
    return NextResponse.json({ error: 'Přístup odepřen' }, { status: 403 });
  }

  if (order.status !== 'paid') {
    return NextResponse.json({ error: 'Objednávku nelze přijmout v tomto stavu' }, { status: 400 });
  }

  const { error } = await supabase
    .from('orders')
    .update({ status: 'accepted', accepted_at: new Date().toISOString() })
    .eq('id', id);

  if (error) {
    return NextResponse.json({ error: 'Chyba při aktualizaci' }, { status: 500 });
  }

  return NextResponse.json({ status: 'accepted' });
}
