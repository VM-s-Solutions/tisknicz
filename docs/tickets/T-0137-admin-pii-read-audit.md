---
id: T-0137
title: Audit privileged admin reads of customer PII (downloads + order detail)
status: ready
size: M
owner:
created: 2026-06-21
updated: 2026-06-21
depends_on: [T-0011, T-0126, T-0127]
blocks: []
user_stories: [US-admin-0012]
adrs: [0014]
phase: 6
manual_steps: []
security_touching: true
layers: [dotnet-backend, secops]
---

# T-0137 — Audit privileged admin reads of customer PII

> Closes **Q-0028** (admin invoice-PDF reads are not audited). The second half
> of `feat/secops-hardening-bundle` (ships in one PR with T-0136).

## Context

Admin actions are audited **command-side only**: `AdminAuditPipelineBehavior`
fires for any request implementing `IAdminAuditableCommand`, and the audit row
is persisted by the **UnitOfWork** behavior (commands only). Privileged admin
**reads** of customer PII leave **no** audit row:

- `GET /admin-invoices/{id}/pdf` — streams a customer's invoice (recipient name,
  address, tax ids, line items) — controller-direct, not a command (T-0126).
- `GET /payout-batches/{id}/csv` — streams maker payout data — controller-direct.
- `GET /admin-orders/{orderId}` — returns the full contact snapshot
  (`CustomerEmail`, `ContactName`, `ContactPhone`, `CustomerNotes`), **no GDPR
  redaction** (admin is privileged, T-0127 / Q-0024).

