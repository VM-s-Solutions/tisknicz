---
id: T-0139
title: Apple Sign-In (Sign in with Apple OAuth) — server-side flow alongside Google OAuth
status: ready
size: M
owner: <agent name when in_progress>
created: 2026-07-07
updated: 2026-07-07
depends_on: [T-0026, T-0035]
blocks: []
user_stories: [US-customer-0004]
adrs: [0012, 0026]
phase: 2
manual_steps: [vendor-account, secret-rotation]
security_touching: true
layers: [dotnet-db, dotnet-backend, frontend, l10n, secops]
---

# T-0139 — Apple Sign-In (Sign in with Apple OAuth)

## Context
Google OAuth (T-0026, T-0035) is live and converges into the same `User` record as email/password and magic link, per ADR 0012. iOS/Safari users expect "Sign in with Apple" as a peer option, and Apple mandates it for apps offering third-party social login in App-Store-distributed contexts — but even web-only, it materially raises registration conversion for Apple-device users. This ticket adds Apple as a fourth identity provider, mirroring the Google server-side authorization-code flow exactly, so the two providers share the same state-signing, audience-binding, and user-resolution machinery. It delivers on US-customer-0004 ("as a customer I want to register/log in with a social account") for the Apple-device segment.

## Scope
- **Core.Domain/Identity/**: `IAppleOAuthClient.cs` + `AppleProfile` record (sub, email, emailVerified — Apple's `is_private_email` flag surfaced too, informational only). `User.AppleSub` column (nullable, unique-if-present, mirrors `GoogleSub`) + `LinkAppleSub(string)` domain method mirroring `LinkGoogleSub` (idempotent for same sub, rejects relinking to a different sub).
- **dotnet-db**: EF Core migration adding `apple_sub` (nullable `text`, unique partial index `WHERE apple_sub IS NOT NULL`, mirroring the existing `google_sub` index shape). `IUserRepository.GetByAppleSubAsync`.
- **Infra.Clients/Apple/**:
  - `AppleOAuthOptions.cs` — `Auth:Apple` config: `ClientId` (the Services ID, e.g. `cz.makables.web`), `TeamId`, `KeyId`, `PrivateKeyPem` (from Key Vault, never logged), `Scopes` (`name email`), `AuthorizationEndpoint`/`TokenEndpoint` (default to Apple's live URLs, overridable for tests).
  - `AppleClientSecretSigner.cs` — mints Apple's **ES256 JWT client secret** on demand: `iss=TeamId`, `sub=ClientId`, `aud=https://appleid.apple.com`, `iat=now`, `exp=now+15min` (Apple's docs allow up to 6 months but we mint short-lived per-request, no caching complexity, no rotation job needed), signed with the P-256 private key identified by `KeyId` (`kid` header). This is the real technical delta vs Google — Apple has no static client secret.
  - `AppleOAuthClient.cs` — `IHttpClientFactory`-backed; POSTs to Apple's token endpoint with the freshly-signed client secret; validates the returned `id_token` against Apple's JWKS (`https://appleid.apple.com/auth/keys`) checking issuer `https://appleid.apple.com`, audience=ClientId, expiry. Apple returns the user's name/email **only on first authorization**, in a separate `user` form field (not in subsequent logins) — client captures it if present, falls back to id_token claims only otherwise. Throws `AppleOAuthException` on any failure.
- **Core.AppServices/Features/Auth**:
  - `StartAppleOAuth.cs` — `Command(Audience, RedirectUri)` → `Response(AuthorizationUrl, CsrfCookieValue)`, structurally identical to `StartGoogleOAuth`: mints CSRF cookie value, signs `(audience, redirectUri, csrfCookieValue, nonce, issuedAt)` via the existing `IOAuthStateSigner` (same HKDF-derived sub-key, same domain-separation label scoped per-provider — see Technical notes), rejects admin audience. Apple requires `response_mode=form_post` for web flows (Apple POSTs back, not GETs) — the callback controller must accept a POST body, not just query params; this is the other real delta from Google.
  - `CompleteAppleOAuth.cs` — `Command(Code, State, RedirectUri, CsrfCookieValue, UserAgent?, IpAddress?)` → `SessionResult`, mirroring `CompleteGoogleOAuth`: verifies state, rejects admin, exchanges code via `AppleOAuthClient` (using the freshly-minted JWT client secret), refuses unverified email, resolves user via a shared/adapted `ResolveOrCreateUserAsync` helper (AppleSub-match / link-by-email / create-new with role-from-signed-audience and country-from-`AuthDefaultCountryOptions`), checks audience, mints access + 30-day refresh. Narrow catch: `HttpRequestException`, `TaskCanceledException`, `JsonException`, `AppleOAuthException`; re-throws `OperationCanceledException`.
- **Common**: `BusinessErrorMessage.AuthOAuthInvalidState` / `AuthOAuthEmailNotVerified` / `AuthOAuthExchangeFailed` are reused (provider-agnostic already); no new error codes needed unless Apple-specific failure modes surface in testing (e.g. `AuthOAuthAppleClientSecretSigningFailed` if the Key Vault key is unreachable — add only if a distinct customer-facing message is warranted).
- **DI**: `AddMakablesInfrastructure` registers `IAppleOAuthClient`; `AddMakablesClients` binds `AppleOAuthOptions`, registers named HttpClient. `AppleClientSecretSigner` registered as scoped/transient (no shared mutable state).
- **Packages**: a JWT/ES256 signing library already present in the solution (check existing JWT signing deps used for access tokens before adding a new package — prefer reuse over a new NuGet dependency).
- **Web.Customer/Web.Maker/Web.Public controllers**: `GET /api/public/auth/apple/start?audience=...` (mirrors Google's start endpoint) and `POST /api/public/auth/apple/callback` (POST, not GET — Apple's `form_post` requirement) alongside the existing Google callback in `AuthController`.
- **Frontend**: Apple button rendered next to the existing Google button in `frontend/src/app/(auth)/login/login-form.tsx` and `register/register-form.tsx`; `frontend/src/lib/api-client-helpers/auth.ts` gets an `startAppleOAuth` wrapper mirroring `startGoogleOAuth`. New `auth.apple.*` cs-CZ i18n keys (button label, any Apple-specific error copy) — routed through `l10n`.
- **NSwag regen**: required if any new controller routes change the OpenAPI contract (they do — two new endpoints) — regenerate in the same PR, `npm run check:api` must pass.
- **ADR**: an amendment to ADR 0012 (or a new ADR 0026, architect's call) documenting Apple's OAuth parameters — specifically the ES256 client-secret-as-JWT mechanic (vs Google's static secret), the `form_post` callback shape, and the first-authorization-only name/email delivery quirk. **This ticket does not author the ADR text** — PM routes to `architect` before/alongside implementation per routing.md.
- **Doc-currency flag (Gate 7, out of scope for this ticket's implementation but noted for architect)**: `docs/architecture/patterns.md` §A.17 "Authentication (custom)" still reads "Out of scope for MVP: OAuth (Google)" — stale since T-0026/T-0035 shipped. Architect should correct this line to reflect both Google and Apple in the same pass as the ADR amendment.

## Alternatives Considered
- **Option A — client-side "Sign in with Apple JS" SDK only (Apple's `AppleID.auth.init` + `signIn()` in the browser, token posted to our backend for verification only)** — *rejected because* it fragments our auth architecture: Google is server-side authorization-code flow with HttpOnly-cookie CSRF binding and audience-signed state; a client-side Apple flow would need a parallel token-verification path with different trust assumptions (browser-held tokens vs server-held code exchange), duplicating security review surface for no real benefit — Apple's server-side flow is equally well-supported and lets us reuse `IOAuthStateSigner`, the audience-rejection rule, and `ResolveOrCreateUserAsync` verbatim.
- **Option B — server-side authorization-code flow mirroring Google exactly (chosen)** — same shape as T-0026, same `Core.AppServices/Features/Auth` structure, same state-signing primitive, same admin-audience rejection. The only genuine deltas are (1) the ES256-JWT client secret instead of a static one and (2) the `form_post` callback requiring POST-body handling instead of query-string GET.
- **Option C — defer Apple to post-MVP, ship Google-only** — *rejected*: user explicitly requested this feature now; Apple-device users are a material share of the target Czech consumer market, and the marginal backend cost is low given the Google flow already exists to mirror.

**Defense:** Option B is the only choice that preserves the single-converged-`User`-record model and reuses proven, already-security-reviewed primitives (HKDF-derived state HMAC, redirect-URI + CSRF-cookie binding, narrow exception catch, admin-audience rejection) rather than opening a second, differently-shaped auth surface. The two real technical deltas (ES256 client-secret JWT, `form_post` POST callback) are isolated to `Infra.Clients/Apple/` and the controller route method, so the blast radius of "Apple is different from Google" stays small and auditable.

## Out of scope
- Native iOS/Android Sign in with Apple (Makables is web-only at MVP; no native app).
- PKCE for the Apple flow — Google's PKCE is tracked as open question Q-0005; Apple should ride the same resolution rather than open a second parallel question. If Q-0005 is unresolved when this ticket starts, log a note under Q-0005 rather than opening a duplicate question.
- Rewriting `patterns.md` §A.17 beyond the one stale line the architect is asked to fix — a full pattern-doc rewrite is not this ticket's job.
- Any change to the admin audience rule — Apple is rejected for admin exactly like Google, no new admin-auth decision here.
- MFA / SSO — explicitly out of scope per ADR 0012, unaffected by this ticket.

## Acceptance criteria
- **AC-1** Given the Apple OAuth flow is started for `audience=customer`, when `StartAppleOAuth` runs, then the response contains an authorization URL pointing at Apple's `https://appleid.apple.com/auth/authorize` with `response_mode=form_post`, `scope=name email`, and a signed state parameter, and a CSRF cookie value is returned for the controller to set as `__Host-makables_oauth_csrf`.
- **AC-2** Given `audience=admin`, when `StartAppleOAuth` or `CompleteAppleOAuth` runs, then the request is rejected with the existing admin-OAuth-rejection error code (same as Google) — proof: a passing test `Rejects_admin_audience` in both handler test suites.
- **AC-3** Given Apple posts back to the callback with a valid `code` and `state`, when `CompleteAppleOAuth` runs, then the client secret is minted as a fresh ES256 JWT (`iss`=TeamId, `sub`=ClientId, `aud=https://appleid.apple.com`, `kid`=KeyId header) and used exactly once for the token exchange — proof: unit test asserting `AppleClientSecretSigner` output claims/header, and an integration test asserting the token-exchange request carries that JWT as `client_secret`.
- **AC-4** Given a returned Apple `id_token` with `email_verified=false` (as a string `"false"` — Apple sometimes sends this as a string, not a bool, per their docs quirk), when the profile is parsed, then the login is refused with `AuthOAuthEmailNotVerified` — proof: a passing test covering the string-vs-bool parsing edge case.
- **AC-5** Given an existing password-based user with email `X`, when they complete Apple OAuth with a verified email match on `X`, then `AppleSub` is linked to that user (not a duplicate created) — proof: passing test `Password_user_with_same_email_gets_AppleSub_linked_and_confirmed`, mirroring the Google equivalent.
- **AC-6** Given a first-time Apple authorization where Apple includes the one-time `user` form field with name, when `CompleteAppleOAuth` creates a new user, then `FullName` is populated from that field (falling back to a placeholder derived from email local-part if absent) — proof: passing test asserting both the with-name and without-name paths.
- **AC-7** Given a repeat Apple login (no `user` field present, Apple only sends it on first auth), when the user is resolved by `AppleSub` match, then login succeeds without requiring the name field — proof: passing test.
- **AC-8** Given a customer JWT minted via Apple OAuth for `audience=customer`, when that token is presented to `Web.Maker`, then the request is rejected per the existing per-host audience enforcement (no Apple-specific bypass) — proof: existing `JwtAuthMiddlewareTests` pattern extended or reused unchanged (Apple mints the exact same JWT shape as Google/password).
- **AC-9** Given the migration adding `apple_sub`, when it runs against a seeded database with existing users, then the column is nullable, the partial unique index excludes NULLs, and no existing row's data is altered — proof: migration test asserting schema shape (mirrors the `google_sub` index test if one exists) plus a clean `dotnet ef database update` run in CI.
- **AC-10** Given the login page and registration page, when rendered, then an Apple button appears visually adjacent to the existing Google button, uses an `auth.apple.*` i18n key for its label, and triggers `startAppleOAuth` (not a hardcoded URL) on click — proof: screenshot + component test asserting the wrapper call.
- **AC-11** Given the OpenAPI contract changes (two new endpoints), when the PR is prepared, then the NSwag-generated client in `frontend/src/lib/api-client/` is regenerated and committed in the same PR, and `npm run check:api` passes in CI — proof: CI green + diff includes the regenerated client files.
- **AC-12** Given `docs/architecture/patterns.md` §A.17 is stale ("Out of scope for MVP: OAuth (Google)"), when the architect amends ADR 0012 (or authors ADR 0026) for Apple, then the same PR (or an immediately-preceding architect PR) corrects that stale line to reflect both providers — proof: diff on `patterns.md` §A.17 alongside the ADR file.

## Technical notes

- **State signer reuse — resolved by architect (ADR 0026, Defense section)**: `IOAuthStateSigner` signs `Audience/RedirectUri/CsrfCookieHash/Nonce/IssuedAt` with no explicit `Provider` claim. Investigated whether a state minted by `StartGoogleOAuth` could be replayed against `CompleteAppleOAuth`. **Conclusion: not exploitable, no fix required before ship.** `OAuthStateSigner.TryVerify` (`backend/src/Makables.Infra.Common/Auth/OAuthStateSigner.cs:142`) exact-matches `RedirectUri` against the value presented at the callback; Google's and Apple's callback routes are distinct (`/api/public/auth/google/callback` vs `/api/public/auth/apple/callback` per this ticket's own scope), so a Google-minted state cannot pass Apple's redirect-URI check absent a configuration error (never point two providers at the same redirect URI). Even in that misconfiguration case, the `code` itself is provider-specific and non-transferable — a Google authorization code presented to Apple's token endpoint (or vice versa) is rejected with `invalid_grant` before any user-resolution logic runs, since `CompleteAppleOAuth` calls only `IAppleOAuthClient.ExchangeCodeAsync` against Apple's token endpoint. No `Provider` claim addition is required as a blocker; it remains an optional, non-blocking defense-in-depth polish item for a future fast-follow (not this ticket). Full reasoning in `docs/adr/0026-apple-oauth.md` Defense section and `docs/architecture/patterns.md` §A.17.1.
- **Apple JWKS caching**: Apple's `https://appleid.apple.com/auth/keys` should be cached with reasonable TTL (mirror however `GoogleJsonWebSignature.ValidateAsync` handles Google's JWKS caching internally — check if Apple's equivalent library, or a hand-rolled `System.IdentityModel.Tokens.Jwt` validation path, needs an explicit cache wrapper).
- **Vendor account manual step**: registering the Apple Developer "Services ID" + generating the private key (`.p8` file, Key ID) in the Apple Developer portal is a **manual_step** — actor: PM/Ops, timing: pre-merge (the backend needs `TeamId`/`KeyId`/`PrivateKeyPem` to even boot the feature in a non-degraded state), rollback: feature-flag or config-absent graceful degradation (Apple button hidden if `Auth:Apple` config section is unset — do not crash the host if Apple isn't configured, mirroring how a missing provider config should fail closed, not fail the whole app).
- **Secret rotation**: the Apple private key has no forced expiry from Apple's side but should be in the same Key Vault rotation playbook as other auth secrets (see `docs/runbooks/` from T-0134).
- Mirror `docs/adr/0012-authentication.md` "Account merging" section semantics exactly for Apple (email is still the convergence key).

## Files touched (expected)
- `backend/src/Makables.Core.Domain/Identity/IAppleOAuthClient.cs`, `AppleProfile.cs`
- `backend/src/Makables.Core.Domain/Identity/User.cs` (add `AppleSub`, `LinkAppleSub`)
- `backend/src/Makables.Infra.Clients/Apple/AppleOAuthOptions.cs`, `AppleClientSecretSigner.cs`, `AppleOAuthClient.cs`, `AppleOAuthException.cs`
- `backend/src/Makables.Core.AppServices/Features/Auth/StartAppleOAuth.cs`, `CompleteAppleOAuth.cs`
- `backend/src/Makables.Infra.Data/Migrations/<timestamp>_AddAppleSubToUser.cs`
- `backend/src/Makables.Infra.Data/Repositories/UserRepository.cs` (`GetByAppleSubAsync`)
- `backend/src/Makables.Web.*/Controllers/AuthController.cs` (Apple start/callback actions)
- `backend/src/Makables.Config/AddMakablesInfrastructure.cs`, `AddMakablesClients.cs` (DI registration)
- `frontend/src/app/(auth)/login/login-form.tsx`, `register/register-form.tsx`
- `frontend/src/lib/api-client-helpers/auth.ts`
- `frontend/src/lib/i18n/cs-CZ/*` (new `auth.apple.*` keys)
- `frontend/src/lib/api-client/` (NSwag regen output)
- `docs/adr/0012-authentication.md` or new `docs/adr/0026-apple-oauth.md` (architect-authored)
- `docs/architecture/patterns.md` §A.17 (architect-authored staleness fix)

## Test plan reference
`docs/test-plans/T-0139.md`

## Status log
- 2026-07-07 `draft` created by PM. Mirrors T-0026 (Google OAuth, done) scope for Apple. Awaiting architect input on ADR 0012 amendment vs new ADR 0026 and the state-payload provider-discriminator question (see Technical notes) before DoR gate can close and status moves to `ready`. Branch to use once work starts: `feat/T-0139-apple-oauth-login`.
- 2026-07-07 `ready` — architect pass complete. Authored new ADR `docs/adr/0026-apple-oauth.md` (does not amend accepted ADR 0012; Apple is additive under the same authentication model). State-payload provider-discriminator question investigated and refuted as non-exploitable: `OAuthStateSigner.TryVerify` (`backend/src/Makables.Infra.Common/Auth/OAuthStateSigner.cs:142`) exact-matches `RedirectUri`, and Google/Apple callback routes are structurally distinct, plus authorization codes are provider-specific and non-transferable between token endpoints — no `Provider` claim required as a blocker (optional non-blocking fast-follow only). Corrected `docs/architecture/patterns.md` §A.17 stale "OAuth (Google)"-only line and added §A.17.1 reusable OAuth-provider pattern. Ticket frontmatter and INDEX.md updated to `ready`.
