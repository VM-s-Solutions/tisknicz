---
id: T-0178
title: "Admin user lookup backing the GDPR erase (verified identity, honest not-found)"
status: done
size: M
owner:
created: 2026-08-21
updated: 2026-08-21
depends_on: []
blocks: []
user_stories: [US-admin-0012]
adrs: [0013, 0014, 0022]
phase: 8
manual_steps: [nswag-regen]
security_touching: true
layers: [dotnet-backend, frontend, l10n, secops]
---

# T-0178 — Admin user lookup backing the GDPR erase

## Context
Audit findings [ADM-H1, ADM-M9](../review/ux-functional-audit-2026-08-21.md). The "Uživatelé"
section is a blind erase form: the admin pastes a user GUID **and** email obtained from the DB or
logs, the "lookup" phase verifies nothing (the type-the-email interlock matches the email the admin
themselves typed), and a typo'd id reports "uživatel již byl smazán" — a false GDPR-compliance
signal. The strongest destructive flow in the product must run against a server-verified identity.

**Scope decision taken (defensible default):** a *lookup-by-email/id* endpoint only — no full
user-browse/list page at MVP (2 admins, low volume; browsing is a v1.1 concern). Recorded here so
the sweep isn't blocked on a product call; revisit if ops asks for browsing.

## Scope
- **Backend:** `GET /api/v1/admin-users/lookup?email=…` (or `?id=…`) on Web.Admin →
  `AdminUserLookupResponse` (id, email, audience/roles, isActive/erasure state, createdAt,
  in-flight order count via the existing `customerUserId` orders filter). AsNoTracking, Unscoped,
  `[Authorize]` admin, globally-unique Response name (T-0111 `IAdminQueries` precedent). 404 reuses
  `UserNotFound`-class code — no new code if one exists.
- **PII-read audit:** lookup is a privileged PII read → `IAdminReadAuditWriter` entry
  (`user.lookup`), matching the T-0137 policy for high-signal reads.
- **Frontend:** phase 1 of the erase flow becomes a real lookup; the confirm screen displays the
  **server-resolved** identity (email, state, in-flight orders) and the interlock matches against
  it; distinct copy for "not found" vs "already erased" (fixes ADM-M9); in-flight orders pre-disable
  the erase with the existing explanation.
- NSwag admin regen; cs-CZ keys.

## Alternatives Considered
- **Full users list page with search/pagination** — deferred (default above): larger surface,
  larger PII exposure, no current operator need beyond erase + support lookups.
- **Reuse the erase endpoint's own validation as the "lookup"** — rejected: a destructive endpoint
  must not be the probe.

## Out of scope
- Any change to the erase command itself (T-0110). User browsing/administration beyond lookup.

## Acceptance criteria
- **AC-1** Given a valid email, when looked up, then the server-resolved identity renders and the
  confirm interlock matches against it — not against admin-typed text (vitest + integration).
- **AC-2** Given an unknown email/id, when looked up, then the copy says the user was not found —
  never "already deleted" (negative-path test asserting the error constant).
- **AC-3** Given a user with in-flight orders, when resolved, then erase is pre-disabled with the
  in-flight explanation.
- **AC-4** Given a successful lookup, then one `admin_audit_log` row with action `user.lookup`
  exists; a 404 writes none (T-0137 pattern tests).
- **AC-5** A customer/maker JWT calling the endpoint is rejected (audience test); `npm run
  check:api` passes after regen.

## Technical notes
Follow `IAdminQueries` + `IAdminReadAuditWriter` precedents (T-0111/T-0137). Uniform-failure rules
do not apply here (authenticated admin surface) but PII stays out of logs. The in-flight count
composes over the T-0127 `customerUserId` filter — no new order read.

## Files touched (expected)
- `backend/src/Makables.Core.AppServices/Features/Admin/**` (lookup query + tests)
- `backend/src/Makables.Infra.Database/**`, `backend/src/Makables.Web.Admin/**`
- `frontend/src/app/(admin)/dashboard/admin/users/**`
- `frontend/src/lib/api-client/admin-api.v1.ts` (regen), `frontend/src/lib/i18n/cs-CZ.ts`

## Test plan reference
`docs/test-plans/T-0178.md`

## Status log
- 2026-08-21 `draft → ready` (Phase 8 UX sweep plan; DoR checklist passed; lookup-only scope
  default recorded in Context)
- 2026-08-22 `ready → in_progress`, branch `feat/admin-read-integrity-bundle` (bundled with its
  sibling — one admin host, one NSwag regen)
- 2026-08-22 `in_progress → in_review` — backend 2091/2091 (+9), frontend tsc clean + vitest
  215/215; NSwag admin regen; see [test plan](../test-plans/T-0177-T-0178.md) +
  [review run](../review/runs/T-0177-T-0178.md)
- 2026-08-22 `in_review → done` — merged via PR #146 (merge 60e410b; CI green)
