# Webhook verification

Every webhook endpoint must verify the caller before processing.

## Comgate
- Verify source IP is in Comgate's allowlist (documented in their portal), via
  `Comgate:WebhookAllowedIps`. Fail-closed: an empty list rejects everything.
  - The address compared is `Connection.RemoteIpAddress` **after**
    `UseMakablesForwardedHeaders` has rewritten it from `X-Forwarded-For`
    (`ForwardedHeaders:Enabled`, set in deployed environments). Without that
    middleware the filter sees the App Service front end and rejects every
    callback; with `ForwardLimit = 1` only the hop the front end itself
    appended is trusted, so a forged header cannot get past it.
  - The notification URL must point at the **public API host directly**. Through
    the frontend's `/api-proxy` rewrite the last hop is the frontend egress IP —
    a permanent 401. Fix the URL; never add that egress IP to the allowlist.
- Re-fetch payment status via Comgate API (`GET /v1.0/status`) — never trust the body alone
- Idempotency: if order is already `paid`, return 200 without re-running side effects

## Zásilkovna / Packeta
- Verify source IP if applicable
- Verify signature if provided
- Re-fetch packet status via API on any suspicious payload

## Vercel Cron
- Verify `Authorization: Bearer ${CRON_SECRET}` header
- Reject all other callers with 401

## General rules
- Never log secrets
- Return 2xx only after successful side effects (Comgate retries on non-2xx)
- Log every incoming webhook with request id for audit
- Rate-limit unauthenticated callers