ADR 0014 deliberately audits writes, not reads. But these three are the
highest-signal **PII-exfiltration** events ("admin X downloaded customer Y's
invoice"), and a forensic trail of them is a launch-grade expectation for a
2-person trusted-admin role handling other people's financial PII. This ticket
adds read-side auditing for exactly those three, without disturbing the
command-audit pipeline.

## Scope

1. **New `IAdminReadAuditWriter` seam** (Core.Domain/Auditing) +
   `AdminReadAuditWriter` impl (Infra.Database). The impl owns its **own**
   `MakablesDbContext` via the already-registered
   `IDbContextFactory<MakablesDbContext>` (the T-0032 ARES-cache precedent:
   side-effect commits that run OUTSIDE the request-scoped UoW so they can't
   flush a caller's tracked aggregates — and so a pure read never opens the
   request UoW or calls `SaveChangesAsync` in a handler). It builds an
   `AdminAuditLogEntry.Record(...)` (`beforeJson = afterJson = null` — reads have
   no state delta) and commits it in one self-contained `SaveChangesAsync`.
   Signature:
   `Task AuditReadAsync(string actionCode, string targetEntity, string targetId, string? notes, CancellationToken ct)`.
   Collaborators mirror the command behavior: `IUserSessionProvider` (actor),
   `IClock` (timestamp), `IIdGenerator` (ULID), plus the
   `IHttpContextAccessor` for `IpAddress` / `UserAgent`.
2. **Wire the three read sites** to call `AuditReadAsync` **only on the
   successful PII-read path**:
   - `AdminInvoicesController.DownloadInvoice` → audit `invoice.pdf.download`
     **after** the blob fetch succeeds and **before** `File(...)` — NOT on the
     404 (not-yet-rendered) and NOT on the 304 (If-None-Match hit; the admin
     already holds the bytes, no new disclosure).
   - `PayoutBatchesController.DownloadCsv` → audit `payout.csv.download` after
     the blob fetch succeeds, before `File(...)` — not on 404 / 409.
   - `AdminQueriesController.GetOrder` → audit `order.detail.view` only when the
     `GetAdminOrderDetail` result `IsSuccess` (a 404 is not a PII disclosure).
3. **Register** `IAdminReadAuditWriter → AdminReadAuditWriter` (Scoped) in
   `AddMakablesInfrastructure` next to the existing `IAdminAuditLogWriter`.
4. **Tests** — integration tests (Testcontainers) asserting each of the three
   200/success paths writes exactly one `admin_audit_log` row with the right
   `action_code` + `target_*` + `admin_user_id`, and that the 404/304/409 paths
   write **zero** rows. Plus a unit test of `AdminReadAuditWriter` (own-context
   commit, null before/after).

## Alternatives Considered

- **Read-audit MediatR behavior** (`IAdminAuditableQuery` +
  `AdminReadAuditPipelineBehavior`) — symmetric with the command path, but it
  only helps MediatR reads; the two controller-direct downloads (PDF, CSV) are
  byte-stream passthroughs with no MediatR query, so they'd still need a direct
  writer. Rejected: the dedicated writer covers all three sites uniformly with
  less surface than a marker-interface + behavior + a direct writer for the
  downloads.
- **Audit the list endpoints too** (`/admin-orders`, `/admin-invoices`,
  `/payout-batches` list) — rejected for this bundle: high-volume, low-forensic-
  value (a routine dashboard page-load), would bloat `admin_audit_log` on every
  navigation. Scoped to single-record / file-download reads, which are the
  "pulled one person's data" events. Flagged in Out-of-scope as a future
  policy-bucket if a broader "admin accessed PII" requirement lands.
- **Reuse `IAdminAuditLogWriter.AppendAsync` directly from the controller** —
  rejected: `AppendAsync` only `.Add()`s and relies on the UoW behavior to
  commit, which does not run for reads; calling `SaveChangesAsync` on the
  request-scoped context from a controller would entangle the read with the UoW
  and risk flushing unintended state. The own-context writer is the clean seam.

## Out of scope

- The paginated **list** reads (orders/invoices/payout-batch lists) — see
  Alternatives. No audit on routine browse.
- The `GET /audit-log` view — reading the audit log is itself low-risk and would
  create a self-referential write loop.
- No schema change: reuses the existing `admin_audit_log` table (T-0011) — **no
  migration**. `beforeJson`/`afterJson` are nullable; a read uses null for both.
- No new `BusinessErrorMessage` code (auditing is a side effect; the read's own
  error codes are unchanged) → **no** cs-CZ i18n key, **no** T8 impact.
- No NSwag regen (no contract surface change).

## Acceptance criteria

- **Given** an admin downloads an invoice PDF (200), **when** the stream is
  returned, **then** exactly one `admin_audit_log` row exists with
  `action_code = invoice.pdf.download`, `target_entity = invoice`,
  `target_id = {invoiceId}`, `admin_user_id = {the admin sub}`, null
  before/after JSON.
- **Given** the same endpoint returns 404 (not-yet-rendered) or 304
  (If-None-Match), **then** **no** audit row is written.
- **Given** an admin downloads a payout CSV (200), **then** one row with
  `action_code = payout.csv.download`; 404/409 write none.
- **Given** an admin opens an order detail that exists (200), **then** one row
  with `action_code = order.detail.view`; an unknown id (404) writes none.
- **Given** any of the three audits, **then** the audit write is committed on a
  context separate from the read — the read path never calls `SaveChangesAsync`
  in a handler (T3 stays green) and never opens the request UoW.

## Technical notes

- `AdminAuditLogEntry.Record` already accepts null `beforeJson`/`afterJson`
  (constructor permits it; there's a domain test pinning that). Reads pass null.
- `action_code` convention: `<entity>.<verb>` — `invoice.pdf.download`,
  `payout.csv.download`, `order.detail.view`. Lowercase, dot-notation, matching
  the existing `maker.verify` / `user.erase` style.
- The own-context writer must dispose its `MakablesDbContext`
  (`await using`). It does NOT share the request DbContext.
- For `GetOrder`, the controller is currently a one-liner returning
  `HandleResult`. Expand it: dispatch the query, audit on success, then
  `HandleResult` — keep the controller thin (no business logic, just the audit
  side effect on the success branch).
- Actor id via `IUserSessionProvider.GetUserId()` (fail-closed on a missing
  `sub`), falling back to `"system"` exactly as the command behavior does — but
  for an admin-audience read it will always be a real admin sub.

## Files touched (expected)

- `backend/src/Makables.Core.Domain/Auditing/IAdminReadAuditWriter.cs` (new)
- `backend/src/Makables.Infra.Database/Auditing/AdminReadAuditWriter.cs` (new)
- `backend/src/Makables.Config/Extensions/AddMakablesInfrastructure.cs` (DI reg)
- `backend/src/Makables.Web.Admin/Controllers/AdminInvoicesController.cs`
- `backend/src/Makables.Web.Admin/Controllers/PayoutBatchesController.cs`
- `backend/src/Makables.Web.Admin/Controllers/AdminQueriesController.cs`
- `backend/src/Makables.IntegrationTests/...` (3 read-audit assertions)
- `backend/src/Makables.Tests/...` (writer unit test)

## Test plan reference

`docs/test-plans/T-0137.md`

## Status log

- 2026-06-21 `draft → ready` by PM (groomed in `feat/secops-hardening-bundle`;
  Q-0028 answer locked: audit downloads + order detail via a dedicated
  own-context `IAdminReadAuditWriter`, skip the list reads).
