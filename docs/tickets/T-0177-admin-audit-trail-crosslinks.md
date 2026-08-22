---
id: T-0177
title: "Admin audit-trail integrity (server-side targetId filter) + entity cross-links"
status: done
size: M
owner:
created: 2026-08-21
updated: 2026-08-21
depends_on: [T-0175]
blocks: []
user_stories: [US-admin-0009, US-admin-0012]
adrs: [0014, 0022]
phase: 8
manual_steps: [nswag-regen]
security_touching: false
layers: [dotnet-backend, frontend, l10n]
---

# T-0177 — Admin audit-trail integrity + entity cross-links

## Context
Audit findings [ADM-H2, ADM-M4, ADM-M7, ADM-M8](../review/ux-functional-audit-2026-08-21.md).
The order-detail "Audit" section fetches the **global** order audit slice and filters client-side —
on a busy marketplace it can show an empty history for an order whose entries live on later pages.
This is the evidence surface for refund/dispute triage, so an incomplete render is actively
dangerous. Around it, the admin surface has no cross-links: filtering orders by maker means
copy-pasting GUIDs between pages, detail pages return to unfiltered lists, and audit rows
truncate notes with nowhere to expand.

## Scope
- **Backend:** `targetId` filter parameter on the admin audit-log query (alongside the existing
  `targetEntity`); AsNoTracking projection unchanged; index check on the (target_entity, target_id,
  created_at) access path — add if missing. NSwag admin regen.
- **Frontend:** order-detail audit section passes `targetId` (drop the client-side filter and its
  false pagination); audit rows targeting an order link to the order detail; order rows/detail link
  maker name → maker detail; maker detail gains "Zobrazit objednávky" using the existing `makerId`
  orders filter (ADM-M7).
- **List-state preservation:** row links carry the current list query; detail "Zpět na seznam"
  restores it (ADM-M4).
- **Audit notes:** same Tooltip treatment as the id columns (full text reachable); the promised
  `audit/[id]` diff route stays deferred — remove the stale "each row links forward" claim (ADM-M8).

## Alternatives Considered
- **Client-side "fetch all pages then filter"** — rejected: unbounded on a busy log; the server
  filter is one parameter on an existing query.
- **Building the audit/[id] before/after diff route now** — deferred: Tooltip + server filter close
  the audited harm; the diff view is real scope that deserves its own slice if ops asks.

## Out of scope
- Admin actor display names (GUID → name needs an identity read — noted in the backlog, ADM-L8).
- Admin user lookup (T-0178).

## Acceptance criteria
- **AC-1** Given an order with audit entries beyond page 1 of the global order slice, when its
  detail renders, then all its entries appear, paged over its own filtered set (integration test
  on the query + vitest).
- **AC-2** Given the orders list filtered to Disputed page 2, when a row is opened and "Zpět na
  seznam" clicked, then the same filters + page render.
- **AC-3** Given a maker detail, when "Zobrazit objednávky" is used, then the orders list is
  pre-filtered to that maker without any GUID copying.
- **AC-4** Given a truncated audit note, then its full text is reachable via the tooltip.
- **AC-5** `npm run check:api` passes after the admin regen.

## Technical notes
Helper to delete: `getAdminOrderAuditTrail` client-side filter (`admin-orders.ts:345-377` — its own
comment names this fix). Query lives in the T-0111 `IAdminQueries` family; keep the Response shape,
add the parameter. Verify the audit-log index covers `(target_entity, target_id)` before claiming
the perf gate.

## Files touched (expected)
- `backend/src/Makables.Core.AppServices/Features/Admin/**` (audit-log query + validator + tests)
- `backend/src/Makables.Infra.Database/**` (queries impl; index migration if needed)
- `backend/src/Makables.Web.Admin/**` (controller param)
- `frontend/src/lib/api-client-helpers/admin-orders.ts`, `frontend/src/lib/api-client/admin-api.v1.ts` (regen)
- `frontend/src/app/(admin)/dashboard/admin/{orders,makers,audit}/**`

## Test plan reference
`docs/test-plans/T-0177.md`

## Status log
- 2026-08-21 `draft → ready` (Phase 8 UX sweep plan; DoR checklist passed)
- 2026-08-22 `ready → in_progress`, branch `feat/admin-read-integrity-bundle` (bundled with its
  sibling — one admin host, one NSwag regen)
- 2026-08-22 `in_progress → in_review` — backend 2091/2091 (+9), frontend tsc clean + vitest
  215/215; NSwag admin regen; see [test plan](../test-plans/T-0177-T-0178.md) +
  [review run](../review/runs/T-0177-T-0178.md)
- 2026-08-22 `in_review → done` — merged via PR #146 (merge 60e410b; CI green)
