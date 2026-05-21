---
role: AuthService
kind: application-service
status: accepted
---

# AuthService

## Responsibility

Orchestrate user authentication: registration, login, refresh-token rotation, magic-link issue/consume, Google OAuth, password reset, password change, lockout enforcement.

## Collaborators

- **User** (reads: credentials, lockout state; writes: hash, confirmed-at, Google linkage)
- **RefreshToken** (creates, rotates, revokes families)
- **PasswordHasher** (Argon2id wrapper)
- **JwtIssuer** (issues access tokens with audience claim)
- **EmailProvider** (via outbox: confirmation, magic-link, reset emails)
- **GoogleOAuthClient** (`Infra.Clients.Google` adapter)

## Knows

- The token lifetimes from configuration (15-min access, 30-day refresh)
- The lockout policy (5 failures → 15 min)
- The audience-to-host mapping (`Web.Customer` accepts `aud=customer | admin`, etc.)
- That admin role cannot use Google OAuth

## Does NOT know

- Why the user is logging in (intent / next page is the frontend's concern)
- Anything about orders, products, makers
- The HTTP layer (this is a service; controllers in `Web.*` call it)

## Operations

```csharp
Task<BusinessResult<AuthSession>> RegisterAsync(RegisterRequest)
Task<BusinessResult<AuthSession>> LoginAsync(email, password)
Task<BusinessResult<AuthSession>> RefreshAsync(refreshToken)        // rotates
Task<BusinessResult> LogoutAsync(refreshToken)                       // revokes
Task<BusinessResult> SendMagicLinkAsync(email)                       // queues outbox event
Task<BusinessResult<AuthSession>> ConsumeMagicLinkAsync(token)
Task<BusinessResult<AuthSession>> CompleteGoogleOAuthAsync(code, audience)
Task<BusinessResult> SendPasswordResetAsync(email)                   // queues outbox event
Task<BusinessResult> ConfirmPasswordResetAsync(token, newPassword)
Task<BusinessResult> ChangePasswordAsync(userId, currentPw, newPw)
Task<BusinessResult> SendEmailConfirmationAsync(userId)              // queues outbox event
Task<BusinessResult> ConfirmEmailAsync(token)
```

`AuthSession` = `{ accessToken, refreshToken, accessTokenExpiresAt }`.

## Invariants

- Refresh tokens are stored as SHA-256 hashes; raw value never persisted, never logged.
- Refresh-token reuse triggers family revocation.
- Magic-link / reset / confirmation tokens are single-use; consumed atomically.
- After password reset, all of the user's refresh tokens are revoked.
- A blacklisted password (top-100 breached list) is rejected at registration / change with `auth.passwordTooCommon`.

## Implementation pointer

`backend/src/Makables.Core.AppServices/Authentication/AuthService.cs`. Per ADR 0012, the implementation is split across:
- `Core.Domain/Authentication/IAuthService.cs` (interface)
- `Infra.Common/Authentication/AuthService.cs` (impl — uses `IPasswordHasher`, `IJwtIssuer`, `IUserRepository`, `IRefreshTokenRepository`)
- `Infra.Common/Authentication/Argon2PasswordHasher.cs`
- `Infra.Common/Authentication/JwtIssuer.cs`
- `Infra.Clients/Google/GoogleOAuthClient.cs`

## Related

- ADRs: 0005 (per-audience JWT), 0012 (this ADR defined the policy), 0013 (GDPR cascade)
- Roles: `user`, `email-provider`
