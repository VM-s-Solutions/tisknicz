---
id: T-0026
title: Google OAuth — server-side authorization-code flow with HKDF-derived signed state
status: done
size: M
owner: dotnet-backend
created: 2026-05-25
updated: 2026-05-25
depends_on: [T-0022]
blocks: [T-0035]
adrs: [0012]
phase: 2
---

# T-0026 — Google OAuth

## Scope

Server-side OAuth 2.0 authorization-code flow per ADR 0012 §Google OAuth. Two HTTP-facing use cases (`StartGoogleOAuth`, `CompleteGoogleOAuth`) plus a Google adapter in `Infra.Clients/Google/` plus an HMAC-signed-state primitive in `Infra.Common/Auth/`.

### Domain (`Core.Domain/Identity/`)
- `IGoogleOAuthClient.cs` + `GoogleProfile` record.
- `IOAuthStateSigner.cs` + `OAuthStatePayload(Audience, RedirectUri, CsrfCookieHash, Nonce, IssuedAt)`.

### Infra.Common (`Auth/`)
- `OAuthStateSigner.cs` — HMAC-SHA256 over `{payloadB64}.{hmacB64}`. The HMAC key is HKDF-derived from the JWT signing key with context label `"makables-oauth-state-v1"` (domain separation — closes reviewer BLOCKER B-1). Sign takes `(audience, redirectUri, csrfCookieValue, nonce, issuedAt)` and hashes the cookie value internally (SHA-256 hex) so it never appears in the URL state. Verify checks signature, redirect-URI binding, CSRF-cookie binding, stale-window (10 min default). All compares are constant-time (`CryptographicOperations.FixedTimeEquals`).

### Infra.Clients (`Google/`)
- `GoogleOAuthOptions.cs` — `Auth:Google` config. `ClientId`, `ClientSecret`, `Scopes` (default `openid email profile`), `AuthorizationEndpoint`, `TokenEndpoint` (URLs default to Google but are overridable so integration tests can point at a stub).
- `GoogleOAuthClient.cs` — `IHttpClientFactory`-backed; ID-token validation via `GoogleJsonWebSignature.ValidateAsync` (Google JWKS, audience=ClientId, expiry, issuer). Throws `GoogleOAuthException` on any failure.

### Core.AppServices/Features/Auth
- `StartGoogleOAuth.cs` — `Command(Audience, RedirectUri)` → `Response(AuthorizationUrl, CsrfCookieValue)`. Mints a fresh 32-byte CSRF cookie value, signs `(audience, redirectUri, csrfCookieValue, nonce, issuedAt)`, asks the Google client for the URL. Rejects admin audience. Caller (controller in T-0035) sets the cookie as `__Host-makables_oauth_csrf` HttpOnly+Secure+SameSite=Lax before redirecting.
- `CompleteGoogleOAuth.cs` — `Command(Code, State, RedirectUri, CsrfCookieValue, UserAgent?, IpAddress?)` → `SessionResult`. Verifies state (covers signature + redirect-URI binding + CSRF-cookie binding + stale window), rejects admin, exchanges code, refuses unverified email, resolves user via `ResolveOrCreateUserAsync` helper (covers GoogleSub-match / link-by-email / create-new with role-from-signed-audience and country-from-`AuthDefaultCountryOptions`), checks audience, mints access + 30-day refresh. Catches narrowly: `HttpRequestException`, `TaskCanceledException`, `JsonException`, `GoogleOAuthException`. Re-throws `OperationCanceledException` on caller cancel.
- `AuthDefaultCountryOptions.cs` — `Auth:DefaultCountry` config; default `"CZ"` matching the launch market. Replaces the hardcoded country in the create-new-user branch.

### Common
- `BusinessErrorMessage.AuthOAuthInvalidState`, `AuthOAuthEmailNotVerified`, `AuthOAuthExchangeFailed`.

### DI
- `AddMakablesInfrastructure` — registers `IOAuthStateSigner` (singleton) + binds `AuthDefaultCountryOptions`.
- `AddMakablesClients` — binds `GoogleOAuthOptions`, registers named HttpClient + `IGoogleOAuthClient`.

### Packages
- `Google.Apis.Auth 1.74.0`.

### Open question filed
- `docs/questions/open.md` Q-0005 — Google OAuth PKCE.

