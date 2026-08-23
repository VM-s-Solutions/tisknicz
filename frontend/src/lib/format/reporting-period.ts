import { RevenueBucketGranularity } from '@/lib/api-client-helpers/admin-ops-client';

/**
 * Czech-locale labels for the admin revenue reports (T-0192): the month the
 * earnings panel is showing, and the buckets along the chart's x-axis.
 *
 * <para>
 * Pure display, like the rest of `lib/format/`. Every label comes out of
 * `Intl.DateTimeFormat('cs-CZ')` rather than a hand-written month table —
 * Czech declines month names (nominative "srpen" standalone, genitive
 * "22. srpna" beside a day), and Intl already knows which form each pattern
 * wants. A hardcoded list would get one of them wrong.
 * </para>
 *
 * <para>
 * <b>A bucket is labelled in the timezone it was computed in</b>, which the
 * backend sends on the series response. A bucket start is an instant: an
 * operator whose laptop is on UTC — or on a plane — would otherwise see an
 * axis that disagreed with its own data by an hour or two, and the 22:00
 * labels would look like a bug in the chart rather than in the formatting.
 * That is why every function here takes an explicit `timeZoneId` and none of
 * them falls back to the browser's zone.
 * </para>
 */

/** Month names are the same in every zone; mid-month dodges any boundary. */
const MONTH_LABEL_DAY = 15;

/** `"srpen 2026"` — the earnings panel's heading. */
export function formatReportingMonth(year: number, month: number): string {
  return new Intl.DateTimeFormat('cs-CZ', {
    timeZone: 'UTC',
    month: 'long',
    year: 'numeric',
  }).format(new Date(Date.UTC(year, month - 1, MONTH_LABEL_DAY)));
}

/**
 * Short axis label for one bucket. Deliberately terse — an axis carries the
 * values that were not directly labelled, so it has to stay readable at 90
 * ticks without wrapping or colliding.
 */
export function formatBucketLabel(
  bucketStartIso: string,
  granularity: RevenueBucketGranularity,
  timeZoneId: string,
): string {
  const at = new Date(bucketStartIso);
  const format = (options: Intl.DateTimeFormatOptions) =>
    new Intl.DateTimeFormat('cs-CZ', { timeZone: timeZoneId, ...options }).format(at);

  switch (granularity) {
    case RevenueBucketGranularity.Hour:
      return format({ hour: '2-digit', minute: '2-digit', hour12: false });
    case RevenueBucketGranularity.Month:
      // Month alone, no year: cs-CZ ignores `short` as soon as a year is
      // present ("srpen 26", too wide for twelve ticks), and a twelve-month
      // window holds each month exactly once, so the year adds nothing. The
      // tooltip and the table still say "srpen 2026".
      return format({ month: 'short' });
    case RevenueBucketGranularity.Day:
    case RevenueBucketGranularity.Week:
    default:
      return format({ day: 'numeric', month: 'numeric' });
  }
}

/**
 * The fuller label a tooltip and the data table use. Says which PERIOD the
 * number covers, not just when it started — "17. 8." on its own is ambiguous
 * on a weekly series, where it means the whole week.
 */
export function formatBucketPeriod(
  bucketStartIso: string,
  granularity: RevenueBucketGranularity,
  timeZoneId: string,
): string {
  const at = new Date(bucketStartIso);
  const format = (options: Intl.DateTimeFormatOptions) =>
    new Intl.DateTimeFormat('cs-CZ', { timeZone: timeZoneId, ...options }).format(at);

  switch (granularity) {
    case RevenueBucketGranularity.Hour: {
      const day = format({ day: 'numeric', month: 'numeric', year: 'numeric' });
      const from = format({ hour: '2-digit', minute: '2-digit', hour12: false });
      const to = new Intl.DateTimeFormat('cs-CZ', {
        timeZone: timeZoneId,
        hour: '2-digit',
        minute: '2-digit',
        hour12: false,
      }).format(new Date(at.getTime() + 60 * 60 * 1000));
      return `${day}, ${from}–${to}`;
    }
    case RevenueBucketGranularity.Week:
      return `týden od ${format({ day: 'numeric', month: 'numeric', year: 'numeric' })}`;
    case RevenueBucketGranularity.Month:
      return format({ month: 'long', year: 'numeric' });
    case RevenueBucketGranularity.Day:
    default:
      return format({ weekday: 'long', day: 'numeric', month: 'numeric', year: 'numeric' });
  }
}

/** `?month=` value for a year/month pair — zero-padded so it sorts and reads like a date. */
export function toMonthParam(year: number, month: number): string {
  return `${year}-${String(month).padStart(2, '0')}`;
}

/**
 * Reads `?month=YYYY-MM`. Returns `null` for anything unrecognised — a
 * hand-typed param must fall back to the month in progress rather than reach
 * the API, which would 400 and blank the panel. The bounds mirror the
 * backend Validator; it stays authoritative.
 */
export function parseMonthParam(raw: string): { year: number; month: number } | null {
  const match = /^(\d{4})-(\d{2})$/.exec(raw.trim());
  if (!match) return null;

  const year = Number(match[1]);
  const month = Number(match[2]);
  if (year < 2020 || year > 2100 || month < 1 || month > 12) return null;

  return { year, month };
}

/** Steps a year/month pair by whole months, rolling the year. */
export function shiftMonth(
  year: number,
  month: number,
  delta: number,
): { year: number; month: number } {
  const zeroBased = year * 12 + (month - 1) + delta;
  return { year: Math.floor(zeroBased / 12), month: (zeroBased % 12) + 1 };
}
