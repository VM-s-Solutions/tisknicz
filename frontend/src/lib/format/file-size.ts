/**
 * Locale-aware file-size display for attachment lists (T-0084a/b).
 * Mirrors the `formatWeight` convention (lib/format/weight.ts): the
 * decimal separator must flow through `Intl.NumberFormat('cs-CZ')` so
 * Czech output uses a comma, not a dot. Binary units (KiB/MiB) match
 * the backend's 10-MiB attachment cap, displayed with the common
 * kB/MB labels.
 */
export function formatFileSize(bytes: number): string {
  if (bytes < 1024) {
    return `${bytes} B`;
  }
  const kib = bytes / 1024;
  if (kib < 1024) {
    const formatted = new Intl.NumberFormat('cs-CZ', {
      maximumFractionDigits: 0,
    }).format(kib);
    return `${formatted} kB`;
  }
  const mib = kib / 1024;
  const formatted = new Intl.NumberFormat('cs-CZ', {
    minimumFractionDigits: 1,
    maximumFractionDigits: 1,
  }).format(mib);
  return `${formatted} MB`;
}
