/**
 * Hand-written wrappers around the T-0100 review endpoints. Same
 * convention as <c>orders-client.ts</c> / <c>payouts-client.ts</c>
 * (patterns.md B.16): we call <c>apiFetch</c> directly (not the
 * NSwag-generated <c>CustomerApi</c> / <c>MakerApi</c> classes) because
 * <c>apiFetch</c> returns <c>Result&lt;T, ApiError&gt;</c> and the
 * generated client throws on every non-2xx — that doesn't fit the Result
 * flow the route code uses.
 *
 * The customer submit + read-back wrappers (T-0115) and the maker
 * list + reply wrappers (T-0117) live in one file: the review surface
 * spans both audiences and the helper routes each call to the correct
 * host. The audience cookie rides along (browser: <c>credentials:
 * 'include'</c>; SSR: cookie forwarding per patterns.md B.14 / ADR 0024).
 * The IDOR shield is backend-side (per-audience compile-time split +
 * scoped reads); the frontend never passes a makerId.
 */

import {
  type IReviewableOrderDto,
  type ISubmittedReviewDto,
} from '../api-client/customer-api.v1';
import { type IMakerReceivedReviewDto } from '../api-client/maker-api.v1';
import { apiFetch } from '../runtime/api-fetch';
import { type ApiError, type Result, ok } from '../runtime/result';

// ---- Customer side (T-0115) ----

/**
 * Mirror of <c>ReviewableOrderDto</c> — a Delivered/Completed order with
 * no active review yet. <c>deliveredAt</c> is overridden to wire-shape
 * <c>string</c> (ISO 8601): <c>apiFetch</c> returns raw JSON without the
 * generated class's <c>Date</c>-parsing constructor (orders-client.ts B1
 * rationale).
 */
export type ReviewableOrder = Readonly<Omit<IReviewableOrderDto, 'deliveredAt'>> & {
  readonly deliveredAt: string;
};

/**
 * Mirror of <c>SubmittedReviewDto</c> — the customer's own review +
 * optional maker reply. <c>createdAt</c> wire-shaped to <c>string</c>.
 */
export type SubmittedReview = Readonly<Omit<ISubmittedReviewDto, 'createdAt'>> & {
  readonly createdAt: string;
};

interface GetReviewableOrdersEnvelope {
  readonly orders: readonly ReviewableOrder[];
}

interface GetSubmittedReviewsEnvelope {
  readonly reviews: readonly SubmittedReview[];
}

/** Mirror of <c>SubmitReviewResponse { reviewId, rating, createdAt }</c>. */
export interface SubmitReviewResult {
  readonly reviewId: string;
  readonly rating: number;
  readonly createdAt: string;
}

const CustomerBase = '/api/v1';

/**
 * Submit a review for a Delivered/Completed order the customer owns
 * (T-0100, US-customer-0015). Failure codes from <c>BusinessErrorMessage</c>:
 * <c>review.alreadyExists</c> (race — another tab/device submitted first),
 * <c>review.orderNotDelivered</c> (state regression), <c>review.ratingOutOfRange</c>
 * / <c>review.bodyTooLong</c> (bypassed UX mirror), <c>order.notFound</c>
 * (cross-tenant / unknown — IDOR-resistant 404). The backend stays
 * authoritative for eligibility + rating math + validation.
 */
export async function submitReview(
  orderId: string,
  input: { readonly rating: number; readonly body?: string },
): Promise<Result<SubmitReviewResult, ApiError>> {
  return apiFetch<SubmitReviewResult>(
    'customer',
    `${CustomerBase}/orders/${encodeURIComponent(orderId)}/review`,
    { method: 'POST', json: { rating: input.rating, body: input.body ?? null } },
  );
}

/**
 * The customer's Delivered/Completed orders with no active review yet —
 * the "leave a review" CTA list. Envelope unwrapped to the inner array.
 */
export async function getReviewableOrders(): Promise<
  Result<readonly ReviewableOrder[], ApiError>
