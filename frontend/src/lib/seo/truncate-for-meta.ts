/**
 * Truncate free-form text to a sentence-safe slice suitable for a
 * <c>&lt;meta name="description"&gt;</c> tag. Collapses any whitespace
 * run to a single space, trims, and (when the input exceeds
 * <paramref name="max"/>) cuts back to the last word boundary inside
 * the limit before appending an ellipsis.
 *
 * <para>
 * Shared by <c>generateMetadata</c> across the public route group
 * (T-0047 maker profile, T-0048 product detail, ...). Centralised here
 * so the metadata truncation contract doesn't drift between pages —
 * SEO consumers (Google, OG snippet renderers) read the same shape
 * everywhere.
 * </para>
 *
 * <para>
 * The <c>lastSpace &gt; max / 2</c> heuristic keeps the cut from
 * removing more than roughly half the budget when the limit lands
 * mid-word in a long token (e.g. an URL). Below that threshold we fall
 * back to a hard slice to avoid returning a near-empty string. The
 * threshold scales with <paramref name="max"/> so a caller passing a
 * non-default limit gets the same "roughly half" guarantee.
 * </para>
 */
export function truncateForMeta(text: string, max = 160): string {
  const collapsed = text.replace(/\s+/g, ' ').trim();
  if (collapsed.length <= max) return collapsed;
  const slice = collapsed.slice(0, max);
  const lastSpace = slice.lastIndexOf(' ');
  const minSpaceIndex = Math.floor(max / 2);
  return (lastSpace > minSpaceIndex ? slice.slice(0, lastSpace) : slice).trimEnd() + '…';
}
