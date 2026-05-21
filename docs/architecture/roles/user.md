---
role: User
kind: aggregate
status: accepted
---

# User

## Responsibility

Represent the identity behind every action on the platform. Owns credentials, role assignment, email confirmation status, and primary country.

## Collaborators

- **AuthService** (uses: password hashing, JWT issuance, magic-link / OAuth flows)
- **EmailProvider** (asks via outbox: send confirmation / reset / magic-link emails)
- **Maker** (1:1 if the user is a maker; otherwise null)

## Knows

- Email (`EmailNormalized` is the canonical unique key)
- `PasswordHash` (Argon2id) — null if user uses only OAuth or magic link
- `EmailConfirmedAt`
- Role (`customer | maker | admin`) — single role at launch (Q-open for multi-role)
- `FullName`, `Phone`
- `CountryCodePrimary` — drives default language and JWT claim
- `GoogleSub` if linked
- Lockout state (`FailedLoginCount`, `LockedUntil`)
- Refresh-token family (via separate `RefreshToken` aggregate referencing UserId)

## Does NOT know

- Which orders, products, or messages exist for this user (those are queried from the respective aggregates)
- Session state in detail — `RefreshToken` is a separate aggregate
- Maker-specific data (bio, IČO, bank account) — that's on `Maker`

## Lifecycle

- **Created by:**
  - `RegisterCustomer.Command` (email + password)
  - `MagicLinkLogin.Command` (first-time magic link from a never-registered email)
  - `GoogleOAuthCallback.Command` (first-time Google login)
  - `RegisterMaker.Command` (in collaboration with `Maker` aggregate)
- **Modified by:**
  - `UpdateProfile.Command` (user action: name, phone)
  - `ConfirmEmail.Command` (link consumption)
  - `ChangePassword.Command` (user action)
  - `ResetPassword.Command` (forgot-flow)
  - `LinkGoogleAccount.Command` (auto on first Google login if email exists)
  - `RecordLoginFailure` / `ClearLoginFailures` (`AuthService` internal)
- **Persisted by:** `IUserRepository`
- **Destroyed by:** `DeleteUserPermanently.Command` (admin GDPR action; audited; cascades to `Maker` if any; anonymizes related orders)

## Invariants

- `EmailNormalized` is unique across all active users.
- `Role` is immutable after creation. Role changes require admin tooling (post-MVP).
- A user with `Role = maker` has exactly one `Maker` aggregate.
- A user with `Role = admin` cannot authenticate via Google OAuth (ADR 0012).
- Password reset revokes all existing refresh tokens.

## Implementation pointer

`backend/src/Makables.Core.Domain/Users/User.cs`.

## Related

- ADRs: 0012 (authentication), 0013 (soft delete + GDPR hard delete), 0014 (admin actions audited)
- Roles: `auth-service`, `maker`, `email-provider`
