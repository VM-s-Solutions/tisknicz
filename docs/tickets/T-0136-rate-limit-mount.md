---
id: T-0136
title: Mount the default rate-limit envelope + a tight auth-endpoint policy
status: ready
size: S
owner:
created: 2026-06-21
updated: 2026-06-21
depends_on: [T-0008, T-0035]
blocks: []
user_stories: []
adrs: [0023]
phase: 6
manual_steps: []
security_touching: true
layers: [dotnet-backend, secops]
---

# T-0136 — Mount the default rate-limit envelope + a tight auth-endpoint policy

> Closes **Q-0011** (rate-limiter "default" policy mounted nowhere). The first
> half of `feat/secops-hardening-bundle` (Bundle, user-locked 2026-06-21; ships
> in one PR with T-0137).

## Context

`AddMakablesRateLimiting.cs` defines a per-audience **"default"** fixed-window
policy (Customer 100/min, Maker 60/min, Admin 30/min, Public 60/min) plus two
partitioned policies that ARE mounted (`addresses-autocomplete` on the Mapbox
proxy, `shipping-widget-config` on the public Packeta widget). But the "default"
policy is referenced by **no** `[EnableRateLimiting("default")]` attribute and is
set as **no** `GlobalLimiter` — so it is inert. Every endpoint that isn't one of
the two partitioned ones is effectively unlimited, including:

- the **14 anonymous auth endpoints** (`/auth/login`, `/auth/register`,
  `/auth/refresh`, `/auth/confirm-*`, `/auth/request-*`, `/auth/consume-magic-link`)
  — the brute-force / enumeration / credential-stuffing surface; and
- `POST /orders/{orderId}/messages` (`PostMessage`, 2000-char authenticated
  bodies on both the Customer and Maker hosts) — the DB-bloat-spam surface
  Q-0011 explicitly names.

ADR 0023 §4 already anticipates auth-endpoint limiting: the alert table carries
"Failed login rate > 50/min from same IP → Sev 3 (potential attack)". This
ticket gives that alert a matching enforcement control.

## Scope

1. **Mount the "default" policy as the per-host `GlobalLimiter`** in
   `AddMakablesRateLimiting`. Partition shape mirrors the existing
   `PartitionAutocomplete`: per `sub` claim when authenticated, per remote IP
   (X-Forwarded-For-aware via `Connection.RemoteIpAddress`) when anonymous, with
   a fixed `ip:unknown` fallback bucket so an attacker can't bypass by dropping
   the header. Permit limit + window are the per-audience pair already computed
   (`permitLimit`, `window`). This covers `PostMessage` and the entire
   un-attributed surface in one move.
2. **Add a tight `"auth"` policy** — per-IP fixed window, **10/min**, `QueueLimit
   = 0` (reject, don't queue, so a stuffer gets an immediate 429). Mount it
   class-level on `AuthController` via `[EnableRateLimiting("auth")]`. The
   per-endpoint `auth` limit is intentionally tighter than (and composes under)
   the global default — ASP.NET applies the endpoint policy AND the global
   limiter; the stricter wins for the auth surface.
3. **Keep the two existing partitioned policies untouched** — they already
   override the global where mounted (`[EnableRateLimiting(...)]` on the
   endpoint takes precedence). Confirm no regression.
4. **Set `Retry-After` on rejection** via an `OnRejected` callback (emit the
   `Retry-After` header from the limiter metadata when present) so a
   well-behaved client backs off. `RejectionStatusCode` stays 429.
5. **Tests** — integration tests on a test host that (a) the global limiter
   yields 429 after the per-audience budget on a normal endpoint, (b) the `auth`
   policy yields 429 after 10 requests/min/IP on `/auth/login`, (c) the two
   pre-existing partitioned endpoints still behave, (d) `Retry-After` is present
   on a 429. Plus a pure-logic test of the partition-key selection
   (authenticated → `user:{sub}`, anonymous → `ip:{ip}` / `ip:unknown`).

## Alternatives Considered

- **Per-endpoint attribute on `PostMessage` only** (Q-0011 option 2) — narrowest,
  but leaves the auth surface and everything else unlimited. Rejected: the auth
  endpoints are the real risk; a piecemeal attribute sprawl is harder to reason
  about than one global envelope + one tight auth policy.
- **Defer until traffic data exists** (Q-0011 option 3) — rejected: the control
  is cheap, the ADR 0023 §4 alert already assumes it, and "ship the alert with no
  enforcement" is a launch-blocking inconsistency.
- **Distributed limiter (Redis)** — rejected for MVP: single-region, low volume;
  the in-memory partitioned limiter is per-instance but adequate. Flagged as a
  v1.1 scale concern in the ticket's Technical notes, not this bundle.

## Out of scope

- No per-endpoint tuning beyond auth (the global envelope covers the rest).
- No distributed/Redis-backed limiter (single-instance MVP).
- No frontend change — 429 is a raw middleware rejection, not a
  `BusinessResult`, so it carries **no** `BusinessErrorMessage` code and **no**
  cs-CZ i18n key (the generic `api-fetch` error path already handles non-2xx).
- No NSwag regen (no contract surface change — 429 is a transport status).

## Acceptance criteria

- **Given** an anonymous client, **when** it sends > 10 `POST /auth/login`
  in a minute from one IP, **then** the 11th returns **429** with a `Retry-After`
  header.
- **Given** any authenticated client, **when** it exceeds its per-audience
  default budget on a normal endpoint (e.g. `PostMessage`), **then** further
  requests return **429** until the window rolls.
- **Given** the existing `addresses-autocomplete` / `shipping-widget-config`
  endpoints, **when** exercised, **then** their dedicated partition limits still
  apply (no regression from the new global limiter).
- **Given** the host boots, **then** `UseRateLimiter()` is active and the global
  limiter is registered (existing `Host_RateLimiter_Options_Are_Registered`
  smoke test still passes).

## Technical notes

- `GlobalLimiter` is `PartitionedRateLimiter.Create<HttpContext, string>(...)`.
  Factor a shared `DefaultPartition(HttpContext, int permitLimit, TimeSpan
  window)` helper so the global limiter and the audience pair stay in one place.
- The `auth` policy is anonymous-only in practice (auth endpoints are
  `[AllowAnonymous]`), so partition straight on IP — no `sub` branch needed.
- Pipeline order is already correct (`UseRateLimiter()` after
  `UseAuthorization()` in `UseMakablesPipeline`); no ordering change.
- **In-memory caveat:** the limiter is per-instance. At single-region MVP scale
  this is fine; a multi-instance scale-out would need a distributed store
  (v1.1). Document inline.

## Files touched (expected)

- `backend/src/Makables.Config/Extensions/AddMakablesRateLimiting.cs` (global
  limiter + `auth` policy + `OnRejected` Retry-After)
- `backend/src/Makables.Config/Controllers/Auth/AuthController.cs`
  (`[EnableRateLimiting("auth")]` class-level)
- `backend/src/Makables.IntegrationTests/...` (429 behavior tests)
- `backend/src/Makables.Tests/...` (partition-key pure-logic test)

## Test plan reference

`docs/test-plans/T-0136.md`

## Status log

- 2026-06-21 `draft → ready` by PM (groomed in `feat/secops-hardening-bundle`;
  Q-0011 answer locked: global default + tight per-IP auth policy).
