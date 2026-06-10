/**
 * Hand-written wrapper around the authenticated payment-session
 * endpoint in .NET <c>OrdersController</c> at
 * <c>/api/v1/orders/{orderId}/payment-session</c> on the Customer host
 * (T-0065). Same convention as <c>orders-client.ts</c> (patterns.md
 * B.16).
 *
 * The backend handler owns verify-then-recreate + the 24h-cached
 * <c>PaymentRedirectUrl</c> — the frontend's only job is: call on
 * explicit click (T-0084b Q3 lock — never on page load), then perform a
 * full document navigation to the returned Comgate URL.
 */

import type { ICreatePaymentSessionResponse } from '../api-client/customer-api.v1';
import { apiFetch } from '../runtime/api-fetch';
import type { ApiError, Result } from '../runtime/result';

/** Mirror of <c>CreatePaymentSessionResponse</c> — `{ paymentProviderRef, redirectUrl }`. */
export type PaymentSession = Readonly<ICreatePaymentSessionResponse>;

/**
 * Create (or re-serve) the Comgate payment session for an order.
 * Failure codes from <c>BusinessErrorMessage</c>:
 * <c>payment.providerUnavailable</c>, <c>payment.providerRejected</c>,
 * <c>payment.providerMisconfigured</c>,
 * <c>payment.providerNotRegistered</c>, <c>payment.unknownError</c>,
 * <c>order.invalidStateForPayment</c>, <c>order.paymentAlreadyCaptured</c>.
 */
export async function createPaymentSession(
  orderId: string,
): Promise<Result<PaymentSession, ApiError>> {
  return apiFetch<PaymentSession>(
    'customer',
    `/api/v1/orders/${encodeURIComponent(orderId)}/payment-session`,
    { method: 'POST' },
  );
}
