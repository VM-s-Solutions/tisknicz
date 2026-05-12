import { NextResponse, type NextRequest } from 'next/server';
import { fetchFromAres } from '@/lib/ares/client';
import { validateICO } from '@/lib/utils/validation';

export async function GET(
  _request: NextRequest,
  { params }: { params: Promise<{ ico: string }> }
) {
  const { ico } = await params;

  if (!validateICO(ico)) {
    return NextResponse.json(
      { error: 'Neplatný formát IČO. Zadejte 8 číslic.' },
      { status: 400 }
    );
  }

  try {
    const data = await fetchFromAres(ico);
    return NextResponse.json(data);
  } catch (err) {
    const message = err instanceof Error ? err.message : 'Chyba při komunikaci s ARES';
    return NextResponse.json({ error: message }, { status: 404 });
  }
}
