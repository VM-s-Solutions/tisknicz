/**
 * Display-only money helpers for the frontend. The backend is the system
 * of record for prices (per CLAUDE.md "Money as `long` minor units"), so
 * everything here is pure formatting — no rounding rules, no currency
 * conversion, no business logic.
 *
 * Czech-only at launch. CZK strips haléře (integer division by 100) and
 * uses the Czech-locale thousands separator (NBSP) plus the literal
 * <c>Kč</c> suffix to match CLAUDE.md ("Currency: <c>1 234 Kč</c>, whole
 * CZK display, space thousands separator").
 *
 * Non-CZK input throws — keeps callers honest until the platform adds
 * EUR (post-launch per ADR 0007 multi-country roadmap). The throw is a
 * developer-time assertion, not a user-facing failure.
 */

const SUPPORTED_CURRENCY = 'CZK';

/**
 * Format a money amount as the Czech-locale display string
 * <c>"1 234 Kč"</c>. Drops the haléř fraction per the Czech CZK display
 * convention. The thousands separator is a NBSP (U+00A0) emitted by
 * <c>Intl.NumberFormat('cs-CZ')</c>.
 *
 * <para>
 * <paramref name="currency"/> is a guard, not a switch — only CZK is
 * supported at launch. Any non-CZK input throws via
 * <see cref="assertCzkCurrency"/> so callers can't silently render the
 * wrong currency. Callers that may receive non-CZK (display-only paths
 * where a fallback is acceptable, e.g. <c>ProductCard</c>) check the
 * currency themselves and route around the formatter.
 * </para>
 */
export function formatCzk(amountMinor: number, currency: string): string {
  assertCzkCurrency(currency);
  const whole = Math.trunc(amountMinor / 100);
  const formatted = new Intl.NumberFormat('cs-CZ', {
    style: 'decimal',
    maximumFractionDigits: 0,
  }).format(whole);
  return `${formatted} Kč`;
}

/**
 * Currency guard for <see cref="formatCzk"/>. Keeps the launch invariant
 * loud: any non-CZK price reaching the formatter during MVP is a
 * contract violation worth surfacing in dev — backend price snapshots
 * are CZK per CountryConfiguration. Exported so callers that perform
 * their own currency routing (and want to skip the formatter) can still
 * share the predicate.
 */
export function assertCzkCurrency(currency: string): void {
  if (currency !== SUPPORTED_CURRENCY) {
    throw new Error(
      `formatCzk only supports CZK at launch; got "${currency}". Add EUR/etc. when adding the next country.`,
    );
  }
}
