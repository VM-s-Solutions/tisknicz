---
id: T-0190
title: Stop the client re-running a refresh the backend has already rejected
status: in_review
size: S
owner: claude
created: 2026-08-23
updated: 2026-08-23
depends_on: [T-0189]
blocks: []
user_stories: []
adrs: [0012]
phase: 8
manual_steps: []
security_touching: true
layers: [frontend, secops]
---

# T-0190 — Client-side dead-session memo

## Context

The follow-up [T-0189](T-0189-stale-refresh-cookie-tax.md) explicitly left open:
the same "refresh failed, cookies survive" shape exists in the **client** path.

[`lib/runtime/api-fetch.ts`](../../frontend/src/lib/runtime/api-fetch.ts)
implements `401 → refresh → retry-once` (T-0154). `refreshClientSession`
returned a **boolean**, so "the backend rejected this token" and "the backend is
unreachable" were the same answer, and nothing was remembered either way. Once a
refresh token was definitively dead, **every** later call on that page re-ran the
identical doomed sequence: request → 401 → refresh → 401. Two round trips per
call, for the life of the page.

T-0189 fixes the document-request half by expiring the cookies in the
middleware — but that only runs on a navigation. A long-lived client page (a
dashboard, the order-detail polling surface) keeps firing XHRs and kept paying.

The browser **cannot** expire the cookies itself; they are HttpOnly by design
(ADR 0012). So the rejection is remembered in the tab instead.

## Scope

- `refreshClientSession` returns `'rotated' | 'rejected' | 'unavailable'`
  instead of a boolean — same three-way split as the middleware.
- A module-level `Set<ApiHost>` records hosts the backend has rejected; the
  401 path skips the refresh for those.
- `'unavailable'` (5xx, 429, timeout, network throw) is deliberately **not**
  remembered.
- The memo is cleared on any successful response from that host, so a login —
  or another tab rotating the shared cookies — recovers it with no special case.
- `api-fetch-auth-retry.test.ts` re-imports the module per test; module-scope
  state would otherwise make the existing tests order-dependent.

## Alternatives Considered

- **Have the client delete the cookies** — impossible, and correctly so: they
  are HttpOnly. Rejected.
- **Remember it in `sessionStorage`** — survives reloads, which is worse, not
  better: a reload is exactly when the middleware expires the cookies properly,
  and a stuck flag there would suppress a legitimate refresh. Rejected.
- **A timestamped back-off instead of a flag** — more moving parts for no gain;
  a rejected token never becomes valid again, and the success-clears rule
  already handles every recovery path. Rejected.

## Out of scope

- The refresh timeout on the client (`DEFAULT_TIMEOUT_MS`). Unlike the
  middleware's three concurrent render-blocking calls, this is one refresh
  after a 401 and matches the rest of the client's budget.

## Acceptance criteria

- **AC-1** Given the backend answers the refresh 401/403, when further calls to
  that host 401, then no further refresh is attempted.
- **AC-2** Given the refresh throws or answers 5xx, when further calls 401, then
  the refresh IS attempted again — a blip must not strand a live session.
- **AC-3** Given the memo is set, when any call to that host succeeds, then the
  memo is cleared and a later expiry refreshes normally.
- **AC-4** Given one host's session is dead, when another host 401s, then that
  other host still refreshes.

## Technical notes

**Server safety.** This module is shared by every SSR request in the Node
process — and, since [T-0187](T-0187-frontend-cpu-headroom.md), by each of its
workers — so per-session state at module scope would leak *across users*. Every
read and write of the memo sits inside a `typeof window !== 'undefined'` branch.
That invariant is load-bearing and called out in the source comment.

Measured by fetch count, four calls on a page whose session is dead:

| | requests issued |
|---|---|
| before | 8 (4 calls + 4 doomed refreshes) |
| after | **5** (4 calls + 1 refresh) |

Each avoided refresh is a full round trip through the `/api-proxy` rewrite,
measured at 260–540 ms on dev under concurrency during T-0187.

## Files touched (expected)

- `frontend/src/lib/runtime/api-fetch.ts`
- `frontend/src/lib/runtime/api-fetch-dead-session.test.ts` (new)
- `frontend/src/lib/runtime/api-fetch-auth-retry.test.ts` (test isolation)
