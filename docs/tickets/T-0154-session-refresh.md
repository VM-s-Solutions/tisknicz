---
id: T-0154
title: Session refresh — frontend never called /auth/refresh, sessions evaporated after 15 minutes
status: in_review
size: M
owner: frontend
created: 2026-07-23
updated: 2026-07-23
depends_on: [T-0022, T-0027, T-0035, T-0152, T-0153]
blocks: []
user_stories: [US-customer-0002, US-maker-0002]
adrs: [0012, 0005]
phase: 7
manual_steps: []
security_touching: true
layers: [frontend]
---

# T-0154 — Session refresh (frontend "does not hold logged in state")

## Context

Operator-reported during the T-0153 walk: the frontend does not hold the
logged-in state. Root cause: the access JWT and its cookie live 15 minutes
(`JwtOptions.AccessTokenLifetime`; `AuthCookies` expires the cookie with the
JWT), the refresh cookie lives for the rotated-family lifetime — and
**nothing on the frontend ever called `/api/v1/auth/refresh`**. The
`refresh()` helper shipped in T-0035 with zero call sites; `api-fetch.ts`
carried a comment deferring the bridge "once real JWT refresh exists". Every
session silently evaporated within 15 minutes: navbar reverted to logged-out,
middleware bounced dashboards to /login, client API calls 401ed.

## Scope

- **`lib/auth/jwt-expiry.ts`** (new): edge-safe JWT payload decode
  (`atob` + `TextDecoder`, no `Buffer`) + `isJwtExpiredOrInvalid` with a 15 s
  skew (a token expiring mid-request counts as expired).
  `display-session.ts` refactored onto the shared decoder.
- **Middleware session refresh** (`middleware.ts`, matcher widened from
  dashboards-only to every page/RSC request): per audience
  (customer/maker/admin), when the access cookie is missing/expired and the
  refresh cookie is present, call the audience host's refresh endpoint
  server-side (cookie-in/Set-Cookie-out, `[DisableRateLimiting]` on the
  backend precisely for this), forward the rotated cookies to the browser
  AND patch them into the current request so the very first render already
  sees the fresh session. Backend-unreachable → cookies left untouched (a
  blip must not log anyone out). The dashboard guard now runs AFTER the
  refresh attempt.
- **`apiFetch` browser-side 401 → refresh → retry-once**: long-lived pages
  (order-detail polling) recover mid-session; auth endpoints excluded (no
  loops); single retry; server side relies on the middleware.
- **Concurrency safety on BOTH paths**: refresh-token *reuse detection
  revokes the entire family* (ADR 0012 stolen-token defense), so racing
  refreshes would hard-log-out the user. In-flight refreshes are de-duped
  via module-level promise maps (per refresh-token value in middleware, per
  host in the client).

## Alternatives Considered

- **Proactive client-side timer refreshing before expiry** — *rejected: a
  timer only runs while a tab is open and still races multi-tab sessions;
  the middleware covers cold navigations (the common "came back later"
  case) and the 401-retry covers live pages — together they need no clock.*
- **Longer access-token lifetime** — *rejected: weakens the ADR 0012
  short-token model to paper over a missing client; the refresh
  infrastructure existed end-to-end and only lacked a caller.*
- **Refresh in `getDisplaySession()` (Server Component)** — *rejected:
  Server Components cannot set cookies in Next; middleware is the only
  request-scoped seam that can both rotate the browser cookies and let the
  same render see them.*

## Out of scope

- Safari-on-`http://localhost` rejecting `Secure` cookies (environmental
  dev caveat — Chrome/Firefox treat localhost as trustworthy, Safari does
  not; deployed envs are https). Documented here; no code change.
- Multi-instance refresh races (separate processes each de-dupe locally;
  dev runs single-instance — revisit with horizontal scaling).

## Acceptance criteria

- **AC-1** Given a logged-in user whose access cookie expired but refresh
  cookie is live, when they open any page, then the middleware rotates the
  session before render — the navbar shows the account menu and no
  intermediate logged-out flash occurs.
- **AC-2** Given an expired access cookie on a dashboard URL, when the
  request arrives, then the user is NOT bounced to /login (refresh runs
  before the guard); with an expired/revoked refresh cookie they ARE.
- **AC-3** Given a long-open page whose client call gets 401, when apiFetch
  handles it, then exactly one refresh + one retry happen and the call
  succeeds; a rejected refresh surfaces the original 401 with no loop.
- **AC-4** Given N concurrent 401s/renders racing one refresh token, when
  refresh triggers, then exactly one backend refresh round trip occurs
  (family-revocation reuse defense is never tripped by our own client).
- **AC-5** Given the backend briefly unreachable, when the middleware
  refresh attempt fails transport-level, then existing cookies are left
  untouched and the next request retries.

## Test plan reference

12 new vitest cases: `jwt-expiry.test.ts` (decode incl. UTF-8, expiry, skew,
garbage) + `api-fetch-auth-retry.test.ts` (refresh-and-retry, rejected
refresh, auth-path exclusion, single-retry cap, concurrent-401 collapse to
one refresh). Suite 76/76 green; `tsc`, eslint, `next build` (middleware
compiles for the edge bundle) all clean.

## Status log

- 2026-07-23 `draft → in_progress → in_review` — operator bug report
  ("it does not hold logged in state on frontend") root-caused and fixed
  same session; PR left open for operator merge.
