/**
 * Hand-written wrappers around the authenticated admin control-plane
 * endpoints on the .NET Admin host (T-0109 outbox retry/acknowledge,
 * T-0108 country-config update, T-0103 payout complete + operator CSV +
 * T-0126 processing-payout / stalled-outbox count, T-0110 GDPR erase, and
 * the T-0126-backed admin invoice PDF). Same convention as
 * <c>admin-orders.ts</c> / <c>admin-client.ts</c> (patterns.md B.16): we
 * call <see cref="apiFetch"/> directly (not the NSwag-generated
 * <c>AdminApi</c> class) because <c>apiFetch</c> returns
 * <c>Result&lt;T, ApiError&gt;</c> and the generated client throws on every
 * non-2xx.
 *
 * All endpoints require an authenticated admin session; the
 * audience-scoped cookies set by <c>AuthController.login</c> on the admin
 * host ride along (browser: <c>credentials: 'include'</c>; SSR: cookie
 * forwarding per patterns.md B.14 / ADR 0024). A customer/maker JWT
 * replayed against the Admin host 401s at the backend (ADR 0013).
 *
 * T-0118c is a READ-ONLY consumer of <c>lib/api-client/</c>: this module
 * wraps the generated request/response *shapes* (imported type-only) but
 * never edits the generated file. The outbox retry/ack asymmetry, the
 * provider-change retype gate, the payout forward-only terminal state and
 * the delete-user double interlock are ALL backend business logic — these
 * helpers only post inputs and surface the typed verdict.
 *
 * T-0127 closed the four read gaps these surfaces logged against
 * themselves (Q-0029 + Q-0024): the country-config GET (`getCountryConfig`
 * — pre-fills the form), the stalled-outbox LIST (`getStalledOutboxEvents`
 * — browsable triage), the payout-batch LIST (`getPayoutBatches` —
 * browsable settlement) and the per-user in-flight filter on the admin-
 * orders read (in `admin-orders.ts` — drives the delete-user proactive
 * pre-disable). The count reads stay for the dashboard tiles.
 */

import {
  type IAcknowledgeOutboxEventResponse,
  type IAdminPayoutBatchListItemDto,
  type IDeleteUserPermanentlyResponse,
  type IGetCountryConfigurationResponse,
  type IMarkPayoutBatchCompletedResponse,
  type IRetryOutboxEventResponse,
  type IStalledOutboxEventDto,
  type IUpdateCountryConfigurationResponse,
  type IGetPlatformRevenueResponse,
  InvoicingMode,
  PayoutBatchState,
  RevenueWindow,
} from '../api-client/admin-api.v1';
import { apiFetch } from '../runtime/api-fetch';
import { type ApiError, type Result, ok } from '../runtime/result';

// Re-export the enums so route code never imports `lib/api-client/` directly.
export { InvoicingMode, PayoutBatchState, RevenueWindow };

// Leading slash matters: apiFetch concatenates `${baseUrl}${path}` against
// host URLs with no trailing slash (admin-client.ts precedent).
const OutboxBase = '/api/v1/outbox-events';
const CountryBase = '/api/v1/country-configurations';
const PayoutBase = '/api/v1/payout-batches';
const UsersBase = '/api/v1/users';
const InvoicesBase = '/api/v1/admin-invoices';
const MakersBase = '/api/v1/makers';
const RevenueBase = '/api/v1/platform-revenue';

/**
 * Streaming-download budget (payouts-client.ts precedent): the operator
 * CSV bank file + the invoice PDF share the 120 s ceiling so a slow
 * downlink doesn't trip the 8 s JSON default.
 */
const DOWNLOAD_TIMEOUT_MS = 120_000;

// ---- Shared list constants ----

/**
 * Mirrors of the backend list Validators' DefaultPageSize / MaxPageSize
 * (PagedData clamp `[1,50]`, default 20). UX-only duplicates for URL
 * clamping — the backend Validator stays authoritative (admin-client.ts
 * precedent). The stalled-outbox + payout-batch LISTs share the window.
 */
