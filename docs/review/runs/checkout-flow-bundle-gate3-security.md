# Gate 3 (Security) — checkout-flow-bundle

- **Branch:** `feat/checkout-flow-bundle` (5 commits, 27 files, +2703)
- **Date:** 2026-06-10
- **Reviewer:** Security & DevOps agent
- **Trigger:** Insurance pass — tickets marked `security_touching: false`, but bundle is the revenue path (payment redirect handling, third-party script, browser uploads)

## Verdict: GATE3_FOLD

Nothing **in this diff** fails. One pre-existing Medium finding (open redirect in `login-form.tsx`) is newly exercised by three `?redirect=` producers this bundle adds — fold the 5-line consumer-side guard into this PR (or PM explicitly defers with a ticket before merge).

## Findings

| # | Sev | Where | Finding |
|---|-----|-------|---------|
| F1 | **Medium (pre-existing)** | `frontend/src/app/(auth)/login/login-form.tsx:24,39` | `const redirectTo = searchParams.get('redirect') ?? '/'` → `router.push(redirectTo)` with **no path validation**. Accepts absolute (`https://evil.com`) and protocol-relative (`//evil.com`) values → post-login open redirect: victim authenticates on the genuine Makables login page, then lands on an attacker site (credential re-phish / fake "payment failed" page). Not introduced by this bundle, but this bundle adds 3 new `?redirect=` producers (checkout page, order page, confirmation page) that normalize the pattern. **Fix:** accept only values matching `^\/(?!\/)` (single leading slash, not `//`), else fall back to `/`. |
| F2 | Low | `backend/.../Packeta/PacketaOptionsValidator.cs:31-35` + `frontend/src/components/shared/zasilkovna-widget.tsx` | Widget `scriptUrl` is https-pinned (absolute https enforced at backend startup) but **not host-pinned**. A compromised `Packeta:WidgetScriptUrl` config value = arbitrary https script injected into the checkout page (XSS on the revenue path). Acceptable trust posture (config compromise implies broader compromise; SRI not feasible for the dynamic Packeta widget), but cheap hardening: allowlist `widget.packeta.com` in the validator. Follow-up, not a fold. |
| F3 | Info (pre-existing) | `login-form.tsx:90-101` | `mapLoginError` default branch returns the raw backend `error.message` to the UI — contradicts the bundle's own `resolveErrorMessage` posture ("never the raw `error.message`"). Cosmetic/consistency; fold into the F1 touch if convenient. |

## Checklist results

1. **CLAUDE.md payments rule — PASS.** `potvrzeni/page.tsx`: success is granted exclusively by backend state (`isPaidOrLater(detail.state)` from the SSR fetch). `?status=` is consumed only as a failure short-circuit against a closed set (`cancelled|cancel|failed|error`). Craft test `?status=paid` on an unpaid order: "paid" is not in `FAILURE_STATUS_VALUES` → row 6 → verifying frame + poller → success only when the backend reads `Paid`. The poller (`payment-poll-client.tsx`) likewise grants success only from `getCustomerOrderDetail` state; cap-reached falls back to the order-paid email. No trust in redirect params anywhere.
2. **No secrets in client bundle — PASS.** Zero `process.env` references in the diff; zero `dangerouslySetInnerHTML`/`innerHTML`/`eval`. Packeta `publicKey` comes from the anonymous `/api/v1/public/shipping/widget-config` endpoint backed by `PacketaOptions.PublicWidgetKey` (designed public-safe); the private `ApiKey` (`apiPassword`) never leaves `Infra.Clients/Packeta`.
3. **Third-party script provenance — PASS with F2 noted.** `script.src = scriptUrl` where scriptUrl flows backend config → startup-validated (absolute https) → anonymous config endpoint → SSR prop. No arbitrary-URL injection path from user input; host not pinned (F2). Lazy injection on click, load/error handlers, no SRI (expected for Packeta v6).
4. **Upload surface — PASS.** Client type/size/count checks in `attachment-picker.tsx` / `attachment-manager-client.tsx` are documented UX mirrors of the T-0064 server validator (`file.tooLarge`, `file.unsupportedType`, `order.attachmentLimitReached` remain authoritative). Filenames render as React text nodes (auto-escaped) — no XSS via filename. `attachment.downloadUrl` is a backend-built **relative path on the audience host** (`OrderAttachmentSummaryDto`) — complies with the no-direct-blob-links rule; the `href` is backend-controlled, never derived from the filename.
5. **Redirect targets — PASS in-diff; F1 pre-existing.** `window.location.assign(result.value.redirectUrl)` consumes the authed payment-session response — backend-trust, acceptable. All three new `?redirect=` producers encode path-only, self-built targets. The consumer (`login-form.tsx`) is the unvalidated link → F1.
6. **PII rendering — PASS.** Contact snapshot (`contactName`, `contactPhone`) renders only on the owner-scoped order page; backend 404s foreign orders identically to unknown ids (IDOR predicate, US-customer-0012 AC-3). No PII in URLs (only opaque `orderId`/`productId`); no `console.*`/logger calls in the diff; `defaultEmail` prop is the customer's own session profile.
7. **`?attachmentsFailed` — PASS.** `Number.parseInt` + `Number.isFinite` + `> 0` gate; rendered through i18n interpolation as a text node. No reflected injection.
8. **Poller enumeration — PASS.** Polls the customer's own order via the authed detail endpoint; foreign and unknown ids 404 identically, so the poller has zero enumeration value. Capped (~30s), visibility-paused, in-flight-guarded — no unbounded request amplification.

## Required fold (F1)

In `login-form.tsx`, replace line 24 with a path-validated consumption, e.g.:

```ts
const rawRedirect = searchParams.get('redirect') ?? '/';
const redirectTo = /^\/(?!\/)/.test(rawRedirect) ? rawRedirect : '/';
```

## Follow-ups (ticket, not fold)

- F2: host-allowlist `Packeta:WidgetScriptUrl` to `widget.packeta.com` in `PacketaOptionsValidator`.
- F3: route `mapLoginError` default through `resolveErrorMessage` fallback copy.