## Reviewer findings (commit ba9da91) and resolutions

Two reviewers ran in parallel.

### Security reviewer — BLOCKER × 2 + MAJOR × 4

- **B-1 domain separation** — JWT signing key was used directly for OAuth-state HMAC. **Fixed:** `OAuthStateSigner` HKDF-derives a dedicated sub-key with context label `"makables-oauth-state-v1"`. JWT and state are now signed under different keys; a JWT can never validate as a state and vice versa. Pinned by `HKDF_domain_separation_a_JWT_signed_under_the_raw_key_is_not_a_valid_state`.
- **B-2 state not bound to context** — captured-state replay against a victim's browser. **Fixed:** state payload now binds `RedirectUri` + SHA-256 hash of an HttpOnly anti-CSRF cookie value. `StartGoogleOAuth` mints the cookie value and returns it for the controller to set; `CompleteGoogleOAuth` reads both from the request and feeds them into `TryVerify`. Pinned by `TryVerify_rejects_redirect_uri_mismatch` and `TryVerify_rejects_csrf_cookie_mismatch`.
- **M-1 nonce replay within 10-min window** — explicitly deferred. The B-2 cookie binding makes a captured-state replay require ALSO forging the victim's HttpOnly cookie, which a remote attacker can't do. A persistent nonce-consumption table is a real DB write per OAuth start; not worth the cost while the cookie binding holds.
- **M-3 catch-all swallowing OperationCanceledException** — **Fixed:** narrowed to `HttpRequestException` / `TaskCanceledException` / `JsonException` / `GoogleOAuthException`. `OperationCanceledException` on caller cancellation re-throws so the framework can wind it up. Pinned by `Rethrows_OperationCanceledException_on_caller_cancellation`.
- **M-4 `IPersistOnFailureCommand` overstated in doc** — **Fixed:** doc-comment now spells out the single mutation path that needs the marker (link-then-fail-audience).
- **N-1/N-2/N-3/N-4 (minors)** — accepted or noted.

### Code-quality reviewer — 0 BLOCKERs + 3 MAJORs + 4 MINORs

- **M-1 hardcoded "CZ"** — **Fixed:** `AuthDefaultCountryOptions` bound from `Auth:DefaultCountry`. Default is still `"CZ"` per the launch market but it's config-driven.
- **M-2 missing T-0026 ticket file** — **Fixed:** this file. Reviewer was correct; the ticket doc was never created in the first pass.
- **M-3 IPersistOnFailureCommand doc** — **Fixed** with M-4 above.
- **M-4 ResolveOrCreateUser helper** — **Fixed:** branch extracted into `ResolveOrCreateUserAsync` returning a `UserResolution` record. Main handler reads top-to-bottom.
- **M-5 Google endpoints hardcoded** — **Fixed:** moved to `GoogleOAuthOptions.AuthorizationEndpoint` / `TokenEndpoint` with the live URLs as defaults.
- **M-6 PKCE deferral tracked only inline** — **Fixed:** Q-0005 added to `docs/questions/open.md`.
- **N (scope literal const, JsonOptions freeze, tuple deconstruction, ValueTask)** — accepted; minor polish.

## Acceptance criteria
- **AC-1** Build clean; 337 tests pass.
- **AC-2** State HMAC uses a HKDF-derived sub-key; raw JWT key is never used for state.
- **AC-3** State binds redirect URI + anti-CSRF cookie hash; both verified at callback.
- **AC-4** Admin audience rejected in both Start and Complete.
- **AC-5** `email_verified=false` profiles refused.
- **AC-6** User resolution covers GoogleSub-match / email-link / create-new with role-from-state and country-from-config.
- **AC-7** Cross-audience login refused.
- **AC-8** Narrow exception catch in code exchange; cancellation re-throws.

## Out of scope
- PKCE — Q-0005 open.
- HTTP endpoints (cookie set in response, code+state read on callback) — T-0035 (frontend + controller).

## Status log
- 2026-05-25 done. Initial commit ba9da91. Both reviewers ran in parallel.
- 2026-05-25 reviewer fix folded in. 337 tests pass. Security B-1 / B-2 closed; CQ M-1 / M-2 (this doc) / M-3 / M-4 / M-5 / M-6 closed.
