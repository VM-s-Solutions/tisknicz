# Gate 3 (Security) — admin-ops-bundle (T-0108..T-0111)

**Verdict: GATE3_FOLD** (conditional pass — two erasure-completeness gaps + one doc gap to fold before merge; no BLOCKER, no privilege-escalation hole)

Scope: 8 commits `2a9ee86..9e7f5c0` on `feat/order-cleanup-bundle`. Highest-privilege surface in the system: 4 admin mutations (incl. the only hard-delete + PII erasure) + 3 Unscoped cross-tenant reads. Reviewed against the 9 listed checks.

---

## Erasure-completeness assessment (the GDPR headline)

The **primary matrix is correct and complete**: User row hard-deleted (Email, EmailNormalized, PasswordHash, FullName, Phone, GoogleSub all gone with the row); Order contact snapshots → `"Anonymized"` (all three columns, `AnonymizeContact`); Review author → `"Anonymized"` (`CustomerUserId`, content retained de-identified); Maker PII → `"Anonymized"` (CompanyName/LegalForm/VatId/Bio/PickupNote) with IČO + BankAccount **lawfully retained** + `IsRetainedForLegal=true`; RefreshTokens + unreferenced Addresses hard-deleted. The seam runs in the caller's UoW (no `SaveChangesAsync`), guards in-flight first (belt-and-braces with the handler), and never loads Invoices.

**But two user-PII-bearing rows are NOT in the matrix and survive the hard-delete carrying personal data:**

- **MAJOR-1 — `OneTimeToken` survives carrying `UserId` + `IpAddress`.** Magic-link/reset/confirm tokens key on `UserId` and store `IpAddress` (IP = personal data, GDPR Recital 30). The seam deletes `RefreshToken` rows but not `OneTimeToken` rows for the user. After erasure a dangling `UserId` + an IP linkable to the erased subject persist. They are short-TTL and single-use, but erasure must purge them like refresh tokens (same "session/credential infra, no legal-retention case" rationale already applied to `RefreshToken`).
- **MAJOR-2 — `LoginAttemptBucket` survives keyed by the user's normalized email (PK).** The bucket Id IS the normalized email. After a hard-delete the erased subject's email lives on as a `login_attempt_bucket` PK. ADR 0012 keeps ghost buckets for never-existed emails (anti-enumeration), so a blanket retain is arguably defensible — but the erasure ticket/matrix never reasons about it. Either purge it or document the lawful-basis retain (legitimate-interest anti-abuse) in roles/user.md. Currently it is an undocumented, unconsidered PII residue.

Both are fixable with two `RemoveRange` passes (OneTimeToken: definite; LoginAttemptBucket: purge-or-document) + matrix-table entries in roles/user.md.

---

## Retain-correctness (check 2) — PASS

Invoices touched by zero code in the seam (verified: never loaded for mutation; the only Invoice reads are the T-0111 Unscoped admin list, read-only). `Invoice.RecipientName`/`RecipientEmail` carry customer PII but their retention is the deliberate Art. 17(3)(b) tax-record exemption — correct. IČO/BankAccount retention is explicit, flagged (`IsRetainedForLegal`), and `AnonymizeForErasure` is idempotent — not an accidental leak.

---

## Findings by severity

**BLOCKER:** none.

**MAJOR:**
- M-1 OneTimeToken PII residue (above) — incomplete erasure.
- M-2 LoginAttemptBucket email residue (above) — undocumented PII retention.

**MINOR:**
- m-1 Audit AfterJson of a hard-deleted user. `AdminAuditPipelineBehavior` captures `afterJson` after the handler stages the delete; `FindAsync` returns the change-tracker's Deleted-state entity, so AfterJson serialises the about-to-vanish user's FullName/Email/Phone (PasswordHash IS redacted). The ticket §231 explicitly elects this as the forensic record, and the row is admin-host-only + the list DTO omits the JSONB — so this is a *sanctioned accountability retention* (Art. 5(2)/30), not a leak. Recommend a one-line note in roles/admin-audit-log-entry.md that erasure before/after JSONB is a deliberate forensic retain. Not blocking.
- m-2 `AdminAuditLogWriter` uses `dynamic` (CLAUDE.md "no dynamic"). **Out of scope** — pre-existing (T-0011, commit 03f5991), not in this diff. Flag for a separate cleanup ticket.

---

## Authz / Unscoped assessment (checks 3,5,6,7,8) — PASS (airtight)

- **Unscoped exposure (T-0111):** `AcceptedAudiencesFor(Admin) => [Admin]` only — a customer/maker JWT presented to Web.Admin fails audience validation → 401, never data. All three Unscoped queries live solely on `AdminQueriesController` in `Makables.Web.Admin` (grep-confirmed: single file); no Unscoped endpoint mounted on Customer/Maker/Public. Audit-log list DTO omits before/after JSONB. Soft-deleted/anonymised orders surfaced intentionally (single commented `IgnoreQueryFilters`); invoice + audit queries correctly do NOT ignore filters.
- **Erasure authz + interlock (T-0110):** admin-host + `[Authorize]`; fail-closed session (`Error.Unauthorized()` when no `sub`, never "system"); retype gate (NFC/case-insensitive email match); in-flight interlock pre-flighted in handler AND re-guarded in seam over both customer- and maker-side orders. POST `/erase` (honest non-idempotent verb); re-call → `user.notFound` (no Silent-Success). No customer/maker path can reach it.
- **T-0108:** provider change is admin-only; retype gate (`ConfirmationMatches`, payment-first) + unregistered-code reject before mutation; provider codes are not secrets; no secret in config/response.
- **T-0109:** retry/acknowledge admin-only + fail-closed; `RetryOutboxEventResponse`/`AcknowledgeOutboxEventResponse` expose only id/retry-count/timestamps/acknowledger — `PayloadJson` NOT exposed; retry on drained row → clean 409 (no malicious replay/suppress by non-admin).
- **Q-0011 (rate-limit):** all admin endpoints are admin-JWT-gated (2 trusted users); no new unprotected surface. Re-affirm Q-0011 stays OPEN as a customer/maker-host follow-up.

## Audit + IAdminAuditableCommand (checks 4,9) — PASS

All 4 mutations implement `IAdminAuditableCommand` with explicit ActionCode/TargetEntity/TargetId/Notes; all 4 fail-closed (no "system" attribution). `admin_audit_log` is append-only with a Postgres trigger rejecting UPDATE/DELETE (immutable); `TargetId` is a string, not an FK, so the `user.erase` row survives the deletion and references the now-gone id — the accountability record holds.

---

## Required to convert FOLD → PASS
1. Purge `OneTimeToken` rows in `UserDataDeletionService.EraseAsync` (M-1).
2. Purge OR document-with-lawful-basis the `LoginAttemptBucket` (M-2); update roles/user.md erasure matrix.
3. (Recommended) one-line note that erasure audit JSONB is a deliberate forensic retain (m-1).
