---
id: T-0156
title: Cookie → JWT bridge — JwtBearer never read the HttpOnly access cookie; every [Authorize] endpoint 401'd for browsers
status: in_review
size: S
owner: dotnet-backend
created: 2026-07-23
updated: 2026-07-23
depends_on: [T-0027, T-0035]
blocks: []
user_stories: [US-customer-0018, US-customer-0016, US-maker-0005]
adrs: [0012, 0005]
phase: 7
manual_steps: []
security_touching: true
layers: [dotnet-backend]
---

# T-0156 — Cookie → JWT bridge (walk-surfaced BLOCKER)

## Context

Operator report during the T-0153 walk: logged in (navbar shows the account
menu) but clicking Profile bounces to login and Orders says "please log in".
Root cause: ADR 0012 ships the access JWT as an **HttpOnly cookie**
(`makables_access_<audience>`, `AuthCookies`), but `AddMakablesAuth` wired a
stock `JwtBearer` handler, which reads **only the `Authorization: Bearer`
header**. Browser JS cannot read an HttpOnly cookie to build that header
(that is the point of HttpOnly), and the T-0027-era `apiFetch.accessToken`
"cookie → Bearer bridge" was never built. Net effect: **every `[Authorize]`
endpoint returned 401 for every real browser session, always** — profile,
orders, maker dashboard, all of it. The suite never caught it because
integration tests mint tokens and pass `Authorization` headers directly.
The navbar looked logged-in because it decodes the cookie itself
(display-session), never calling a protected endpoint.

## Scope

- `JwtBearerEvents.OnMessageReceived` in `AddMakablesAuth`: when the request
  carries no `Authorization` header, resolve the token from the first
  accepted-audience access cookie (`MakablesAuthExtensions.ResolveTokenFromCookies`,
  internal + tested via `InternalsVisibleTo` mirroring the Core.Domain
  precedent).
- Probe order = the host's `AcceptedAudiencesFor` list (own audience first,
  admin last; Public probes customer → maker → admin), so a multi-cookie
  browser authenticates with the most specific session.
- An explicit `Authorization` header **always wins** — the bridge never
  overrides a caller-supplied Bearer token, so every existing test rig and
  machine-to-machine path is untouched.

## Security notes

- Cookie-borne auth makes CSRF the relevant threat: contained by
  `SameSite=Strict` on the session cookies (cross-site requests do not carry
  them) + per-host CORS allowlists. This is the ADR 0012 §Cookies design
  finally taking effect, not a new surface.
- Audience isolation is preserved: a maker cookie on the customer host is
  not in the accepted list → not read → request stays anonymous → 401, same
  compile-time isolation as before.

## Acceptance criteria

- **AC-1** Given a logged-in browser session (access cookie only, no
  header), when it calls any `[Authorize]` endpoint on its audience host,
  then the request authenticates and succeeds.
- **AC-2** Given a request with an `Authorization: Bearer` header AND a
  cookie, when validated, then the header token is used (bridge returns
  null).
- **AC-3** Given only a foreign-audience cookie (maker cookie on the
  customer host), when validated, then the request stays unauthenticated.
- **AC-4** Given both an own-audience and an admin cookie on one host, when
  validated, then the own-audience token wins (accepted-list order).

## Test plan reference

7 new unit tests (`MakablesAuthCookieBridgeTests`): cookie read, header
precedence, foreign-audience rejection, own-before-admin order, Public-host
order, empty-value skip, bare request. Full unit suite 1872/1872 green in
Release. Live re-verify = T-0153 walk row 1.10 after merge + deploy.

## Status log

- 2026-07-23 `draft → in_progress → in_review` — second walk-surfaced
  blocker of the day (after T-0154); this one is THE reason no
  authenticated page ever worked in a browser. PR left open for operator
  merge.
