/**
 * Czech-locale weight formatter shared by the public product detail page
 * (T-0048) and the maker dashboard product list (T-0049). Pure display,
 * no business logic — the backend stores grams as <c>int</c> and the
 * frontend chooses how to show them.
 *
 * Rule: <c>&lt;1000 g</c> renders as the integer plus " g"
 * (e.g. <c>"450 g"</c>); <c>≥ 1000 g</c> renders as kilograms to one
 * decimal with the Czech comma separator from
 * <c>Intl.NumberFormat('cs-CZ')</c>, which also handles the NBSP
 * thousands separator if a four-digit kg value ever shows up
 * (e.g. <c>"1,5 kg"</c>, <c>"12,3 kg"</c>).
 */
export function formatWeight(grams: number): string {
  if (grams < 1000) {
    return `${grams} g`;
  }
  const kg = grams / 1000;
  const formatted = new Intl.NumberFormat('cs-CZ', {
    minimumFractionDigits: 1,
    maximumFractionDigits: 1,
  }).format(kg);
  return `${formatted} kg`;
}
