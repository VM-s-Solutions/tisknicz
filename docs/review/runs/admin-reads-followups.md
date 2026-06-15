# T-0126 — Admin read follow-ups — FINAL review

**Branch:** `feat/admin-reads-followups` (4 commits: 69b932b grooming, 2ece9a9 invoice, 01c0020 counts, 3b87c2e NSwag).
**Verdict: APPROVE.** All eight mandatory checks pass; AC-1…AC-8 traced; Gates 1–7 green. One NON-BLOCKING docs gap (role-file read-surface notes the ticket listed but did not ship). No BLOCKERs.

Reviewer read FIRST: the preliminary draft (`admin-reads-followups-draft.md`), the T-0126 ticket + ADR 0013/0022/0023/0025, then the scoped diff (`git diff 69b932b~1...HEAD` — 22 files, 1465+/6-).

---

## (b) IDOR-inversion + stalled-predicate dispositions

### IDOR-inversion (the headline) — PASS
`AdminInvoicesController.DownloadInvoice` (`AdminInvoicesController.cs:55-104`) resolves via `IInvoiceRepository.GetByIdUnscopedReadOnlyAsync` (`.AsNoTracking().IgnoreQueryFilters()`, `InvoiceRepository.cs:123-137`) — Unscoped by id, by design (admin sees ANY invoice; ADR 0013 §"Unscoped escape hatch is admin-host only"). The IgnoreQueryFilters posture (admin/GDPR-reconciliation visibility of soft-deleted/anonymised rows) is intended and commented (`InvoiceRepository.cs:128-132`). The ONLY shield is `[Authorize]` + admin host audience (`AdminInvoicesController.cs:37`).
- **Cross-host 401 (AC-4) genuinely exercises non-admin tokens** — `AdminInvoiceDownloadIntegrationTests.cs:209-221` issues a REAL signed customer JWT (`aud=Customer`) and a REAL maker JWT (`aud=Maker`) and asserts `401` against the admin host. Not an unauth probe — a true audience-mismatch replay. AC-4 proven.
- **No non-admin reach:** `GetByIdUnscopedReadOnlyAsync` (invoice) referenced only in Web.Admin + its own repo/iface/tests. (The Order-side `GetByIdUnscopedReadOnlyAsync` matches are a different method on a different interface.)
- **404 parity, no Cache-Control leak (AC-2):** all three 404 paths return `Error.NotFound("invoice", InvoiceNotYetRendered)` — no-row + null `PdfBlobPath` (`:63-66`) and blob-miss (`:72-78`). `DownloadInvoice_BlobMiss_..._NoCacheControl` (`AdminInvoiceDownloadTests.cs:116-131`) asserts `CacheControl ... BeEmpty()`. No new error code (reuses T-0069 `InvoiceNotYetRendered`).
- **200 headers (AC-1/AC-3):** `private, no-store` + ETag/304 + `Content-Disposition: attachment; filename="faktura-{InvoiceNumber}.pdf"` via `EscapeFilenameForHeader`; `application/pdf`; `enableRangeProcessing: false` — pinned at `AdminInvoiceDownloadTests.cs:56-81` (happy) + `:133-149` (304).

