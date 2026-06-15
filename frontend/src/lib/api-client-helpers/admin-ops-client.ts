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
 * CONTRACT GAPS (flagged in the ticket follow-up, NOT papered over here):
 *   - No outbox-event LIST read exists (only `count()` for the stalled
 *     total). The outbox triage page therefore renders the stalled count +
 *     a by-id action surface; a thin list endpoint is the clean fix.
 *   - No payout-batch LIST read exists (the `payoutBatches()` generated
 *     method is the POST that CREATES a batch — A.3 forbids calling it from
 *     this UI; `count2()` is the processing count). The payout page renders
 *     the processing count + a by-id complete/CSV surface; a list endpoint
 *     is the clean fix.
 *   - No country-config GET exists (only the PUT). The form ships without a
 *     server pre-fill (operator enters the full editable set); a GET is the
 *     clean fix.
 *   - No user-lookup / per-user-order read exists (only `erase`). The
 *     delete-user screen surfaces the in-flight block as the backend's
 *     post-call verdict (`user.cannotDeleteWithInFlightOrders`) rather than
 *     pre-disabling; a per-user read is the clean fix.
 */

import {
  InvoicingMode,
  type IAcknowledgeOutboxEventResponse,
  type IDeleteUserPermanentlyResponse,
  type IMarkPayoutBatchCompletedResponse,
  type IRetryOutboxEventResponse,
  type IUpdateCountryConfigurationResponse,
  PayoutBatchState,
} from '../api-client/admin-api.v1';
import { apiFetch } from '../runtime/api-fetch';
import { type ApiError, type Result, ok } from '../runtime/result';

// Re-export the enums so route code never imports `lib/api-client/` directly.
export { InvoicingMode, PayoutBatchState };

// Leading slash matters: apiFetch concatenates `${baseUrl}${path}` against
// host URLs with no trailing slash (admin-client.ts precedent).
const OutboxBase = '/api/v1/outbox-events';
const CountryBase = '/api/v1/country-configurations';
const PayoutBase = '/api/v1/payout-batches';
const UsersBase = '/api/v1/users';
const InvoicesBase = '/api/v1/admin-invoices';

/**
 * Streaming-download budget (payouts-client.ts precedent): the operator
 * CSV bank file + the invoice PDF share the 120 s ceiling so a slow
 * downlink doesn't trip the 8 s JSON default.
 */
const DOWNLOAD_TIMEOUT_MS = 120_000;

// ---- Outbox count (T-0126 GET /outbox-events/stalled/count) ----

/**
 * Stalled-outbox count (T-0126). The ONLY outbox read on the contract —
 * there is no event LIST endpoint, so the triage page renders this count +
 * a by-id action surface (gap flagged). `null` on failure so the page
 * degrades gracefully.
 */
export async function getStalledOutboxCount(): Promise<Result<number, ApiError>> {
  const result = await apiFetch<{ count: number }>('admin', `${OutboxBase}/stalled/count`, {
    method: 'GET',
  });
  return result.success ? ok(result.value.count) : result;
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
 * blocking) are ALL backend. There is NO GET on the contract, so the form
 * has no server pre-fill (gap flagged). Failure codes:
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
 * Processing-payout count (T-0126). The only payout READ on the contract
 * (the `payoutBatches()` generated method is the CREATE POST — A.3 forbids
 * it here; there is no LIST read). The payout page renders this count + a
 * by-id complete/CSV surface (gap flagged). `null` on failure.
 */
export async function getProcessingPayoutsCount(): Promise<Result<number, ApiError>> {
  const result = await apiFetch<{ count: number }>(
    'admin',
    `${PayoutBase}/count?state=${encodeURIComponent(PayoutBatchState.Processing)}`,
    { method: 'GET' },
  );
  return result.success ? ok(result.value.count) : result;
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
