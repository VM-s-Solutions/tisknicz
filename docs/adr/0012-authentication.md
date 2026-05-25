---
id: 0012
title: Authentication — email/password + magic link + Google OAuth; Argon2id; rotating refresh tokens
status: accepted
date: 2026-05-21
deciders: [Architect, user]
---

# 0012 — Authentication

## Context

Per ADR 0007 we own auth end-to-end (no Supabase Auth, no Auth0, no Clerk). The user wants Google OAuth from day one in addition to email/password and magic link. We need a concrete policy: password rules, hashing parameters, token lifetimes, refresh-token rotation, lockout, OAuth audience separation, email confirmation.

## Decision

### Identity providers at launch
1. **Email + password** (Argon2id-hashed).
2. **Magic link** (one-time email-based login).
3. **Google OAuth** (Sign in with Google).

All three converge on the same `User` record. Email is the primary identity key.

### User entity

```csharp
public class User : Auditable
{
    public string Email { get; private set; } = default!;
    public string EmailNormalized { get; private set; } = default!;   // lowercase, NFC-normalized; unique
    public string? PasswordHash { get; private set; }                  // null if user only uses OAuth or magic link
    public DateTimeOffset? EmailConfirmedAt { get; private set; }
    public UserRole Role { get; private set; } = UserRole.Customer;
    public string FullName { get; private set; } = default!;
    public string? Phone { get; private set; }
    public string CountryCodePrimary { get; private set; } = default!;  // user's primary country; drives default language

    // External identities
    public string? GoogleSub { get; private set; }                     // Google's `sub` claim; unique if present

    // Lockout
    public int FailedLoginCount { get; private set; }
    public DateTimeOffset? LockedUntil { get; private set; }
}
```

### Password policy

- **Algorithm:** Argon2id via `Konscious.Security.Cryptography.Argon2`.
- **Parameters:** memorySize 65536 KB (64 MiB), iterations 3, parallelism 1. Targets ~100 ms per hash on App Service B2. Reviewed yearly.
- **Storage format:** versioned, e.g. `argon2id$v=19$m=65536,t=3,p=1$<salt-b64>$<hash-b64>`. The version prefix lets us migrate parameters later by re-hashing on next login.
- **Rules:** minimum 10 characters; no other complexity requirement (matches NIST 800-63B guidance and the user's directive). Passwords are checked against the top-100 list of breached passwords (kept as a static file; refreshed quarterly) — if matched, registration is refused with `auth.passwordTooCommon`.
- **No periodic forced reset.** Reset only on user request or compromise.

### Magic link

- Endpoint: `POST /api/public/auth/magic-link/request { email }`.
- Backend generates an opaque random token (32 bytes, URL-safe base64); stores SHA-256 of the token; emails the user a link with the raw token.
- TTL: **15 minutes**. Single-use. Consumed (deleted) on first successful exchange.
- Endpoint: `POST /api/public/auth/magic-link/consume { token }` returns a session (access + refresh tokens) on success.
- Rate limit: 3 magic-link requests per email per 10 minutes.
- If the email doesn't exist, return 200 with a no-op (don't leak existence). Send no email.

### Google OAuth

- Backend manages the OAuth flow (server-side; no SPA-only flow).
- `GET /api/public/auth/google/start?audience={customer|maker|admin}` → redirect to Google with state.
- `GET /api/public/auth/google/callback?code=...&state=...` → exchange code, fetch profile, match on `EmailNormalized`:
  - If matched: link `GoogleSub` to the existing user (if not already set).
  - If new: create a `User` with role inferred from the `audience` query parameter and `EmailConfirmedAt = now()` (Google has already verified).
- Audience query parameter is signed in the state to prevent role escalation.
- Disallow Google OAuth for `admin` audience at the proxy level — admins must use password + (future) MFA.

### JWT structure

```
header: { "alg": "HS256", "typ": "JWT" }
claims: {
  "sub": "<userId>",
  "email": "<email>",
  "role": "customer|maker|admin",
  "country_code": "CZ",
  "aud": "customer|maker|admin",
  "iss": "https://makables.cz",
  "iat": <unix>,
  "exp": <unix>,             // iat + 15 min
  "jti": "<random-uuid>"
}
```

- **Access token TTL: 15 minutes.**
- Signing key: HS256 with a strong secret in Key Vault; key rotation supported via a `kid` claim and a key-id-to-secret map (next ADR will detail rotation).
- Audience claim is enforced per host: `Web.Customer` accepts only `aud=customer` (or `aud=admin`); `Web.Maker` accepts only `aud=maker` (or `aud=admin`); `Web.Admin` accepts only `aud=admin`; `Web.Public` accepts any of the three (it is the anonymous + any-authenticated surface — protected endpoints on Public MUST mount a named policy that checks `role` / `aud` explicitly; bare `[Authorize]` is not enough). Admins can call any audience host.
- The **runtime source of truth** for the per-host audience table is `MakablesAuthExtensions.AcceptedAudiencesFor` in `Makables.Config` and is pinned by `JwtAuthMiddlewareTests` (T-0027). If this narrative ever disagrees with that method, the method wins.
- A user with multiple roles (rare — only admins who are also customers/makers) gets one JWT per audience by re-authenticating against the audience-specific login endpoint. We never mint multi-audience JWTs.

