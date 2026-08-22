---
id: T-0180
title: "Reactivation for soft-deleted entities: maker restores own products, admin restores makers"
status: ready
size: M
owner:
created: 2026-08-21
updated: 2026-08-22
depends_on: [T-0174, T-0176]
blocks: []
user_stories: [US-maker-0004, US-admin-0004, US-admin-0013]
adrs: [0013, 0014, 0022]
phase: 8
manual_steps: [nswag-regen]
security_touching: true
layers: [dotnet-backend, frontend, l10n, secops]
---

# T-0180 — Reactivation paths for soft-deleted entities

## Context
Audit findings [MAKER-H4, ADM-H5](../review/ux-functional-audit-2026-08-21.md). Soft delete is the
platform default, but nothing exposes an undo: a maker who deletes a product by mistake loses the
listing and its images permanently (from their point of view), and an admin who deactivates a maker
behind a two-click arm-confirm has no way back without DB access.

**Q-0040 answered 2026-08-22** — *ownership of the deletion decides who may undo it*:

- product deleted **by the maker** → the maker may restore it themselves;
- product deleted **by an admin** → the maker has **no** claim to restore it (a platform removal is
  a moderation decision, not self-service); admin-only;
- **categories:** admin approves; a maker may propose one but it stays invisible publicly until
  approved — **already ticketed as [T-0163](T-0163-maker-proposed-categories.md)**, NOT duplicated
  here.

## Scope
- **Domain:** `Auditable.Reactivate(reactivatedBy, at)` clearing `DeactivatedBy`/`DeactivatedAt`
  (mirrors the existing `MarkDeactivated`); invariant — reactivating an already-active row is a
  no-op Success, never an error.
- **Backend, maker side:** `ReactivateProduct` command on the Maker host, IDOR-shielded by the
  scoped repository. **Ownership-of-deletion gate:** proceeds only when `DeactivatedBy` equals the
  requesting maker's user id; an admin-deleted product returns a new
  `BusinessErrorMessage.ProductRemovedByAdmin` (403-class), never a silent no-op — the maker must
  learn *why* they cannot restore it.
- **Backend, admin side:** `ReactivateMaker` (+ `ReactivateCategory`) as
  `IAdminAuditableCommand` so the before/after JSONB rides the T-0011 audit pipeline, mirroring the
  existing `DeactivateMaker` / `DeactivateCategory`.
- **Frontend, maker:** inactive product cards regain a restore action **only when the maker owns
  the deletion**; an admin-removed product shows the reason instead of a dead button. The
  irreversibility copy T-0174 shipped is re-scoped to the admin-deleted case.
- **Frontend, admin:** restore action on an inactive maker detail + inactive category row (both
  currently render nothing at all once deactivated).
- cs-CZ keys for every new code; NSwag regen for maker + admin hosts.

## Alternatives Considered
- **Let makers restore anything they can see** — *rejected by the Q-0040 answer*: it would let a
  maker undo a moderation decision, which is the one case where the platform's action must stick.
- **A new `DeletedByRole` column** — *rejected*: `Auditable.DeactivatedBy` already stores the acting
  user id (`DeleteProduct.cs:67` passes the session user), so ownership is a comparison, not a
  migration.
- **Hard-confirm + irreversible copy only (no restore)** — that is what T-0174 shipped as the
  interim; this ticket replaces it for the maker-owned case.

## Out of scope
- Maker-proposed categories with admin approval — [T-0163](T-0163-maker-proposed-categories.md).
- Restoring GDPR-**erased** users: erasure is irreversible by design (T-0110) and must stay so.

## Acceptance criteria
- **AC-1** Given a product the **maker** soft-deleted, when they restore it, then it becomes active
  and reappears in the public catalog (integration test asserts both the flag and catalog
  visibility).
- **AC-2** Given a product an **admin** soft-deleted, when the maker attempts to restore it, then
  the command fails with `ProductRemovedByAdmin` and the product stays inactive (test asserts the
  constant, not a string).
- **AC-3** Given an admin-deleted product, when the maker views it, then no restore button renders
  and the reason is shown (the button is not the gate — AC-2 is).
- **AC-4** Given an already-active product, when reactivate is called, then Success with no state
  change and no second audit row.
- **AC-5** Given an admin reactivating a maker or category, then the action writes an
  `admin_audit_log` row with before/after (audited-command pipeline).
- **AC-6** A maker's reactivate call for **another maker's** product returns not-found (scoped
  repository, cross-tenant read returns empty — ADR 0013).
- **AC-7** Every new `BusinessErrorMessage` code has a cs-CZ key and a test that triggers it;
  `npm run check:api` passes after regen.

## Technical notes
`Auditable.cs:54` (`MarkDeactivated`) is the shape to mirror. `DeleteProduct.cs:67` shows the
`DeactivatedBy` stamp this ticket reads back. Admin audited-command precedent: `DeactivateMaker`.
The maker-side gate is a domain/command concern — the UI merely reflects it.

## Files touched (expected)
- `backend/src/Makables.Core.Domain/Common/Auditable.cs`
- `backend/src/Makables.Core.AppServices/Features/Products/ReactivateProduct.cs`
- `backend/src/Makables.Core.AppServices/Features/Maker/ReactivateMaker.cs`, `.../Categories/ReactivateCategory.cs`
- `backend/src/Makables.Web.Maker/**`, `backend/src/Makables.Web.Admin/**` (+ tests)
- `frontend/src/app/(maker)/dashboard/maker/produkty/**`, `frontend/src/app/(admin)/dashboard/admin/{makers,kategorie}/**`
- `frontend/src/lib/i18n/cs-CZ.ts`, `frontend/src/lib/api-client/*` (regen)

## Test plan reference
`docs/test-plans/T-0180.md`

## Status log
- 2026-08-21 filed `draft` (Phase 8 UX sweep plan) — blocked on Q-0040
- 2026-08-22 `draft → ready` — Q-0040 answered by the user (ownership of the deletion decides);
  AC written against that rule, category half delegated to T-0163