### Stalled predicate (load-bearing) — PASS
`OutboxConsumerRepository.CountStalledAsync` (`OutboxConsumerRepository.cs:44-60`) =
`ProcessedAt == null && NextRetryAt == null && LastErrorKind != OutboxErrorKind.None`.
- **Matches `OutboxEvent.ParkPendingConsumer`'s stall guard exactly** (`OutboxEvent.cs:100`: `NextRetryAt is null && LastErrorKind != OutboxErrorKind.None`), plus `ProcessedAt == null`. Identical to T-0109's stalled set.
- **Acknowledged excluded via `ProcessedAt == null`** — `Acknowledge` sets `ProcessedAt = now` (`OutboxEvent.cs:145`); no separate `AcknowledgedAt` clause (correctly rejected per §A.2).
- Uses the enum member `OutboxErrorKind.None` (not a magic int), `.AsNoTracking()` (pure read, does NOT inherit the repo's documented TRACKED posture).
- **Integration matrix proves it:** `AdminOverviewCountsIntegrationTests.cs:160-221` seeds 2 stalled + due + fresh + processed + acknowledged → asserts exactly `2`.

## (c) TDD-order RULING — test-alongside ACCEPTED

The draft flagged that tests landed in the SAME commits as impl (2ece9a9, 01c0020), not as a red-first commit. **Ruling: test-alongside is acceptable here; the T-0067+ red-first hard rule does NOT apply.**

Per [docs/process/quality-gates.md](../../docs/process/quality-gates.md) Gate 5, the red-first hard-fail targets **pure-logic domain surfaces** (validators, domain services, specifications, domain predicate methods — `Order.X()`-style). T-0126 ships **no** such surface: the stalled predicate is a **repository WHERE clause** (`CountStalledAsync`), the counts are **repository read methods** (`CountByStateAsync`), and the invoice download is a **controller stream**. These fall under the repository/handler carve-out (test-alongside acceptable). **Precedent:** prior read-bundle predicates shipped the same way — T-0080 / T-0111 did not red-first their query WHERE clauses. The subtlety of the stalled predicate is fully discharged by the canonical-guard cross-check (`ParkPendingConsumer`, `OutboxEvent.cs:100`) + the full exclusion-matrix integration test — not by commit ordering. No rewrite required.

## (d) BLOCKERs
None.

## (e) Fold list (NON-BLOCKING — fold opportunistically, not gating approval)
1. **Role-doc read-surface notes** — the ticket's Cross-cutting checklist lists `roles/invoice.md` (admin Unscoped download), `roles/payout-batch.md` + `roles/outbox.md` (the count reads); none were updated in scope (grep: zero T-0126 mentions). NOT an RDD-parity hard-fail (no new aggregate/VO/domain-service/repo-interface — only new read METHODS on existing interfaces that already have role files; no responsibility changed). Minor unmet checklist item; fold a one-line note into each on the next admin-docs touch.

## (f) Checks (1–8)
1. **IDOR-inversion** — PASS (above). Unscoped by design; admin audience is the sole shield; cross-host 401 exercises real non-admin tokens; 404 parity + no Cache-Control leak; 200 headers correct.
2. **Stalled predicate** — PASS (above). Matches `OutboxEvent.cs:100` exactly; acknowledged excluded by `ProcessedAt == null`.
3. **TDD-order** — RULED test-alongside acceptable (repository/handler carve-out; T-0080/T-0111 precedent). No red-first required.
4. **Count soundness** — PASS. Both are true `.CountAsync(predicate, ct)` (SQL COUNT, no materialize-then-count), `.AsNoTracking()`, admin-only. `CountByStateAsync` keeps the global soft-delete filter (no IgnoreQueryFilters — counts active batches; `PayoutBatchRepository.cs:73-81`). `CancellationToken` propagated on both.
5. **Endpoint placement** — PASS. Folded onto existing `PayoutBatchesController` (`count`, `:54-61`) + `OutboxEventsController` (`stalled/count`, `:35-39`) — clean resource-route fit (count is a sub-resource of the collection). One-liner Mediator dispatch. NSwag emitted `count()` / `count2(state)` (operationId disambiguation) with globally-unique Response types `GetStalledOutboxCountResponse` / `GetProcessingPayoutsCountResponse` — no bare `Response`, no schema collision.
6. **Repo additions** — PASS. `GetByIdUnscopedReadOnlyAsync` (AsNoTracking + IgnoreQueryFilters, intended + commented); `CountByStateAsync`; `CountStalledAsync` — all on the correct interfaces. The stalled count uses `IOutboxConsumerRepository` (the registered consumer-side repo), not `IOutbox`.
7. **NSwag regen** — PASS. Admin host only (commit 3b87c2e: `admin-api.v1.ts` + `.spec-hashes.json`), all 3 methods present (`admin-api.v1.ts:80,101,215`), unique responses, no manual edits to other hosts.
8. **Build + tests + consistency** — PASS. `dotnet build Makables.Tests` (transitively Web.Admin + Core + Infra) → **0 warn / 0 err**. T-0126 unit tests → **9 passed / 0 failed** (5 invoice + 4 count). `check-consistency.mjs` → **exit 0, clean (147 tracked)** — the +2 new-feature T1 entries are audited false-positives; **zero** new BusinessErrorMessage codes, **zero** new indexes, **zero** migrations, **zero** i18n changes within the scoped diff (T8/T9 clean). Integration/full-suite counts (1719+9 unit, +5 integration) pre-confirmed by lead.

## Gates 1–7 summary
- **Gate 1 (CLAUDE self-check):** no `dynamic`, no `Console.*`, no inline error strings, no dead code. PASS.
- **Gate 2 (architecture):** `Core.Domain` package-free; `Core.AppServices` no EF; controller-direct stream per ADR 0014 handler-free read; count features are one-file `<UseCase>.cs` shape. PASS.
- **Gate 3 (security/SecOps):** admin-audience enforced on all 3; PII PDF `private, no-store`; no secrets. `security_touching: true` — SecOps Gate 3 sign-off + Q-0011 rate-limit re-confirm (admin-JWT reads, 2 trusted users) should be recorded at PR-open per the draft; not blocking code approval.
- **Gate 4 (extension points):** no country/provider branch; no money math (counts are ints). PASS.
- **Gate 5 (tests/TDD):** ruled above — test-alongside acceptable; coverage complete. PASS.
- **Gate 6 (contract parity):** NSwag admin regen committed same PR. PASS.
- **Gate 7 (optimizer):** read-only single-aggregate counts; below the >5-entity / pipeline hot-path bar — no optimizer ping required. PASS.
