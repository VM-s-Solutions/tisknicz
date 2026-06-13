/**
 * Hand-written wrappers around the authenticated maker payout endpoints
 * on the Maker host (T-0112 list/detail + T-0112a fee-invoice download).
 * Same convention as <c>maker-orders.ts</c> (patterns.md B.16): we call
 * <c>apiFetch</c> directly (not the NSwag-generated <c>MakerApi</c> class)
 * because <c>apiFetch</c> returns <c>Result&lt;T, ApiError&gt;</c> and the
 * generated client throws on every non-2xx.
 *
 * All endpoints require an authenticated maker session; the audience cookie
 * rides along (browser: <c>credentials: 'include'</c>; SSR: cookie
 * forwarding per patterns.md B.14 / ADR 0024). A customer JWT replayed
 * against the Maker host 401s at the backend (ADR 0013). The IDOR shield is
 * backend-side (T-0112 resolves makerId from the session); the frontend
 * never passes a makerId.
 *
 * NOTE: there is NO CSV affordance anywhere — the operator bank file is
 * admin-only (cross-maker PII, T-0116 A.1 / AC-10). Makers download only
 * their own Fee-invoice PDF.
 */

import {
  type IMakerOutboxEventDto,
  type IMakerPayoutDetailDto,
  type IMakerPayoutListItemDto,
  OutboxDeliveryStatus,
  PayoutBatchState,
} from '../api-client/maker-api.v1';
import { apiFetch } from '../runtime/api-fetch';
import { type ApiError, type Result, ok } from '../runtime/result';

// Re-export the string enums so route code never imports lib/api-client/ directly.
export { OutboxDeliveryStatus, PayoutBatchState };

/**
 * Mirror of <c>GetMakerPayouts.DefaultPageSize</c> (T-0112 Validator).
 * UX-only duplicate for URL clamping — the backend Validator stays
 * authoritative and 400s on a forced out-of-range value.
 */
export const MAKER_PAYOUTS_DEFAULT_PAGE_SIZE = 20;
export const MAKER_PAYOUTS_MAX_PAGE_SIZE = 50;

/**
 * Mirror of <c>MakerPayoutListItemDto</c> (T-0112). <c>completedAt</c> is
 * overridden to wire-shape <c>string | undefined</c> (ISO 8601) —
 * <c>apiFetch</c> returns raw JSON without the generated class's
 * <c>Date</c>-parsing constructor (maker-orders.ts B1 rationale). The DTO
 * deliberately carries NO bankReference and NO CSV reference (cross-maker
 * PII / operator-internal).
 */
export type MakerPayoutListItem = Readonly<Omit<IMakerPayoutListItemDto, 'completedAt'>> & {
  readonly completedAt: string | undefined;
};

/** Wire shape of <c>PagedData&lt;MakerPayoutListItemDto&gt;</c>. */
export interface MakerPayoutsPage {
  readonly items: readonly MakerPayoutListItem[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalCount: number;
  readonly totalPages?: number;
  readonly hasNextPage?: boolean;
  readonly hasPreviousPage?: boolean;
}

/** Mirror of <c>MakerPayoutOrderLineDto</c> (T-0112) — the per-order breakdown. */
export type MakerPayoutOrderLine = Readonly<{
  readonly orderId: string;
  readonly orderNumber: string;
  readonly productPriceMinor: number;
  readonly shippingPriceMinor: number;
  readonly platformFeeAmountMinor: number;
  readonly makerPayoutAmountMinor: number;
  readonly currency: string;
}>;

/** Mirror of <c>MakerPayoutDetailDto</c> (T-0112) — wire-shape <c>completedAt</c>. */
export type MakerPayoutDetail = Readonly<
  Omit<IMakerPayoutDetailDto, 'completedAt' | 'orders'>
> & {
  readonly completedAt: string | undefined;
  readonly orders: readonly MakerPayoutOrderLine[];
};

/** Mirror of <c>MakerOutboxEventDto</c> (T-0112) — payload-free; wire-shape <c>occurredAt</c>. */
export type MakerOutboxEvent = Readonly<Omit<IMakerOutboxEventDto, 'occurredAt'>> & {
  readonly occurredAt: string;
};

interface GetMakerPayoutsEnvelope {
  readonly payouts: MakerPayoutsPage;
}

interface GetMakerPayoutDetailEnvelope {
  readonly detail: MakerPayoutDetail;
}

const Base = '/api/v1/payout-batches';

/**
 * Streaming-download budget (maker-orders.ts precedent): the worst-tolerable
 * mobile-downlink case needs far more than the JSON default, so binary
 * downloads share the 120 s ceiling.
 */
const DOWNLOAD_TIMEOUT_MS = 120_000;

/**
 * Maker dashboard payout list (T-0112). <c>page</c> is emitted only when
 * &gt; 1 so canonical URLs stay clean (patterns.md B.8). The backend
 * Validator stays authoritative (page clamps). Envelope unwrapped to the
 * inner page.
 */
export async function getMakerPayouts(
  input: { readonly page?: number } = {},
): Promise<Result<MakerPayoutsPage, ApiError>> {
  const params = new URLSearchParams();
  if (input.page !== undefined && input.page > 1) params.set('page', String(input.page));
  const query = params.toString();
  const result = await apiFetch<GetMakerPayoutsEnvelope>(
    'maker',
    query ? `${Base}?${query}` : Base,
    { method: 'GET' },
  );
  return result.success ? ok(result.value.payouts) : result;
}

/**
 * Maker payout-batch detail with the per-order breakdown (T-0112). The
 * backend returns ONE 404 shape for foreign + unknown batch ids (no IDOR
 * oracle) → <c>ApiError.type === 'NotFound'</c>. Envelope unwrapped.
 */
export async function getMakerPayoutDetail(
  batchId: string,
): Promise<Result<MakerPayoutDetail, ApiError>> {
  const result = await apiFetch<GetMakerPayoutDetailEnvelope>(
    'maker',
    `${Base}/${encodeURIComponent(batchId)}`,
    { method: 'GET' },
  );
  return result.success ? ok(result.value.detail) : result;
}

/**
 * Maker Fee-invoice PDF download (T-0112a). Deliberately does NOT use the
 * generated <c>MakerApi</c> file method: NSwag types the file response as
 * <c>Promise&lt;void&gt;</c> and discards the body (the <c>label()</c> gap).
 * Fetching as a blob through <c>apiFetch</c> keeps the audience cookie, the
 * timeout budget and RFC7807 parsing; the caller turns the Blob into a named
 * download. The endpoint is keyed on the Fee <c>invoiceId</c>
 * (<c>GET /api/v1/maker/files/invoices/{invoiceId}</c>). 404
 * <c>order.notFound</c> for cross-maker / wrong-type ids;
 * <c>invoice.notYetRendered</c> for the render race.
 */
export async function downloadFeeInvoice(
  feeInvoiceId: string,
): Promise<Result<Blob, ApiError>> {
  return apiFetch<Blob>(
    'maker',
    `/api/v1/maker/files/invoices/${encodeURIComponent(feeInvoiceId)}`,
    { method: 'GET', parse: 'blob', timeoutMs: DOWNLOAD_TIMEOUT_MS },
  );
}