### Refresh token

- **TTL: 30 days.** Rotated on every successful use.
- Stored on the server as `SHA-256(rawToken)` in `RefreshToken` table.
- Delivered to the client as an **HttpOnly, Secure, SameSite=Strict cookie** on `.makables.cz`. The client never reads it directly; it ships automatically with `/auth/refresh` requests.
- Reuse detection: if a refresh token is used twice (we mark `RevokedAt` on first use and `ReplacedByTokenId`), the entire token family is revoked — all of that user's sessions invalidated. This catches stolen-token replay.

```csharp
public class RefreshToken : Auditable
{
    public string UserId { get; private set; } = default!;
    public string TokenHash { get; private set; } = default!;      // SHA-256 of the opaque token
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public string? ReplacedByTokenId { get; private set; }         // for reuse-detection chain
    public string FamilyId { get; private set; } = default!;       // shared across rotations of the same login
    public string? UserAgent { get; private set; }
    public string? IpAddress { get; private set; }
}
```

### Lockout

- **5 consecutive failed password attempts** for the same email → lock the account for **15 minutes**. Failed attempt counter resets on success.
- The lockout is keyed on `User.EmailNormalized`. If the email doesn't exist, we still consume "ghost" lockout slots (rate limit by `EmailNormalized` even if user is missing) to prevent enumeration.
- Magic link and Google OAuth bypass password lockout but are themselves rate-limited.

### Email confirmation

- Customers can browse and add to cart without email confirmation.
- **Email confirmation is required before placing an order.** Order placement returns `auth.emailNotConfirmed` if missing.
- Makers must confirm email before activating the maker profile (i.e. before products go live in the catalog).
- Confirmation token: same shape as magic-link token. TTL 24 hours. Single-use.

### Password reset

- `POST /api/public/auth/reset/request { email }` → email a one-time reset token. Same shape as magic-link; TTL 1 hour; single-use; rate-limited.
- `POST /api/public/auth/reset/confirm { token, newPassword }` → set the new password, revoke all active refresh tokens for the user (force re-login everywhere).

### Account merging

If a user has password + Google OAuth, both link to the same `User` row (by email). The first time a Google OAuth login happens for an existing password user, we link the `GoogleSub` and notify the user via email ("Google account linked").

## Alternatives considered

- **bcrypt instead of Argon2id** — rejected. Argon2id is the modern recommendation (memory-hard, side-channel-resistant). bcrypt is fine but Argon2id is better and well-supported in .NET.
- **JWT in localStorage** — rejected. XSS exposure. In-memory access tokens + HttpOnly cookie for refresh is the OWASP-recommended pattern.
- **Stateless refresh tokens (JWT)** — rejected. We need server-side revocation (logout, lockout, reuse detection). DB-backed refresh tokens are mandatory.
- **No refresh rotation** — rejected. Rotation enables reuse detection, which catches stolen-token attacks.
- **Allow Google OAuth for admins** — rejected. Admins handle money and customer data; we want explicit password + (future) MFA, not delegated trust.
- **Skip the password-blocklist** — rejected. Catching the top-100 breached passwords is cheap and prevents trivial account takeovers.

## Consequences

### Positive
- Three login paths cover every reasonable user preference.
- Argon2id with proper parameters resists offline attacks for ~5 years (re-tune annually).
- Refresh-token rotation + reuse detection catches the most common token-theft attack class.
- Audience-bound JWTs prevent role confusion (a customer JWT cannot be replayed against the admin API).
- Email confirmation gate on order placement prevents fraud at the point where it matters; doesn't block browsing.

### Negative
- Custom auth is real code: ~1500 LOC across User entity, AuthService, controllers, middleware, validators, tests. Mitigation: heavily integration-tested; threat-modeled.
- Google OAuth adds a Google Cloud project to operations and a vendor dependency. Mitigation: thin wrapper; can be disabled with one config change if Google becomes a problem.
- Refresh token rotation requires careful handling on the client (don't fire two concurrent refresh calls). Mitigation: client-side `lib/auth/refresh.ts` serializes refresh requests with a single-flight pattern.

## Compliance / verification

- SecOps audit: password hashing parameters match this ADR; bumped on each yearly review with a new ADR.
- SecOps audit: refresh tokens stored only as hashes; raw token never logged.
- SecOps audit: JWT audience enforced in middleware; integration test confirms `aud=customer` rejected by `Web.Maker`.
- SecOps audit: reuse detection — using a revoked refresh token revokes the family.
- SecOps audit: rate limits in place on every public auth endpoint (`/auth/login`, `/auth/magic-link/request`, `/auth/reset/request`).
- Integration test: lockout after 5 failures; release after 15 minutes.
- Integration test: email confirmation required for order placement.

## Related
- Patterns: §A.17 Custom authentication
- ADR 0005 — Per-audience route groups (JWT `aud` claim matches per-host policy)
- ADR 0007 — Stack pivot (Supabase Auth removed)
- Next: ADR for JWT signing-key rotation and Key Vault integration (SecOps owns)