export const ADMIN_OPS_LIST_DEFAULT_PAGE_SIZE = 20;
export const ADMIN_OPS_LIST_MAX_PAGE_SIZE = 50;

/** Generic wire shape of <c>PagedData&lt;T&gt;</c> (totalPages/hasNext/hasPrevious optional). */
export interface AdminOpsPage<TItem> {
  readonly items: readonly TItem[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalCount: number;
  readonly totalPages?: number;
  readonly hasNextPage?: boolean;
  readonly hasPreviousPage?: boolean;
}

// ---- Outbox count (T-0126 GET /outbox-events/stalled/count) ----

/**
 * Stalled-outbox count (T-0126). The dashboard-tile read — the triage page
 * now browses the LIST (see {@link getStalledOutboxEvents}); this count
 * still backs the KPI tile. `null` on failure so the page degrades
 * gracefully.
 */
export async function getStalledOutboxCount(): Promise<Result<number, ApiError>> {
  const result = await apiFetch<{ count: number }>('admin', `${OutboxBase}/stalled/count`, {
    method: 'GET',
  });
  return result.success ? ok(result.value.count) : result;
}

// ---- Stalled-outbox LIST (T-0127 GET /outbox-events/stalled) ----

/** Mirror of <c>StalledOutboxEventDto</c> (T-0127). <c>createdAt</c> wire-shape <c>string</c> (ISO 8601). */
export type StalledOutboxEvent = Readonly<Omit<IStalledOutboxEventDto, 'createdAt'>> & {
  readonly createdAt: string;
};

/** Generated envelope around the list page (<c>GetStalledOutboxEventsResponse { events }</c>). */
interface GetStalledOutboxEventsEnvelope {
  readonly events: AdminOpsPage<StalledOutboxEvent>;
}

/**
 * Stalled-outbox LIST (T-0127, US-admin-0014). Browsable paged triage —
 * the SAME stalled set <c>CountStalledAsync</c> counts
 * (`ProcessedAt == null && NextRetryAt == null && LastErrorKind != None`),
 * `CreatedAt DESC`. The operator browses + retries/acknowledges by visible
 * id instead of pasting one blind. Params emitted only when they diverge
 * from the backend defaults (patterns.md B.8). Backend Validator
 * authoritative.
 */
export async function getStalledOutboxEvents(
  page: number,
  pageSize: number = ADMIN_OPS_LIST_DEFAULT_PAGE_SIZE,
): Promise<Result<AdminOpsPage<StalledOutboxEvent>, ApiError>> {
  const params = new URLSearchParams();
  if (page > 1) params.set('page', String(page));
  if (pageSize !== ADMIN_OPS_LIST_DEFAULT_PAGE_SIZE) params.set('pageSize', String(pageSize));
  const query = params.toString();
  const result = await apiFetch<GetStalledOutboxEventsEnvelope>(
    'admin',
    query ? `${OutboxBase}/stalled?${query}` : `${OutboxBase}/stalled`,
    { method: 'GET' },
  );
  return result.success ? ok(result.value.events) : result;
}

// ---- Outbox retry (T-0109 POST /outbox-events/{id}/retry) ----

/** Wire shape of <c>RetryOutboxEventResponse</c> (the new retry count + next attempt). */
export type RetryOutboxEventResult = Readonly<IRetryOutboxEventResponse>;

/**
 * Outbox retry (T-0109, US-admin-0014). A one-shot nudge — no body.
 * Retry-on-processed is a hard 409 `outbox.alreadyProcessed` (NOT a silent
 * success); an unknown row 404s `outbox.rowNotFound`. The dispatcher owns
 * the actual re-send; this helper only posts the nudge.
 */
export async function retryOutboxEvent(
  id: string,
): Promise<Result<RetryOutboxEventResult, ApiError>> {
  return apiFetch<RetryOutboxEventResult>(
    'admin',
    `${OutboxBase}/${encodeURIComponent(id)}/retry`,
    { method: 'POST' },
  );
}

// ---- Outbox acknowledge (T-0109 POST /outbox-events/{id}/acknowledge) ----

/** Wire shape of <c>AcknowledgeOutboxEventResponse</c>. */
export type AcknowledgeOutboxEventResult = Readonly<IAcknowledgeOutboxEventResponse>;

/**
 * Outbox acknowledge (T-0109, US-admin-0014) — the "stop bothering me"
 * note that rides the audit log. Reason is mandatory (backend Validator,
 * &le; 2000 chars); re-acknowledging an already-acknowledged row resolves
 * benignly (200). The 2000-char ceiling stays backend-authoritative.
 */
export async function acknowledgeOutboxEvent(
  id: string,
  reason: string,
): Promise<Result<AcknowledgeOutboxEventResult, ApiError>> {
  return apiFetch<AcknowledgeOutboxEventResult>(
    'admin',
    `${OutboxBase}/${encodeURIComponent(id)}/acknowledge`,
    { method: 'POST', json: { reason } },
  );
}

// ---- Country-config read (T-0127 GET /country-configurations/{code}) ----

/**
 * Mirror of <c>GetCountryConfigurationResponse</c> (T-0127). The SAME field
 * set <c>UpdateCountryConfiguration</c>'s Response echoes, so the form
 * round-trips byte-for-byte (the GET pre-fills, the PUT full-replaces). No
 * <c>Date</c> fields → no wire-shape override needed.
 */
export type CountryConfig = Readonly<IGetCountryConfigurationResponse>;

/**
 * Country-config read (T-0127, US-admin-0006). Pre-fills the edit form
 * SSR so the operator edits the current row instead of retyping the full
 * set — this REMOVES the PR-2 full-replace silent-overwrite hazard and
 * lets the provider retype modal gate on a real diff (the loaded provider
 * codes vs the entered ones). 404 → <c>countryConfiguration.notFound</c>
 * (reused code), surfaced to the page so it can fall back to the blank
 * form + warning banner (graceful). Read through
 * <c>ICountryConfigurationRepository.GetByCodeAsync</c> (AsNoTracking).
 */
export async function getCountryConfig(
  countryCode: string,
): Promise<Result<CountryConfig, ApiError>> {
  return apiFetch<CountryConfig>(
    'admin',
    `${CountryBase}/${encodeURIComponent(countryCode)}`,
    { method: 'GET' },
  );
}

// ---- Country-config update (T-0108 PUT /country-configurations/{code}) ----

/** The editable T-0108 field set + the optional provider-confirmation + mandatory reason. */
export interface UpdateCountryConfigInput {
  readonly standardVatRateBp: number;
  readonly reducedVatRateBp: number | undefined;
  readonly invoicingMode: InvoicingMode;
  readonly platformFeeRateBp: number;
  readonly defaultShippingPriceMinor: number;
  readonly defaultPaymentProvider: string;
  readonly defaultShippingCarrier: string;
  readonly defaultRegistry: string;
  readonly defaultEmailProvider: string;
  /**
   * The retyped NEW provider code (A.5). Sent ONLY when a `Default*Provider`
   * field changed; the backend gate `country.providerConfirmationMismatch`
   * stays authoritative. `undefined` for a VAT/fee-only edit.
   */
  readonly confirmedProviderCode: string | undefined;
  readonly reason: string;
}

/** Wire shape of <c>UpdateCountryConfigurationResponse</c> (carries the in-flight advisory). */
export type UpdateCountryConfigResult = Readonly<IUpdateCountryConfigurationResponse>;

/**
 * Country-config update (T-0108, US-admin-0006). The provider-confirmation
 * gate, the unregistered-code rejection (`country.providerNotRegistered`)
 * and the in-flight advisory (`inFlightOrderCount` — informational, never
 * blocking) are ALL backend. The PUT is a full-replace; the form pre-fills
 * via {@link getCountryConfig} (T-0127) so the operator edits the current
 * row, and `confirmedProviderCode` is sent ONLY when a `Default*Provider`
 * value diverges from the loaded config (a real change). Failure codes:
 * `countryConfiguration.notFound` (404),
 * `country.providerConfirmationMismatch` / `country.providerNotRegistered`.
 */
export async function updateCountryConfig(
  countryCode: string,
  input: UpdateCountryConfigInput,
): Promise<Result<UpdateCountryConfigResult, ApiError>> {
  return apiFetch<UpdateCountryConfigResult>(
    'admin',
    `${CountryBase}/${encodeURIComponent(countryCode)}`,
    {
      method: 'PUT',
      json: {
        standardVatRateBp: input.standardVatRateBp,
        reducedVatRateBp: input.reducedVatRateBp,
        invoicingMode: input.invoicingMode,
        platformFeeRateBp: input.platformFeeRateBp,
        defaultShippingPriceMinor: input.defaultShippingPriceMinor,
        defaultPaymentProvider: input.defaultPaymentProvider,
        defaultShippingCarrier: input.defaultShippingCarrier,
        defaultRegistry: input.defaultRegistry,
        defaultEmailProvider: input.defaultEmailProvider,
        confirmedProviderCode: input.confirmedProviderCode,
        reason: input.reason,
      },
    },
  );
}

// ---- Payout processing count (T-0126 GET /payout-batches/count?state=) ----

/**
 * Processing-payout count (T-0126). The dashboard-tile read — the payout
 * page now browses the LIST (see {@link getPayoutBatches}); this count
 * still backs the KPI tile. `null` on failure.
 */
export async function getProcessingPayoutsCount(): Promise<Result<number, ApiError>> {
  const result = await apiFetch<{ count: number }>(
    'admin',
    `${PayoutBase}/count?state=${encodeURIComponent(PayoutBatchState.Processing)}`,
    { method: 'GET' },
  );
  return result.success ? ok(result.value.count) : result;
}

// ---- Payout-batch LIST (T-0127 GET /payout-batches) ----

/**
 * Mirror of <c>AdminPayoutBatchListItemDto</c> (T-0127). <c>createdAt</c> /
 * <c>completedAt</c> wire-shape <c>string</c> (ISO 8601); <c>completedAt</c>
 * is absent on Processing batches.
 */
export type AdminPayoutBatch = Readonly<
  Omit<IAdminPayoutBatchListItemDto, 'createdAt' | 'completedAt'>
> & {
  readonly createdAt: string;
  readonly completedAt: string | undefined;
};

/** Generated envelope around the list page (<c>GetPayoutBatchesResponse { batches }</c>). */
interface GetPayoutBatchesEnvelope {
  readonly batches: AdminOpsPage<AdminPayoutBatch>;
}

/**
 * Payout-batch LIST (T-0127, US-admin-0007). Browsable cross-maker
 * (Unscoped) settlement list — Processing + Completed batches, `CreatedAt
 * DESC`. The GET on the existing `/payout-batches` route (the POST is
 * CreatePayoutBatch — different verb, same route). The operator browses +
 * completes/downloads CSV by visible id. Params emitted only when they
 * diverge from the backend defaults (B.8). Backend Validator authoritative.
 */
export async function getPayoutBatches(
  page: number,
  pageSize: number = ADMIN_OPS_LIST_DEFAULT_PAGE_SIZE,
): Promise<Result<AdminOpsPage<AdminPayoutBatch>, ApiError>> {
  const params = new URLSearchParams();
  if (page > 1) params.set('page', String(page));
  if (pageSize !== ADMIN_OPS_LIST_DEFAULT_PAGE_SIZE) params.set('pageSize', String(pageSize));
  const query = params.toString();
  const result = await apiFetch<GetPayoutBatchesEnvelope>(
    'admin',
    query ? `${PayoutBase}?${query}` : PayoutBase,
    { method: 'GET' },
  );
  return result.success ? ok(result.value.batches) : result;
}

// ---- Payout complete (T-0103 POST /payout-batches/{id}/complete) ----

/** Mirror of <c>MarkPayoutBatchCompletedRequest</c> (T-0103). */
export interface CompletePayoutBatchInput {
  /** Required — the bank wire reference (backend Validator authoritative). */
  readonly bankReference: string;
  /** Optional ISO `yyyy-MM-dd` from `<input type="date">`; the backend binds the date. */
  readonly paymentDate: string | undefined;
}

/** Wire shape of <c>MarkPayoutBatchCompletedResponse</c> (terminal state + settlement facts). */
export type CompletePayoutBatchResult = Readonly<IMarkPayoutBatchCompletedResponse>;

/**
 * Payout complete (T-0103, US-admin-0007). Forward-only terminal state; a
 * double-submit is a backend Silent-Success (`alreadyCompleted: true`), but
 * the UI does not invite it (disabled-while-pending). Failure codes:
 * `payoutBatch.notFound` (404), `payoutBatch.notProcessing` (409). The
 * `paymentDate` is sent only when provided (the date picker is optional).
 */
export async function completePayoutBatch(
  batchId: string,
  input: CompletePayoutBatchInput,
): Promise<Result<CompletePayoutBatchResult, ApiError>> {
  return apiFetch<CompletePayoutBatchResult>(
    'admin',
    `${PayoutBase}/${encodeURIComponent(batchId)}/complete`,
    {
      method: 'POST',
      json: {
        bankReference: input.bankReference,
        paymentDate: input.paymentDate,
      },
    },
  );
}

// ---- Payout CSV download (T-0103 GET /payout-batches/{id}/csv) ----

/**
 * Operator bank-file CSV download (T-0103, A.4). Deliberately does NOT use
 * the generated <c>AdminApi.csv</c> method: NSwag types the file response
 * as <c>Promise&lt;void&gt;</c> and discards the body (`csv()` does
 * `response.text()` → `return;`). Fetching as a blob through
 * <c>apiFetch</c> keeps the audience cookie, the timeout budget and RFC7807
 * parsing; the caller turns the Blob into a named download. This is the
 * cross-maker bank file — admin/operator-only, INVERTING the T-0116 maker
 * absence (the maker must never see cross-maker PII; the admin IS the
 * operator who runs the wire). Failure codes: `payoutBatch.notFound` (404),
 * `payoutBatch.csvNotReady` (409).
 */
export async function downloadPayoutCsv(batchId: string): Promise<Result<Blob, ApiError>> {
  return apiFetch<Blob>('admin', `${PayoutBase}/${encodeURIComponent(batchId)}/csv`, {
    method: 'GET',
    parse: 'blob',
    timeoutMs: DOWNLOAD_TIMEOUT_MS,
  });
}

// ---- Admin invoice PDF download (T-0126 GET /admin-invoices/{id}/pdf) ----

/**
 * Admin invoice PDF download (T-0126-backed — the endpoint that T-0118a
 * shipped as a disabled button now exists). Same blob discipline as the
 * payout CSV / maker fee-invoice: never the generated `pdf()` method (it
 * discards the body), always `apiFetch` `parse: 'blob'`. Failure 404 for an
 * unknown invoice id.
 */
export async function downloadAdminInvoice(invoiceId: string): Promise<Result<Blob, ApiError>> {
  return apiFetch<Blob>('admin', `${InvoicesBase}/${encodeURIComponent(invoiceId)}/pdf`, {
    method: 'GET',
    parse: 'blob',
    timeoutMs: DOWNLOAD_TIMEOUT_MS,
  });
}

// ---- Maker fee-rate override (T-0140 POST /makers/{id}/fee-override) ----

/**
 * Mirror of the backend <c>MakersController.SetMakerFeeOverrideRequest</c>
 * (T-0140, US-admin-0018). <c>feeRateOverrideBp</c> is <c>null</c> to clear
 * the override (reverts pricing to the country default); a non-null value
 * sets it. The discount-only ceiling (must not exceed the maker's
 * <c>CountryConfiguration.PlatformFeeRateBp</c>) and the non-negative floor
 * are backend-authoritative (<c>SetMakerFeeOverride.Validator</c> /
 * <c>Handler</c>) — this input type carries whatever the caller already
 * validated client-side as UX-only defense in depth.
 */
export interface SetMakerFeeOverrideInput {
  readonly feeRateOverrideBp: number | null;
  /** Mandatory audit-log note (&le; 2000 chars, backend authoritative). */
  readonly reason: string;
}

/**
 * Set or clear a maker's per-maker loyalty fee-rate override (T-0140). The
 * endpoint returns <c>200 OK</c> with an empty body (no DTO — the resolved
 * state isn't echoed back because there is, as of this ticket, no admin
 * maker-detail READ endpoint to refresh from; see the read-gap note on the
 * admin maker page). Failure codes: `maker.notFound` (404),
 * `maker.feeOverrideExceedsCountryDefault` / `error.validation` (400/422),
 * `error.unauthorized` (401, fail-closed per AC-7).
 */
export async function setMakerFeeOverride(
  makerId: string,
  input: SetMakerFeeOverrideInput,
): Promise<Result<void, ApiError>> {
  return apiFetch<void>('admin', `${MakersBase}/${encodeURIComponent(makerId)}/fee-override`, {
    method: 'POST',
    json: { feeRateOverrideBp: input.feeRateOverrideBp, reason: input.reason },
  });
}

// ---- GDPR erase (T-0110 POST /users/{id}/erase) ----

/** Mirror of <c>EraseUserRequest</c> (T-0110). */
export interface EraseUserInput {
  /** The retyped exact user email (A.1). The backend re-checks it (`user.deleteConfirmationMismatch`). */
  readonly confirmedEmail: string;
  /** Mandatory GDPR reason / ticket ref (&le; 2000 chars, backend authoritative). */
  readonly reason: string;
}

/** Wire shape of <c>DeleteUserPermanentlyResponse</c> (the erased user id). */
export type EraseUserResult = Readonly<IDeleteUserPermanentlyResponse>;

/**
 * GDPR hard-delete (T-0110, US-admin-0016) — the only irreversible op the
 * platform fronts with a UI. The DOUBLE INTERLOCK is server-enforced: the
 * email-retype (`user.deleteConfirmationMismatch`) AND the in-flight-order
 * block (`user.cannotDeleteWithInFlightOrders` when any order is in
 * PendingPayment/Paid/Accepted/Shipped). A re-call after a successful erase
 * returns `user.notFound` (NOT a silent success — T-0110's no-Silent-Success
 * rule). The UI adds friction and surfaces the verdict; the SERVER decides.
 */
export async function eraseUser(
  userId: string,
  input: EraseUserInput,
): Promise<Result<EraseUserResult, ApiError>> {
  return apiFetch<EraseUserResult>('admin', `${UsersBase}/${encodeURIComponent(userId)}/erase`, {
    method: 'POST',
    json: { confirmedEmail: input.confirmedEmail, reason: input.reason },
  });
}

// ---- Platform revenue (T-0186 GET /platform-revenue?window=) ----

/**
 * Mirror of <c>GetPlatformRevenueResponse</c> (T-0186). The two instants
 * arrive as ISO 8601 strings on the wire; every amount is minor units
 * (<c>long</c> server-side) and is rendered with <c>formatCzk</c> — the
 * frontend does no money arithmetic of its own (CLAUDE.md §4).
 */
export type PlatformRevenue = Readonly<
  Omit<IGetPlatformRevenueResponse, 'fromInclusive' | 'toExclusive'>
> & {
  readonly fromInclusive: string;
  readonly toExclusive: string;
};

/**
 * Platform earnings over a rolling window (T-0186) — backs the admin
 * overview's earnings panel. `null` on failure so the panel degrades to
 * "—" like the KPI tiles rather than throwing the whole overview
 * (AC-4 precedent).
 *
 * The window enum is the ONLY input, and the backend Validator rejects
 * anything outside it — the caller cannot widen the reporting period by
 * hand-crafting a query string.
 */
export async function getPlatformRevenue(
  window: RevenueWindow,
): Promise<Result<PlatformRevenue, ApiError>> {
  return apiFetch<PlatformRevenue>(
    'admin',
    `${RevenueBase}?window=${encodeURIComponent(window)}`,
    { method: 'GET' },
  );
}
