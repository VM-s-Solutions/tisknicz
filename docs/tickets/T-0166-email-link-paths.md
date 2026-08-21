---
id: T-0166
title: Fix dead transactional email link paths (confirm / magic / reset all 404)
status: ready
size: S
owner:
created: 2026-08-21
updated: 2026-08-21
depends_on: []
blocks: [T-0168]
user_stories: [US-customer-0003, US-customer-0005, US-customer-0006]
adrs: [0012, 0019]
phase: 8
manual_steps: []
security_touching: false
layers: [dotnet-backend, frontend]
---

# T-0166 — Fix dead transactional email link paths

## Context
Audit finding [AUTH-H1](../review/ux-functional-audit-2026-08-21.md): `PublicAppUrlsOptions`
defaults compose email links as `/auth/confirm`, `/auth/magic`, `/auth/reset`, but the `(auth)`
route group adds no URL segment — the real routes are `/verify`, `/magic`, `/reset`. Only
`WebBaseUrl` is overridden in any environment, so **every** email-confirmation, magic-link and
password-reset round trip 404s in every environment. Masked locally because dev never sends real
emails. Highest-severity finding of the Phase 8 sweep — registration→activation is broken end-to-end.

## Scope
- `PublicAppUrlsOptions` defaults become `EmailConfirmationPath = "/verify?token={token}"`,
  `MagicLinkPath = "/magic?token={token}"`, `PasswordResetPath = "/reset?token={token}"`.
- Unit tests pinning the three composed absolute URLs (catch any future drift).
- `frontend/next.config.ts` gains permanent redirects `/auth/verify → /verify`, `/auth/confirm →
  /verify`, `/auth/magic → /magic`, `/auth/reset → /reset` (query preserved) — belt-and-braces for
  already-sent emails.
- Grep sweep: no other composer builds `/auth/*` frontend URLs (Q-0013 swept `<Link>`s only).

## Alternatives Considered
- **Rename the route group so `/auth/*` is real** — rejected: URL churn on live routes; Q-0013
  already standardized on the un-prefixed paths.
- **Override paths per environment in appsettings/Bicep** — rejected: leaves broken defaults as a
  trap; code defaults must be correct.

## Out of scope
- Any change to token issuing/consumption (T-0168 owns the recovery UX).

## Acceptance criteria
- **AC-1** Given a registration, when the confirmation email payload is composed, then the action
  URL is `{WebBaseUrl}/verify?token={token}` (unit test asserts all three composed URLs).
- **AC-2** Given an already-sent email containing `/auth/confirm?token=x`, when opened, then the
  browser lands on `/verify?token=x` (redirect test; token query preserved).
- **AC-3** Given a real local round trip (mint token per the dev recipe), when the emailed URL is
  opened, then the confirm page loads and the account is confirmed (manual proof in the running app).

## Technical notes
Backend: `Makables.Core.AppServices/Common/PublicAppUrlsOptions.cs:28-34`. The leaf name differs
too (`confirm` vs `verify`) — do not "fix" by renaming the frontend route. Frontend redirects go in
`next.config.ts` `redirects()` (none exist today).

## Files touched (expected)
- `backend/src/Makables.Core.AppServices/Common/PublicAppUrlsOptions.cs`
- `backend/tests/Makables.Tests/**` (options URL-composition tests)
- `frontend/next.config.ts`

## Test plan reference
`docs/test-plans/T-0166.md`

## Status log
- 2026-08-21 `draft → ready` (Phase 8 UX sweep plan; DoR checklist passed)
