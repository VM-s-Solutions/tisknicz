---
id: T-0035
title: Backend auth controllers + frontend auth pages (login, register, register/maker, verify, reset, magic) + email-confirmation banner
status: done
size: L
owner: dotnet-backend + frontend
created: 2026-05-26
updated: 2026-05-26
depends_on: [T-0024, T-0025, T-0023, T-0026, T-0033, T-0034]
blocks: [T-0036]
adrs: [0012, 0005, 0008]
phase: 2
---

# T-0035 — Auth controllers + auth pages

## Scope

The ticket was originally defined as frontend-only, but T-0033 marked the maker-register controller as out-of-scope and the rest of the auth controllers were never built. T-0035 was expanded to include the backend HTTP surface so the Next.js pages have something to call.

### Backend (`Makables.Config/Controllers/Auth/AuthController.cs`)
One shared controller mounted on every Web host via the Config MVC application part. Nine anonymous endpoints under `/api/v1/auth/`:
- `POST register` — customer registration (Maker registration lives on a separate controller below).
- `POST login` — issues `SessionResult`; ships access + refresh as audience-scoped HttpOnly cookies via `AuthCookies.SetSessionCookies`.
- `POST logout` — clears the cookies; calls the `Logout` command if a refresh cookie was present.
- `POST refresh` — reads the refresh cookie, calls `Refresh.Command`, ships new cookies (or clears stale ones on failure).
- `POST confirm-email` — wraps `ConfirmEmail.Command`.
- `POST request-password-reset` + `POST confirm-password-reset` — wrap `RequestPasswordReset` and `ConfirmPasswordReset`.
- `POST request-magic-link` + `POST consume-magic-link` — wrap `RequestMagicLink` and `ConsumeMagicLink`; `consume-magic-link` ships session cookies on success.

The controller depends on a new `IHostAudience` singleton registered per host via `AddMakablesHostAudience(audience)` from `Program.cs`. The audience drives the cookie naming (`makables_access_{audience}` / `makables_refresh_{audience}`, matching `frontend/src/lib/auth/session.ts`) and is passed into audience-aware commands (`Login.Command.Audience`, `Refresh.Command.Audience`, `ConsumeMagicLink.Command.Audience`).

### Backend (`Makables.Web.Public/Controllers/RegisterMakerController.cs`)
Lives in the Public host project (NOT in shared Config) so the route appears ONLY on the Public host. One endpoint:
- `POST /api/v1/makers/register` — wraps `RegisterMaker.Command` (T-0033). Returns `{userId, makerId, snapshotIsStale}`.

### Backend (`Makables.Config/Auth/AuthCookies.cs`)
Helper for shipping/clearing the access + refresh cookies. Both cookies are `HttpOnly`, `Secure`, `SameSite=Strict`, `Path=/`. Cookie names match the frontend conventions: `makables_access_{audience}` and `makables_refresh_{audience}`.

### Frontend (`/auth/*` pages, `lib/api-client-helpers/auth.ts`)
- `(auth)/layout.tsx` — brand-aligned card layout (dark surface, centered, wordmark linking home).
- `(auth)/login/` — login form. Posts to customer host's `/api/v1/auth/login`. Maps `auth.invalidCredentials` / `auth.locked` / `auth.emailNotConfirmed` to localized messages.
- `(auth)/register/` — customer registration (defaults to `CZ`). Success state shows "check your inbox" copy.
- `(auth)/register/maker/` — maker registration (calls Public host's `/api/v1/makers/register`). Surfaces `snapshotIsStale` via a warning banner per ADR 0018.
- `(auth)/verify/` — consumes the `?token=…` from the email-confirmation link via `useEffect` (one-off client-side call so the future "login-on-confirm" flow can set cookies cleanly).
- `(auth)/reset/` — dual-mode page. No token = "send me a reset link" (enumeration-safe success message). With `?token=…` = "set new password" form.
- `(auth)/magic/` — dual-mode page. No token = "send me a magic link" form. With `?token=…` = consume + redirect to `/`.
- `components/shared/email-confirmation-banner.tsx` — banner for logged-in-but-not-confirmed users; one-click resend via `request-magic-link`.

### Frontend (`lib/runtime/api-fetch.ts`)
Added `credentials: 'include'` default so cookies ride along on cross-origin requests. The audience-scoped session cookies must accompany every request that needs auth. Callers can still override via `options.credentials`.

### Frontend (`lib/i18n/cs-CZ.ts`)
~70 new auth keys covering all five pages, error mappings, and the email-confirmation banner.

### Out of scope (deferred to follow-up tickets)
- **NSwag regen.** The hand-written helpers in `lib/api-client-helpers/auth.ts` mirror the controller DTOs; full NSwag regeneration requires running the dev backend and committing the four generated client files. Tracked for a follow-up.
- **Google OAuth pages.** Backend has `StartGoogleOAuth` / `CompleteGoogleOAuth` handlers but no controllers yet. T-0026's frontend OAuth callbacks are deferred until the OAuth controller ships.
- **Integration tests for the new controllers.** The handlers are fully tested at 670 facts; the controllers are thin mapping layers (one-liners + cookie writes). A future ticket should add `WebApplicationFactory` end-to-end smoke tests for login/refresh/logout cookie flow.
- **CSRF token on the auth endpoints.** All auth endpoints are `[AllowAnonymous]` POSTs; `SameSite=Strict` on the session cookies blocks the canonical CSRF attack for authenticated endpoints, but the unauthenticated login/register endpoints have no protection beyond that. Track for the security-hardening ticket.

## Acceptance criteria
- **AC-1** Customer can register, receive email, click verify link, log in via `/auth/login`, and see `/auth/login → /` redirect on success.
- **AC-2** Customer can request password reset, click email link, set new password, and log in.
- **AC-3** Customer can request a magic link, click it, and be logged in without typing a password.
- **AC-4** Maker can register via `/auth/register/maker` providing IČO + credentials; ARES drives the snapshot; stale-snapshot is surfaced via a warning banner.
- **AC-5** All auth endpoints set/clear `HttpOnly`, `Secure`, `SameSite=Strict` audience-scoped cookies named `makables_access_{audience}` / `makables_refresh_{audience}`.
- **AC-6** No user-facing English strings; every visible string keyed through `lib/i18n/cs-CZ`.
- **AC-7** TypeScript clean (`tsc --noEmit`); ESLint clean (`eslint src/`). Backend build clean; 670 tests still pass.
- **AC-8** Maker-registration controller is mounted on the Public host ONLY; customer/maker/admin hosts do not expose `/api/v1/makers/register`.
- **AC-9** Backend error codes map to localized Czech messages on the frontend (invalidCredentials, locked, emailNotConfirmed, emailAlreadyExists, icoFormat, companyNotFound, makerCompanyDissolved, makerIcoAlreadyRegistered).

## Status log
- 2026-05-26 done. Backend: AuthController (9 endpoints) + RegisterMakerController (Public-only) + AuthCookies + IHostAudience wired into all four hosts. Build clean, 670 tests pass. Frontend: 5 page routes + 6 client components + shared banner + ~70 i18n keys. `tsc` clean, `eslint` clean. Awaiting dual reviewer per workflow.
