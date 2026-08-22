---
id: T-0167
title: Google OAuth callback must land the user in the app, not on raw JSON
status: in_review
size: M
owner:
created: 2026-08-21
updated: 2026-08-21
depends_on: []
blocks: []
user_stories: [US-customer-0004]
adrs: [0012]
phase: 8
manual_steps: [nswag-regen]
security_touching: true
layers: [dotnet-backend, frontend, l10n, secops]
---

# T-0167 — Google OAuth callback lands in the app

## Context
Audit finding [AUTH-H2](../review/ux-functional-audit-2026-08-21.md): `google/callback` ends with
`HandleResult(result)` — a JSON body on the API host. After authenticating at Google the user is
stranded on raw JSON: on success cookies are set but they must manually type the site URL; on
failure (maker account on the hardcoded customer host, unverified Google email, expired state)
they see a bare JSON error. T-0035 deferred the frontend callback UX and it was never built.

## Scope
- Backend: `google/callback` returns `302` — success → `{WebBaseUrl}` post-login landing for the
  audience (reuse the audience-home mapping), failure → `{WebBaseUrl}/login?oauth_error={code}`.
  Cookie issuance unchanged. `[ProducesResponseType]` updated.
- Frontend: `/login` reads `oauth_error` and renders mapped Czech copy per code (incl. the
  maker-account-on-customer-host `auth.forbidden` case, with a hint to use password/magic login).
- cs-CZ keys for each surfaced code; error-code list pinned by a test.
- Integration tests: success 302 + cookies; failure 302 with the code in the query; no token or
  PII in the redirect URL.
- NSwag regen (spec annotations change).

## Alternatives Considered
- **Frontend callback page completing the exchange client-side** — rejected: the code→cookie
  exchange must stay server-side (HttpOnly cookies); a 302 from the API is the minimal correct shape.
- **Success → per-audience dashboard deep link** — kept simple: audience home; deep-link return
  can ride the `state` payload later.

## Out of scope
- Apple Sign-In (UI removed in T-0152; backend endpoints unreachable).
- Offering Google on the maker host (business decision; today's button stays customer-only).

## Acceptance criteria
- **AC-1** Given a successful Google exchange, when the callback completes, then the browser is
  redirected to the frontend with session cookies set (integration test asserts 302 + Set-Cookie;
  manual proof: full browser round trip lands logged-in).
- **AC-2** Given an expired/invalid `state`, when the callback completes, then the browser lands on
  `/login?oauth_error=<code>` and the page shows mapped Czech copy (not fallback text).
- **AC-3** Given a maker's Google account, when the customer-host flow rejects it, then the login
  page explains the account type and offers password/magic login.
- **AC-4** No access/refresh token, email or other PII appears in any redirect URL (test).

## Technical notes
`backend/src/Makables.Config/Controllers/Auth/AuthController.cs:230-256`. Redirect target must be
built from `PublicAppUrlsOptions.WebBaseUrl` — never from request headers (open-redirect hygiene).
Frontend mapping mirrors `mapStartGoogleOAuthError`.

## Files touched (expected)
- `backend/src/Makables.Config/Controllers/Auth/AuthController.cs` (+ integration tests)
- `frontend/src/app/(auth)/login/page.tsx`, `login-form.tsx`
- `frontend/src/lib/i18n/cs-CZ.ts`
- `frontend/src/lib/api-client/*` (regen)

## Test plan reference
`docs/test-plans/T-0167.md`

## Status log
- 2026-08-21 `draft → ready` (Phase 8 UX sweep plan; DoR checklist passed)
- 2026-08-22 `ready → in_progress`, branch `feat/auth-recovery-bundle` (bundled with the sibling
  auth ticket — one PR, secops-hardening-bundle precedent)
- 2026-08-22 `in_progress → in_review` — backend 2082/2082, frontend tsc clean + vitest 199/199
  (+6 new); NSwag regenerated for the new resend endpoint; see
  [test plan](../test-plans/T-0167-T-0168.md) + [review run](../review/runs/T-0167-T-0168.md)
