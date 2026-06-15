# T-0126 — Admin read follow-ups — PRELIMINARY review notes (draft)

> **Status:** PRELIMINARY. Written in parallel with the implementer; no T-0126 code is in the
> working tree yet (the `feat/order-cleanup-bundle` branch diff is unrelated bundle work). These
> notes are the ground-truth checklist the FINAL review (at PR-open) must walk row by row. Verdict
> here is **NOT an approval** — it is the gate the implementer must clear.
>
> Reviewer (Opus). Inputs read: T-0126 ticket, T-0088 precedent, quality-gates.md, checklist.md,
> recurring-findings.md (#2 T8 + #3 T9 codified), ADR 0013, and the live source the implementer
> builds against (OutboxEvent, OutboxRetryPolicy, the three repos, the existing admin controllers,
> the T-0112a Fee-invoice download precedent).

## Scope recap
- **Q-0026** admin invoice-PDF download: `GET /api/v1/admin-invoices/{invoiceId}/pdf` (Web.Admin),
  controller-direct, **Unscoped-by-id** (admin sees ANY invoice — the audience is the only gate).
- **Q-0027** two count reads: `GET /api/v1/payout-batches/count?state=Processing` and
  `GET /api/v1/outbox-events/stalled/count`, each `{ count }`.
- Backend-only; one NSwag regen (admin host); no migration, no outbox event, no new code, no new index.

## Ground-truth confirmations (already verified in master)
- `BusinessErrorMessage.InvoiceNotYetRendered = "invoice.notYetRendered"` exists
  (`BusinessErrorMessage.cs:564`) with parallel i18n key `cs-CZ.ts:422`. **Reuse — no new code, no T8.**
- `IInvoiceRepository` has `GetByIdUnscopedAsync` (tracked, `.IgnoreQueryFilters()`) but **NO read-only
  unscoped variant** — the implementer must add `GetByIdUnscopedReadOnlyAsync`
  (`.IgnoreQueryFilters()` + `.AsNoTracking()`). Read-only mirrors that already exist
  (`GetByOrderIdReadOnlyAsync`, `GetForMakerReadOnlyAsync`) are NOT unscoped-by-id; they don't fit.
- `OutboxEvent.ParkPendingConsumer` (OutboxEvent.cs:100) encodes the canonical stall guard:
  `NextRetryAt is null && LastErrorKind != OutboxErrorKind.None`. The ticket's §A.2 stalled predicate
  must match this exactly. `OutboxErrorKind` is the enum at OutboxEventStatus.cs:9 (`None = 0`).
- `OutboxConsumerRepository.LoadDueAsync` due-predicate is
  `ProcessedAt == null && NextRetryAt != null && NextRetryAt <= now` — the stalled set is the
  documented inverse narrowed to the failed-and-not-rescheduled rows.
- `OutboxEventsController` (route `outbox-events`, `[Authorize]`) and `PayoutBatchesController`
  (route `payout-batches`, `[Authorize]`, ctor already has `IPayoutBatchRepository`) both exist —
  the two count actions slot onto them as `[HttpGet("stalled/count")]` / `[HttpGet("count")]`, OR as
  thin MediatR features under `Features/Admin/` (ADR 0023 leaves the choice to the implementer).
- `Features/Admin/` already holds `GetAllInvoices/GetAllOrders/GetAdminAuditLog` — count features fit there.
- T-0112a `FilesController.DownloadFeeInvoice` (maker host) is the closest body-shape precedent for the
  admin invoice action: same `private, no-store` + ETag/304 + `faktura-{InvoiceNumber}.pdf` +
  `EscapeFilenameForHeader`/`ETagMatches` helpers + `File(stream, "application/pdf", enableRangeProcessing: false)`.
- `check-consistency.mjs` exits **0** at baseline ("clean (145 tracked)"). The PR must keep exit 0
  with **zero NEW** T1–T9 (esp. zero new T8/T9).

---

## Pre-flight HIGH items — what the final review MUST verify

### HIGH-1 — IDOR-inversion correctness (the audience IS the shield)
Unlike T-0088 (owner-scoped) this is INTENTIONALLY Unscoped. The ONLY gate is `[Authorize]` + the
admin host audience. **There is no owner predicate** — so there is also no IDOR oracle to hide; a
valid admin JWT may read ANY invoice by id, and that is correct (ADR 0013 §"Unscoped admin-host only").
Final review must confirm:
- [ ] `[Authorize]` present on the action; endpoint lives on a **Web.Admin** controller (host audience).
- [ ] A customer/maker JWT (`aud != admin`) → **401/403** (AC-4). This MUST be pinned by an
      integration cross-host probe (precedent: `JwtAuthMiddlewareTests`,
      `CreatePayoutBatchIntegrationTests`). Without it, AC-4 is unproven.
- [ ] The unscoped read is reachable from **no non-admin host** (grep: no `GetByIdUnscoped*` call in
      Web.Customer/Web.Maker/Web.Public).
- [ ] **PII headers on the 200:** `Cache-Control: private, no-store`; ETag + `If-None-Match` → 304;
      `Content-Disposition: attachment; filename="faktura-{InvoiceNumber}.pdf"` through
      `EscapeFilenameForHeader`; `Content-Type: application/pdf`; `enableRangeProcessing: false`.
- [ ] **404 is identical** for (a) no invoice row, (b) `PdfBlobPath` null, (c) blob-download failure
      (purged-blob race) → all `404 invoice.notYetRendered`, **no `Cache-Control` on the 404** (AC-2).
      No `OrderNotFound` path here (admin reads by invoice id directly — no order in the chain).

### HIGH-2 — the stalled-outbox predicate (the subtle one)
The count MUST match T-0109's stalled set exactly. Required predicate (CountStalledAsync):
```
ProcessedAt == null && NextRetryAt == null && LastErrorKind != OutboxErrorKind.None
```
- [ ] Uses `OutboxErrorKind.None` (the enum member), not a magic int/string.
- [ ] Does **not** count acknowledged rows — `Acknowledge` sets `ProcessedAt` (OutboxEvent.cs:145), so
      `ProcessedAt == null` already excludes them; `AcknowledgedAt == null` must NOT be added as a
      separate clause (would be redundant and is explicitly rejected in §A.2).
- [ ] Does **not** count due/retrying rows (`NextRetryAt != null`) or freshly-enqueued rows
      (`NextRetryAt = CreatedAt`, non-null — excluded by the `NextRetryAt == null` clause).
- [ ] The `LastErrorKind != None` clause is present (the load-bearing inverse of the due-set; without
      it a hypothetical `NextRetryAt==null` non-failed row would over-count). **Rejected predicates**
      to watch for: `RetryCount >= MaxTransientAttempts` (misses Permanent/Config/Unknown immediate
      stalls), or `next_retry_at IS NULL` alone (counts freshly-processed rows).
- [ ] **The predicate is load-bearing → it needs a dedicated unit test** asserting: stalled counted;
      processed / acknowledged / due / fresh-enqueued all EXCLUDED. Per the test plan stub (~the
      "load-bearing predicate assertion"). If this test is missing, request changes.

### HIGH-3 — count-read soundness
- [ ] `CountByStateAsync` and `CountStalledAsync` are **true COUNTs**
      (`.CountAsync(predicate, ct)` / `.Where(...).CountAsync(ct)`), **not** materialize-then-count
      (no `.ToListAsync()...Count`). Over-fetch is the §A.3 rejection.
- [ ] **AsNoTracking** on both (count never tracks). Note: `OutboxConsumerRepository` is documented
      "intentionally TRACKED" for its mutating callers — the new `CountStalledAsync` is a pure read and
      must NOT inherit that; a `.CountAsync` projection doesn't track, so this is fine, but the method's
      doc-comment must not imply tracking.
- [ ] `CountByStateAsync` is admin-only (`IPayoutBatchRepository` is admin-host only per ADR 0013) and
      counts the right WHERE (`State == state`). AC-5: Completed rows excluded; empty → `{ count: 0 }`,
      never 404. Watch the soft-delete filter: an active Processing batch is never soft-deleted, so the
      global filter is harmless — but confirm the count is not inadvertently `.IgnoreQueryFilters()`'d
      (which would over-count anonymised rows). The existing `GetOpenBatchAsync` relies on the global
      filter (no IgnoreQueryFilters); `CountByStateAsync` should match that policy.
- [ ] `CancellationToken` propagated to both count calls (checklist I).

### HIGH-4 — T8/T9 (codified gates) + check-consistency exit 0
- [ ] **Zero new `BusinessErrorMessage` codes** (404 reuses `InvoiceNotYetRendered`; counts are pure
      reads). If any new code sneaks in without a `cs-CZ` key → **T8 HARD FAIL** (recurring-finding #2,
      codified `ruleT8`). Quote the checklist J row, don't paraphrase.
- [ ] **Zero new NAMED unique indexes** (no migration in scope) → no T9 surface. If a migration appears
      at all, it is out of scope — flag it.
- [ ] `node scripts/check-consistency.mjs` exits **0** with no NEW T1–T7 either (count features must
      follow `<Entity>/<UseCase>.cs` shape with a `public static class` wrapper if MediatR — T1; no
      inline `Error.*` strings — T5; globally-unique Response names per PR #38/NSwag convention).

### HIGH-5 — NSwag regen (admin host)
- [ ] One regen commit, **admin host only**; `frontend/src/lib/api-client/admin-api.v1.ts` types all
      three endpoints (the invoice download as a `FileResponse`-returning method; the two counts with
      their typed `{ count }` responses).
- [ ] Response records have **globally-unique names** (`GetProcessingPayoutsCountResponse`,
      `GetStalledOutboxCountResponse`) — no bare `Response`, no schema collision (NSwag convention).
- [ ] No manual edits to `lib/api-client/*` (pre-commit hook). `.spec-hashes.json` updated by the regen.
- [ ] Contract parity (Gate 6): generated client matches `openapi/v1.json`.

---

## Checklist walk (final-review template)
- **A (CLAUDE self-check):** no `dynamic`, no `Console.WriteLine`, no dead code, no inline error
  strings (all 404s from `BusinessErrorMessage`).
- **B (architecture):** controller one-liners for the count actions IF MediatR; the invoice action is
  controller-direct (ADR 0014 handler-free read — matches T-0088/T-0112a). `Core.Domain` stays
  package-free; `Core.AppServices` no EF.
- **C (domain/extension points):** Unscoped reachable from admin host only (ADR 0013). No
  country/provider branch. No money math here (counts are ints).
- **D (security):** `[Authorize]` on all three; PII PDF `private, no-store`; no secrets. **Gate 3
  SecOps is MANDATORY** (security_touching: admin file streaming of PII invoices + Unscoped reads).
  **Ping SecOps at PR-open** — note Q-0011 rate-limit is TOUCHED-not-closed (admin-JWT reads, 2 trusted
  users) and wants a one-line re-confirm.
- **F (AC traceability):** AC-1..AC-8 each need a proof. AC-4/AC-7 (cross-host 401) MUST be an
  integration probe, not just a unit test.
- **G/Gate 5 (tests):** the stalled-predicate test is pure logic → **TDD discipline applies**
  (T-0067+; this ticket is well past the grandfather line). Per Gate 5, an after-the-fact predicate
  test is a HARD FAIL — the commit order must show the predicate test red→green BEFORE or alongside
  the repo impl. Verify `git log` on the branch: the count-predicate + invoice-controller unit tests
  should not all land in a single post-implementation `test(...)` commit dated after the impl commit.
  (The commits-hint lists test as commit 3, after the two feat commits — **scrutinize this**: if the
  predicate test was authored after the impl with no red→green evidence, request a rewrite under TDD.)
- **I (performance / Optimizer):** counts are O(1) indexed aggregates, not a hot path that needs the
  Optimizer (no paging, no N+1, no loop). The stalled count hits `ix_outbox_event_due`
  `(next_retry_at, processed_at) WHERE processed_at IS NULL` — confirm the predicate is index-friendly
  (it filters `processed_at IS NULL AND next_retry_at IS NULL`, covered by the partial index).
  **Optimizer ping not required** (read-only single-aggregate counts; below the >5-entity / pipeline bar).
- **J (mechanical):** T8 + T9 clean; check-consistency exit 0.

## Harvest duty (note for PR-open)
No new recurring finding expected. #2 (T8) and #3 (T9) are codified — CI owns them; if either fires it
is a "violates ruleT8/ruleT9" comment, not a new log row. Only append/increment if a *new* finding type
repeats a 3rd time.

## Preliminary verdict
**NOT YET APPROVABLE — no code to review (parallel run).** The design is sound and every locked
decision has a clean precedent in master. At PR-open, walk HIGH-1..HIGH-5 + the checklist above. The
two things most likely to fail and block approval:
1. **Cross-host 401 integration proof** for the Unscoped invoice read (AC-4) — easy to omit.
2. **TDD order on the stalled-predicate test** (Gate 5) — the commits-hint puts tests after impl; demand
   red→green evidence or reject.
Also: SecOps Gate 3 sign-off is mandatory before merge (admin PII file streaming + Unscoped).
