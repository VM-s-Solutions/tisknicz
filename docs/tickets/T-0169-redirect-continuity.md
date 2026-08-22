---
id: T-0169
title: "Redirect continuity sweep: returnUrl preserved end-to-end, audience-aware landing, shared 401 navigation"
status: in_review
size: M
owner:
created: 2026-08-21
updated: 2026-08-21
depends_on: [T-0166]
blocks: []
user_stories: [US-customer-0001, US-customer-0002, US-customer-0016, US-maker-0002]
adrs: [0012]
phase: 8
manual_steps: []
security_touching: false
layers: [frontend]
---

# T-0169 — Redirect continuity sweep

## Context
Audit findings [AUTH-M3, AUTH-M4, AUTH-M5, AUTH-L2, AUTH-L3, PUB-L7, CUST-M6](../review/ux-functional-audit-2026-08-21.md).
The `?redirect=` contract leaks everywhere: middleware drops the query string, login ignores
`continueHref` so a wrong-audience redirect bounces the fresh session onto the "Už jste přihlášeni"
panel, register/verify/magic funnels drop the param entirely, admins on `/login` get unmapped copy
with no pointer to `/admin/login`, and terminal client-side 401s show text with no way to log in.

## Scope
- Middleware + the orders-list SSR redirect include `request.nextUrl.search` in the `redirect` param.
- Login success routes through `continueHref(audience, safeRedirect)` — wrong-audience targets land
  on the audience home instead of bouncing.
- Thread `?redirect=` through: login→register links, register→login, verify success → `/login?redirect=…`,
  magic request payload + consume landing (replace the hardcoded `router.replace('/')`).
- Dual-host `auth.forbidden` on `/login` maps to dedicated copy linking `/admin/login`.
- Shared `redirectToLogin(currentPath)` helper for terminal `Unauthorized` in client components;
  existing per-callsite handlers converge on it.
- `/admin/login`: already-signed-in handling parity + `router.refresh()` after login (AUTH-L3).
- `safe-redirect` tests extended for the new composition; open-redirect guard stays authoritative.

## Alternatives Considered
- **Global apiFetch hook auto-navigating on 401** — rejected: hidden navigation from a data helper
  is surprising; an explicit shared helper keeps call sites readable.
- **Encoding returnUrl in OAuth/magic state server-side** — deferred; frontend threading covers the
  audited losses without a contract change.

## Out of scope
- OAuth callback landing (T-0167). Profile-page failure states (T-0173).

## Acceptance criteria
- **AC-1** Given a logged-out visit to `/dashboard/zakaznik/objednavky?state=Shipped&page=3`, when
  the user logs in, then they land on exactly that URL (manual proof + vitest on the composer).
- **AC-2** Given a customer logging in with `?redirect=/dashboard/maker/...`, when login succeeds,
  then they land on the customer home — never on the AlreadySignedIn panel.
- **AC-3** Given an admin with correct credentials on `/login`, when both hosts return
  `auth.forbidden`, then the error copy names admin accounts and links `/admin/login`.
- **AC-4** Given a logged-out user bounced from a protected page who registers (or uses a magic
  link), when the funnel completes, then they return to the original page.
- **AC-5** `safeRedirect` still rejects absolute/protocol-relative URLs (regression tests).

## Technical notes
`middleware.ts:168-171`, `login-form.tsx:55` (`continueHref` at `route-audience.ts:76-80`),
`magic-client.tsx:94`. Keep the redirect param a single URL-encoded value (path + search).

## Files touched (expected)
- `frontend/src/middleware.ts`
- `frontend/src/app/(auth)/**` (login, register, verify, reset, magic)
- `frontend/src/app/(admin)/admin/login/*`
- `frontend/src/lib/auth/{safe-redirect.ts,route-audience.ts}` (+ tests)
- `frontend/src/lib/runtime/` (shared 401 navigation helper)

## Test plan reference
`docs/test-plans/T-0169.md`

## Status log
- 2026-08-21 `draft → ready` (Phase 8 UX sweep plan; DoR checklist passed)
- 2026-08-22 `ready → in_progress`, branch `feat/T-0169-redirect-continuity`
- 2026-08-22 `in_progress → in_review` — tsc clean, vitest 213/213 (+7 new); surfaced and fixed a
  null `useSearchParams()` crash path; admin already-signed-in panel deferred with rationale in the
  [test plan](../test-plans/T-0169.md); see [review run](../review/runs/T-0169.md)
