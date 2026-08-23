---
id: T-0189
title: Stop charging a blocking backend round trip for a refresh token that can never succeed
status: in_review
size: S
owner: claude
created: 2026-08-23
updated: 2026-08-23
depends_on: []
blocks: []
user_stories: []
adrs: [0012]
phase: 8
manual_steps: []
security_touching: true
layers: [frontend, secops]
---

# T-0189 — The stale-refresh-cookie latency tax

## Context

Operator, after the T-0187/T-0188 investigation: *"problem byly stare cookies."*

That closes the loop on why the Safari-vs-Chrome asymmetry was invisible to
every measurement in T-0188 — safaridriver and Playwright both run a **clean
profile with no cookies**. The reporter's Safari held stale ones; their Chrome
did not. But "clear your cookies" is not a fix: real visitors never do, and the
slow path is entirely ours.

[`src/middleware.ts`](../../frontend/src/middleware.ts) matches **every page and
RSC request**. For each audience whose access cookie is expired but whose
refresh cookie is present, it makes a **blocking server-side fetch** to that
host's `/api/v1/auth/refresh` before the render can start. Two defects made that
permanent:

1. **A rejected refresh was indistinguishable from an unreachable backend.**
   `refreshSession` returned `null` for `!response.ok` *and* for a thrown fetch,
   and the caller just did `continue`. So a token the backend had already
   declared dead — expired family, revoked, reused, or signed by a retired key —
   kept its cookies, and the *next* request re-attempted the identical doomed
   refresh. Forever.
2. **The per-audience refreshes ran sequentially.** `for (const audience of AUDIENCES) { … await … }`
   over customer/maker/admin, each with `AbortSignal.timeout(8000)`. A browser
   holding stale cookies for all three paid three round trips back to back on
   every navigation, and a single unreachable host could stall a render for a
   full 8 s (CLAUDE.md §5 — no sequential awaits over independent I/O).

A third, smaller one surfaced while fixing those: `guardOrNext` decided
`hasAccess` from the *presence* of the original request cookie, so a visitor
with an expired access cookie and a dead refresh cookie was waved into the
dashboard, where every call then 401s — a broken page rather than an honest
logged-out redirect.

## Scope

- `refreshSession` returns a discriminated `RefreshOutcome`:
  `rotated` / `rejected` (401 · 403) / `unavailable` (5xx, 429, timeout, throw,
  200-without-Set-Cookie).
- `rejected` expires both cookies on the response (`Max-Age=0`, `Path=/`,
  `HttpOnly`, `SameSite=Strict`, `Secure` on https) and strips them from the
  request this render sees.
- `unavailable` keeps today's behaviour exactly — cookies untouched, retry next
  request. A blip must never log anyone out.
- The audiences refresh concurrently via `Promise.all`.
- Per-audience timeout 8000 ms → 3000 ms: this fetch blocks the render and up to
  three can be needed at once.
- `guardOrNext` treats a just-killed cookie as absent.

## Alternatives Considered

- **Clear the cookies on any non-2xx** — a 503 during an Azure cold start would
  log out every active session. Rejected; that is why the outcome is a
  three-way discriminant and not a boolean.
- **Fix it client-side in `api-fetch.ts`** — that path has the same "refresh
  failed, cookies survive" shape, but it is not what makes *page loads* slow;
  the middleware runs before the render on every request. Worth a follow-up,
  out of scope here.
- **Cap it with a short-lived "don't retry this token" memo** — a module-level
  map is per-process and per-isolate, so it would not survive the multi-worker
  frontend (T-0187) and would leak memory keyed on attacker-suppliable values.
  Expiring the cookie puts the state where it belongs: the browser.

## Out of scope

- The client-side `401 → refresh → retry` path in `lib/runtime/api-fetch.ts`.
- Anything about *why* the reporter's tokens went stale (deploys rotate the JWT
  signing key in dev; that is expected).

## Acceptance criteria

- **AC-1** Given a refresh the backend answers 401/403, when the middleware
  handles the request, then both cookies for that audience are expired on the
  response.
- **AC-2** Given the backend is unreachable or answers 5xx, when the middleware
  handles the request, then no cookie is expired.
- **AC-3** Given stale cookies for several audiences, when one request is
  handled, then the refreshes are issued concurrently.
- **AC-4** Given a guarded route and a cookie whose refresh was just rejected,
  when the middleware handles the request, then it redirects to the login rather
  than rendering the dashboard.
- **AC-5** Given a browser that has just been served the expiry, when it makes
  the next request, then the middleware makes no backend refresh call at all.

## Technical notes

Measured against the real standalone build with a stub backend at 300 ms
latency answering 401, stale cookies for all three audiences, curl driving a
cookie jar so it behaves like a browser:

| request | before (master) | after |
|---|---|---|
| 1 | 962 ms · 3 backend calls | **346 ms** · 3 calls |
| 2 | 923 ms · 6 cumulative | **5.6 ms** · still 3 |
| 3 | 922 ms · 9 cumulative | **4.0 ms** · still 3 |
| 4 | 919 ms · 12 cumulative | **4.7 ms** · still 3 |

First request 2.7× faster (concurrency); every request after it ~180× faster
and free of backend calls, because the dead cookies are gone.

## Files touched (expected)

- `frontend/src/middleware.ts`
- `frontend/src/__tests__/middleware-stale-refresh.test.ts` (new)
