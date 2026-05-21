const ARES_BASE_URL = 'https://ares.gov.cz/ekonomicke-subjekty-v-be/rest/ekonomicke-subjekty';

export interface AresData {
  ico: string;
  companyName: string;
  street: string;
  city: string;
  zip: string;
  legalForm: string | null;
  dic: string | null;
}

interface AresResponse {
  ico: string;
  obchodniJmeno: string;
  sidlo?: {
    textovaAdresa?: string;
    nazevObce?: string;
    psc?: number;
    nazevUlice?: string;
    cisloDomovni?: number;
    cisloOrientacni?: number;
  };
  pravniForma?: string;
  dic?: string;
}

const LEGAL_FORMS: Record<string, string> = {
  '101': 'Fyzická osoba podnikající',
  '112': 's.r.o.',
  '121': 'a.s.',
  '205': 'Družstvo',
  '301': 'Státní podnik',
  '331': 'Příspěvková organizace',
  '706': 'Spolek',
  '736': 'Nadace',
};

export async function fetchFromAres(ico: string): Promise<AresData> {
  const response = await fetch(`${ARES_BASE_URL}/${ico}`, {
    headers: { Accept: 'application/json' },
    next: { revalidate: 86400 },
  });

  if (!response.ok) {
    if (response.status === 404) {
      throw new Error('IČO nebylo nalezeno v registru ARES');
    }
    throw new Error('Chyba při komunikaci s ARES');
  }

  const data: AresResponse = await response.json();
  const { sidlo } = data;

  let street = '';
  if (sidlo?.nazevUlice) {
    street = sidlo.nazevUlice;
    if (sidlo.cisloDomovni) {
      street += ` ${sidlo.cisloDomovni}`;
      if (sidlo.cisloOrientacni) {
        street += `/${sidlo.cisloOrientacni}`;
      }
    }
  } else if (sidlo?.textovaAdresa) {
    street = sidlo.textovaAdresa.split(',')[0]?.trim() ?? '';
  }

  return {
    ico: data.ico,
    companyName: data.obchodniJmeno,
    street,
    city: sidlo?.nazevObce ?? '',
    zip: sidlo?.psc ? String(sidlo.psc).replace(/(\d{3})(\d{2})/, '$1 $2') : '',
    legalForm: data.pravniForma ? (LEGAL_FORMS[data.pravniForma] ?? data.pravniForma) : null,
    dic: data.dic ?? null,
  };
}
