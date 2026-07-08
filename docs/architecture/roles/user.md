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
- `AppleSub` if linked (T-0139, mirrors `GoogleSub`)
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
  - `CompleteAppleOAuth.Command` (first-time Apple login, T-0139 — mirrors `GoogleOAuthCallback`)
  - `RegisterMaker.Command` (in collaboration with `Maker` aggregate)
- **Modified by:**
  - `UpdateProfile.Command` (user action: name, phone)
  - `ConfirmEmail.Command` (link consumption)
  - `ChangePassword.Command` (user action)
  - `ResetPassword.Command` (forgot-flow)
  - `LinkGoogleAccount.Command` (auto on first Google login if email exists)
  - `LinkAppleSub.Command` (auto on first Apple login if email exists, T-0139 — mirrors `LinkGoogleAccount`)
  - `RecordLoginFailure` / `ClearLoginFailures` (`AuthService` internal)
- **Persisted by:** `IUserRepository`
- **Destroyed by:** `DeleteUserPermanently.Command` (admin GDPR hard-delete, T-0110 / US-admin-0016; audited; the ONLY genuinely irreversible command in the system). The handler is a thin orchestrator — fail-closed session → load Unscoped → retype gate → in-flight interlock — and delegates the whole destructive matrix to the `IUserDataDeletionService` seam (see below).

## GDPR erasure — `IUserDataDeletionService` relationship

The `User` is the **anchor row** of the erasure seam (patterns §A.23, extension-points §14). `DeleteUserPermanently.Handler` owns the gates; the architect-owned `IUserDataDeletionService` (`Core.Domain.Privacy`, impl `Infra.Database.Privacy.UserDataDeletionService`) owns the disposition matrix and runs the entire pass — guard → anonymize → hard-delete → retain — inside the command's **single pipeline UoW**. The seam never calls `SaveChangesAsync`; the UoW pipeline commits everything (anonymizations + hard-deletes) atomically, or nothing. It is the only place EF Core `Remove()` runs against `User` data (ADR 0013 §"Hard delete (GDPR)").

**In-flight interlock (runs FIRST, before any mutation).** Both the handler and the seam (belt-and-braces) block when the user — as customer OR as maker-seller — has any order in `[PendingPayment, Paid, Accepted, Shipped, Disputed]` → `user.cannotDeleteWithInFlightOrders`. `Disputed` is in the block set because a disputed order holds escrowed money + an unresolved adjudication; erasing the subject mid-dispute is unsafe (dispute must resolve first).

**Full erasure matrix (the documented contract):**

| Entity | Disposition | Detail |
|---|---|---|
| `User` | **HARD-DELETE** | The anchor row. The only user hard-delete in the system. Gone after the first run. |
| `RefreshToken` | **HARD-DELETE** | All tokens for the user (session credentials; no legal-retention case). |
| `OneTimeToken` | **HARD-DELETE** | Magic-link / reset / confirm tokens keyed on `UserId` — carry an `IpAddress` (IP is personal data, GDPR Recital 30). Same credential-infra tier as `RefreshToken`; no retention value. |
| `LoginAttemptBucket` | **HARD-DELETE** | Anti-abuse bucket(s) whose PK **is** the erased user's `EmailNormalized`. Once the `User` row is gone the bucket is orphaned state carrying the subject's email as PII; purged by email (no lawful-basis retain since the key is the user's email). |
| `Address` | **HARD-DELETE** if unreferenced | Deleted only when no live maker still references it (the maker legal-seat address stays). The referencing check is a SQL `NOT EXISTS` anti-join — only the user's own addresses are evaluated. |
| `Order` contact snapshot | **ANONYMIZE** | Contact PII → `"Anonymized"` (replace-in-place sentinel). The order itself is retained (legal/commercial record). |
| `Review` author | **ANONYMIZE** | `CustomerUserId` author identity → `"Anonymized"`; the review text/rating stays (it's about the maker). |
| `Maker` PII | **ANONYMIZE + flag** | PII → `"Anonymized"`; **RETAIN `IČO` + `BankAccount`** (referenced by retained tax records); set `IsRetainedForLegal = true` (see `maker.md`). |
| `Invoice` | **RETAIN UNTOUCHED** | GDPR Art. 17(3)(b) legal-obligation exemption — immutable tax records. Never loaded for mutation; the invoice repo exposes no `Update`/`Delete`. |

**Irreversible — no Silent-Success re-call.** One-shot: after the first run the `User` anchor row is gone, so a second call returns `user.notFound` (not a benign re-success). The seam is not retry-safe by design; the in-flight guard runs before any irreversible mutation, so a guarded rejection erases nothing.

## Invariants

- `EmailNormalized` is unique across all active users.
- `Role` is immutable after creation. Role changes require admin tooling (post-MVP).
- A user with `Role = maker` has exactly one `Maker` aggregate.
- A user with `Role = admin` cannot authenticate via Google or Apple OAuth (ADR 0012, ADR 0026) — admins use password + (future) MFA only.
- Password reset revokes all existing refresh tokens.

## Implementation pointer

`backend/src/Makables.Core.Domain/Users/User.cs`.

## Related

- ADRs: 0012 (authentication), 0013 (soft delete + GDPR hard delete), 0014 (admin actions audited), 0026 (Apple OAuth)
- Patterns: §A.23 (orchestrated multi-entity GDPR erasure in one UoW); §A.23 erasure-FK note (denormalized author id, no User FK — `Order.CustomerUserId` + `Review.CustomerUserId`)
- Extension points: §14 (`IUserDataDeletionService` seam + erasure matrix)
- Roles: `auth-service`, `maker`, `email-provider`, `order`, `review`
