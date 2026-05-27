---
id: T-0036
title: Backend profile commands + frontend customer + maker dashboard profile pages
status: done
size: M
owner: dotnet-backend + frontend
created: 2026-05-26
updated: 2026-05-26
depends_on: [T-0034, T-0035]
blocks: []
adrs: [0012, 0014, 0018]
phase: 2
---

# T-0036 — Dashboard profile

## Scope

Like T-0035, the original ticket was frontend-only but the dependent backend handlers didn't exist yet. T-0036 was expanded to include:

### Backend (`Makables.Core.AppServices/Features/Profile/`)
- `GetMyProfile.cs` — `Query` returning `Response(UserId, Email, FullName, Phone?, CountryCodePrimary, Role, EmailConfirmed, PreferredLanguage?)`. Target resolved from `IUserSessionProvider.GetUserId()` — no userId in the request shape (IDOR shield, same pattern as T-0034 `UpdateMakerProfile`).
- `UpdateUserProfile.cs` — patches `User.UpdateProfile(fullName, phone)` for the authenticated caller. Email is NOT editable here (separate "change email" flow needs re-confirmation + refresh-family invalidation).
- `ChangePassword.cs` — verifies current password via `IPasswordHasher.Verify`, then stores a new hash via `User.SetPasswordHash`. Returns `auth.currentPasswordWrong` (Unauthorized) on mismatch. OAuth-only accounts (no `PasswordHash`) get the same response so the password-presence is not enumerable.

### Backend (`Makables.Core.AppServices/Features/Maker/`)
- `GetMyMakerProfile.cs` — `Query` returning the full Maker shape including the T-0034 maker-editable fields (`Bio`, `BankAccount`, `PersonalPickupEnabled`, `PickupNote`) AND the ARES snapshot (`RegistrationNumber`, `CompanyName`, `VatId`, `LegalForm`, `IsActiveInRegistry`, `IsVerified`, `SnapshotIsStale`, `SnapshotFetchedAt`) so the dashboard can render both sections in one round-trip. Resolved from session — IDOR shield.

### Backend (`Makables.Config/Controllers/Profile/`)
- `ProfileController.cs` — `[Authorize]`-gated, shared across hosts via Config. Five endpoints under `/api/v1/me`:
  - `GET /api/v1/me` → `GetMyProfile.Query`.
  - `PUT /api/v1/me` → `UpdateUserProfile.Command`.
  - `POST /api/v1/me/change-password` → `ChangePassword.Command`.
  - `GET /api/v1/me/maker` → `GetMyMakerProfile.Query` (returns 404 on the customer host because the User has no maker row).
  - `PUT /api/v1/me/maker` → `UpdateMakerProfile.Command` (T-0034).

### Frontend (`frontend/src/app/(customer)/dashboard/zakaznik/profile/`)
- `page.tsx` — Server Component shell, h1 + max-w-2xl wrapper.
- `profile-client.tsx` — three sections:
  - **Personal info** — name + phone form. Email shown read-only with a "contact support to change" hint.
  - **Password** — current + new. Maps `auth.currentPasswordWrong` to a localized message.
  - **Logout** — single button. Calls `logout('customer')` and routes to `/auth/login`.

### Frontend (`frontend/src/app/(maker)/dashboard/maker/profil/`)
- `page.tsx` — Server Component shell, h1 + max-w-3xl wrapper.
- `profile-client.tsx`:
  - **Company section** (read-only) — IČO / DIČ / company name / legal form from the ARES snapshot. Verification badge + stale-snapshot warning banner.
  - **About** — `Textarea` for bio (≤500).
  - **Bank account** — `Input` with the canonical `2000145399/0100` placeholder; maps `validation.bankAccountFormat` to a localized message.
  - **Personal pickup** — checkbox toggle + a note textarea (the note is disabled when the toggle is off).

### Frontend (`frontend/src/lib/api-client-helpers/profile.ts`)
- Hand-written wrappers around `/api/v1/me/*`: `getMyProfile`, `updateMyProfile`, `changePassword`, `getMyMakerProfile`, `updateMyMakerProfile`. Same NSwag-deferral story as T-0035.

### i18n
- ~40 new keys under `dashboard.customer.profile.*` and `dashboard.maker.profile.*`.

### Out of scope
- **Categories editor** — needs the Category entity (T-0040). Deferred per the T-0034 scope reduction.
- **Pickup address management** — only the toggle + note ship in T-0034. Setting an actual `Address` row separate from the legal seat is deferred to the address-graph work.
- **Email change** — requires re-confirmation, refresh-family invalidation, and a UI flow with its own anti-abuse rate limit. Tracked as a follow-up.
- **Preferred language** — `User.PreferredLanguage` is in the read response but not exposed in the UI. The language toggle ships with the i18n-localization ticket (T-0130 placeholder).
- **Logout-other-sessions** — `ChangePassword` does not invalidate other refresh families. Tracked for the security-hardening ticket.
- **Handler tests** — backend tests for the four new handlers are deferred. The handlers are 30-line orchestrators that mirror the well-tested T-0034 pattern. A follow-up should add the standard ~6 facts per handler (NotFound / Unauthorized / happy-path / etc.).

## Acceptance criteria
- **AC-1** Authenticated customer can load `/dashboard/zakaznik/profile`, see name / phone / email, edit name + phone, save, see the success toast.
- **AC-2** Authenticated customer can change their password by typing the current + new; current-password mismatch returns `auth.currentPasswordWrong` mapped to a localized message.
- **AC-3** Customer can log out — backend clears cookies, frontend routes to `/auth/login`.
- **AC-4** Authenticated maker can load `/dashboard/maker/profil`, see ARES snapshot in read-only mode + verification + stale-snapshot status badges.
- **AC-5** Maker can edit bio (≤500), bank account (ČNB-format-validated), pickup toggle, and pickup note. Server-side `CzechBankAccountValidator` failure maps to a localized message.
- **AC-6** All endpoints resolve the target user/maker from `IUserSessionProvider.GetUserId()`; the Command shapes have NO `userId` / `makerId` field (IDOR shield).
- **AC-7** No user-facing English strings; every visible string keyed through `lib/i18n/cs-CZ`. Brand wordmark exempt.
- **AC-8** TypeScript clean (`tsc --noEmit`); ESLint clean (`eslint src/`). Backend build clean; 670 tests still pass.

## Status log
- 2026-05-26 done. Backend: 4 new feature files (`GetMyProfile`, `UpdateUserProfile`, `ChangePassword`, `GetMyMakerProfile`) + `ProfileController` (5 endpoints). Build clean, 670 tests still pass. Frontend: 2 dashboard pages (customer + maker) + `profile.ts` helper + ~40 new i18n keys. `tsc` + `eslint` clean. Awaiting dual reviewer per workflow.