> {
  const result = await apiFetch<GetReviewableOrdersEnvelope>(
    'customer',
    `${CustomerBase}/reviews/reviewable-orders`,
    { method: 'GET' },
  );
  return result.success ? ok(result.value.orders) : result;
}

/**
 * The reviews the customer themselves submitted (read-back). Envelope
 * unwrapped to the inner array.
 */
export async function getSubmittedReviews(): Promise<
  Result<readonly SubmittedReview[], ApiError>
> {
  const result = await apiFetch<GetSubmittedReviewsEnvelope>(
    'customer',
    `${CustomerBase}/reviews/mine`,
    { method: 'GET' },
  );
  return result.success ? ok(result.value.reviews) : result;
}

// ---- Maker side (T-0117) ----

/**
 * Mirror of <c>MakerReceivedReviewDto</c> — one review about the maker,
 * read-only (no customer identity, GDPR §C.7). <c>createdAt</c> /
 * <c>makerReplyAt</c> wire-shaped to <c>string | undefined</c>.
 */
export type MakerReceivedReview = Readonly<
  Omit<IMakerReceivedReviewDto, 'createdAt' | 'makerReplyAt'>
> & {
  readonly createdAt: string;
  readonly makerReplyAt: string | undefined;
};

/**
 * The maker dashboard reviews page: the paged list PLUS the maker's live
 * aggregate (<c>ratingAverageBp</c> / <c>ratingCount</c>) so the T-0117
 * header reads the authoritative recompute-over-all-active-rows value
 * (§A.3), never an average over the paged window.
 */
export interface MakerReviewsPage {
  readonly items: readonly MakerReceivedReview[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalCount: number;
  readonly totalPages?: number;
  readonly hasNextPage?: boolean;
  readonly hasPreviousPage?: boolean;
  readonly ratingAverageBp: number;
  readonly ratingCount: number;
}

interface GetMakerReviewsEnvelope {
  readonly reviews: {
    readonly items: readonly MakerReceivedReview[];
    readonly page: number;
    readonly pageSize: number;
    readonly totalCount: number;
    readonly totalPages?: number;
    readonly hasNextPage?: boolean;
    readonly hasPreviousPage?: boolean;
  };
  readonly ratingAverageBp: number;
  readonly ratingCount: number;
}

const MakerBase = '/api/v1/reviews';

/**
 * Maker dashboard "reviews about me" list (T-0100, 20/page backend
 * default — <c>pageSize</c> is never emitted). <c>page</c> is emitted
 * only when &gt; 1 so canonical URLs stay clean (patterns.md B.8). The
 * envelope's inner page + the aggregate are flattened into one object the
 * route renders directly.
 */
export async function getMakerReviews(
  input: { readonly page?: number } = {},
): Promise<Result<MakerReviewsPage, ApiError>> {
  const params = new URLSearchParams();
  if (input.page !== undefined && input.page > 1) params.set('page', String(input.page));
  const query = params.toString();
  const result = await apiFetch<GetMakerReviewsEnvelope>(
    'maker',
    query ? `${MakerBase}?${query}` : MakerBase,
    { method: 'GET' },
  );
  if (!result.success) return result;
  const { reviews, ratingAverageBp, ratingCount } = result.value;
  return ok({ ...reviews, ratingAverageBp, ratingCount });
}

/**
 * Attach (or overwrite) the maker's single public reply to a review
 * (T-0100, US-maker-0014). One reply per review — a re-submit OVERWRITES
 * (Q4). Failure codes: <c>review.replyTooLong</c> (≤500 cap),
 * <c>review.replyEmpty</c>, <c>review.notFound</c> (cross-tenant / unknown
 * → IDOR-resistant 404).
 */
export async function respondToReview(
  reviewId: string,
  reply: string,
): Promise<Result<void, ApiError>> {
  const result = await apiFetch<unknown>(
    'maker',
    `${MakerBase}/${encodeURIComponent(reviewId)}/reply`,
    { method: 'POST', json: { reply } },
  );
  return result.success ? ok(undefined) : result;
}
