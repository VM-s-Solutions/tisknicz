/**
 * Hand-written wrapper around the anonymous shipping widget-config
 * endpoint exposed by .NET <c>ShippingController</c> at
 * <c>/api/v1/public/shipping/widget-config</c> on the Public host
 * (T-0070). Same convention as <c>catalog.ts</c> (patterns.md B.16).
 *
 * The endpoint is anonymous and serves
 * <c>Cache-Control: public, max-age=3600</c>, so SSR fetches are
 * CDN/browser-cacheable for free. The config feeds the official Packeta
 * widget v6 (<c>components/shared/zasilkovna-widget.tsx</c>) — the
 * Packeta REST API itself stays backend-only per CLAUDE.md.
 */

import type { IPickupPointWidgetConfig } from '../api-client/public-api.v1';
import { apiFetch } from '../runtime/api-fetch';
import type { ApiError, Result } from '../runtime/result';

/** Mirror of <c>PickupPointWidgetConfig</c> — `{ scriptUrl, publicKey, options }`. */
export type PickupPointWidgetConfig = Readonly<IPickupPointWidgetConfig>;

/**
 * Fetch the pickup-point widget configuration for a country + locale.
 * Czech-only defaults at launch; the parameters exist so future
 * countries reuse the same helper (CountryConfiguration drives the
 * backend side).
 */
export async function getWidgetConfig(
  country = 'CZ',
  locale = 'cs-CZ',
): Promise<Result<PickupPointWidgetConfig, ApiError>> {
  const params = new URLSearchParams();
  params.set('country', country);
  params.set('locale', locale);
  return apiFetch<PickupPointWidgetConfig>(
    'public',
    `/api/v1/public/shipping/widget-config?${params.toString()}`,
    { method: 'GET' },
  );
}
