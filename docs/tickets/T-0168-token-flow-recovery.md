---
id: T-0168
title: "Recovery paths for token flows: magic-link makers, verify double-fire, resend confirmation, reset re-request"
status: in_review
size: M
owner:
created: 2026-08-21
updated: 2026-08-21
depends_on: [T-0166]
blocks: []
user_stories: [US-customer-0003, US-customer-0005, US-customer-0006, US-maker-0002]
adrs: [0012, 0019]
phase: 8
manual_steps: [nswag-regen]
security_touching: true
layers: [dotnet-backend, frontend, l10n, secops]
---

# T-0168 — Recovery paths for token flows

## Context
Audit findings [AUTH-H3, AUTH-M1, AUTH-M2, AUTH-M6, AUTH-L4, CUST-H2](../review/ux-functional-audit-2026-08-21.md).
Every token flow currently has at least one dead end: makers can't consume magic links (customer
host hardcoded), a double-fired/refreshed verify burns the one-time token and shows failure to a
user who IS confirmed, a logged-out user with `auth.emailNotConfirmed` has no resend path anywhere
(the `EmailConfirmationBanner` that implements resend is mounted nowhere), and a burned reset token
renders above a form that can never succeed.

## Scope
- **Magic link (maker):** consume retries the maker host on `auth.forbidden` (mirror LoginForm's
  dual-host fallback — token is deliberately not burned on audience mismatch); map the code to real
  copy; failure card gains "Poslat nový odkaz" + login links.
- **Verify:** double-fire guard (ref) on the mount POST; backend `ConfirmEmail` becomes idempotent —
  a token consumed within the last 24 h whose user is already confirmed returns Success (test);
  failure card gains login link + resend affordance (uses the unused `auth.verify.resend` copy).
- **Resend for logged-out users:** new anonymous `POST /api/v1/auth/resend-confirmation` (uniform
  response regardless of account existence, `"auth"` rate-limit policy per T-0136); login's
  `auth.emailNotConfirmed` error and the register success screen both offer it.
- **Mount `EmailConfirmationBanner`:** customer profile page gated on `emailConfirmed === false`;
  checkout's unconfirmed-email notice links there (fixes the "resend from your profile" copy that
  points at nothing). Banner failure state surfaced (AUTH-L4).
- **Reset:** on `auth.passwordResetInvalid`, swap in a "Vyžádat nový odkaz" link to request mode.
- cs-CZ keys for all new copy; NSwag regen for the new endpoint.

## Alternatives Considered
- **Frontend-only verify fix (guard, no backend idempotency)** — rejected: mail-scanner prefetch
  and genuine re-clicks still strand confirmed users; the 24 h consumed-token grace closes it.
- **Make magic-link issue audience-scoped tokens** — rejected: consume-side host fallback is
  smaller and matches the shipped login pattern.

## Out of scope
- Login/redirect continuity (T-0169). Email HTML wrapper (T-0157 follow-up).

## Acceptance criteria
- **AC-1** Given a maker's magic link, when consumed on `/magic`, then the maker lands logged-in on
  their dashboard (integration + manual proof), with no visible error.
- **AC-2** Given a confirmation link opened twice (or StrictMode dev), when the second confirm runs,
  then the page shows success, not "Odkaz je neplatný" (backend idempotency test + vitest guard test).
- **AC-3** Given a logged-out user whose login fails with `auth.emailNotConfirmed`, when they use the
  offered resend action, then the request returns the uniform success copy and (for a real account)
  a new confirmation email is enqueued (integration test; account-enumeration test: unknown email →
  identical response + no outbox row).
- **AC-4** Given a customer profile with unconfirmed email, when the page renders, then the resend
  banner is visible and a failed resend shows an error message (vitest).
- **AC-5** Given a burned reset token, when the confirm fails, then a "Vyžádat nový odkaz" link is
  offered and leads to the request form.
- **AC-6** Every new `BusinessErrorMessage` code (if any) has a cs-CZ key and a triggering test.

## Technical notes
Rate-limit + uniform-failure rules per [security-rules](../../agents/knowledge/security-rules.md);
resend endpoint mirrors the shape of `request-password-reset` (T-0035). ConsumeMagicLink audience
check: `ConsumeMagicLink.cs:90-98`. Verify client: `verify-client.tsx:27-38`.

## Files touched (expected)
- `backend/src/Makables.Core.AppServices/Features/Auth/{ConfirmEmail,ResendConfirmation}.cs` (+ tests)
- `backend/src/Makables.Config/Controllers/Auth/AuthController.cs`
- `frontend/src/app/(auth)/{magic/magic-client.tsx,verify/verify-client.tsx,reset/reset-client.tsx,login/login-form.tsx,register/register-form.tsx}`
- `frontend/src/components/shared/email-confirmation-banner.tsx`
- `frontend/src/app/(customer)/dashboard/zakaznik/profile/*`, `frontend/src/app/(customer)/objednavka/order-form-client.tsx`
- `frontend/src/lib/i18n/cs-CZ.ts`, `frontend/src/lib/api-client/*` (regen)

## Test plan reference
`docs/test-plans/T-0168.md`

## Status log
- 2026-08-21 `draft → ready` (Phase 8 UX sweep plan; DoR checklist passed)
- 2026-08-22 `ready → in_progress`, branch `feat/auth-recovery-bundle` (bundled with the sibling
  auth ticket — one PR, secops-hardening-bundle precedent)
- 2026-08-22 `in_progress → in_review` — backend 2082/2082, frontend tsc clean + vitest 199/199
  (+6 new); NSwag regenerated for the new resend endpoint; see
  [test plan](../test-plans/T-0167-T-0168.md) + [review run](../review/runs/T-0167-T-0168.md)
