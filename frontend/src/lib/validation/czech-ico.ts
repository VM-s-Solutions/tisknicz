/**
 * Czech IČO (8-digit company registration number) checksum validation —
 * a TS mirror of the backend's `CzechIcoValidator` (mod-11 weighted sum,
 * ADR 0018 §"Validation before lookup"). Used by the maker registration
 * form (T-0159) to reject typos BEFORE any ARES round trip; the backend
 * re-validates authoritatively on lookup and registration.
 *
 * Algorithm: for digits d1..d8, s = Σ d_i · (8−i+1) over i=1..7,
 * m = s mod 11; expected checksum d8 = 1 (m=0), 0 (m=1), 1 (m=10),
 * otherwise 11−m.
 */
export function isValidCzechIco(ico: string): boolean {
  if (!/^[0-9]{8}$/.test(ico)) return false;

  const digits = [...ico].map((c) => c.charCodeAt(0) - 48);
  let sum = 0;
  for (let i = 0; i < 7; i++) {
    sum += digits[i] * (8 - i);
  }
  const mod = sum % 11;
  const expected = mod === 0 ? 1 : mod === 1 ? 0 : mod === 10 ? 1 : 11 - mod;
  return digits[7] === expected;
}

/** Keeps only digits and caps at the canonical 8 characters. */
export function normalizeIcoInput(raw: string): string {
  return raw.replace(/[^0-9]/g, '').slice(0, 8);
}
