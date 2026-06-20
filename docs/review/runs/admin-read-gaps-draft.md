# T-0127 — Admin read gaps — PRELIMINARY review notes (draft)

> **Status:** PRELIMINARY. Written in PARALLEL with the implementer; no T-0127 code is in the working
> tree yet (the `feat/order-cleanup-bundle` branch diff is unrelated bundle work). These notes are the
> ground-truth checklist the FINAL review (at PR-open) must walk row by row. The verdict here is **NOT an
> approval** — it is the gate the implementer must clear.
>
> Reviewer (Opus). Inputs read: T-0127 ticket, T-0111 (`IAdminQueries` precedent) + T-0126
> (sibling admin-read-followup + the shared stalled predicate), quality-gates.md, checklist.md,
> recurring-findings.md (#2 T8 + #3 T9 codified), ADR 0013 (data-scoping / Unscoped admin reads), and the
> live source the implementer builds against: `UpdateCountryConfiguration.cs` (the field set the GET must
> mirror), `OutboxConsumerRepository.CountStalledAsync` (the canonical stalled predicate), `IAdminQueries`,
> `IOrderRepository.GetByIdUnscopedReadOnlyAsync`, `IPayoutBatchRepository`, `ICountryConfigurationRepository`,
> `OrderState`, and the existing Web.Admin controllers.

## Scope recap
Cross-stack M bundle — **4 thin admin-host reads + 1 NSwag regen + 3 FE re-wires**, closes Q-0029 (all four reads) + Q-0024 (admin order-detail).
1. `GET /api/v1/country-configurations/{countryCode}` → `GetCountryConfigurationResponse` (PRIORITY — removes the PR-2 full-replace fence).
2. `GET /api/v1/admin-orders/{orderId}` → `GetAdminOrderDetailResponse` / `AdminOrderDetailDto` (full privileged header, Unscoped, no GDPR redaction).
3. `customerUserId` + `makerId` filters on a thin paged `GET /api/v1/admin-orders` read (the delete-user in-flight signal).
4. `GET /api/v1/outbox-events/stalled` (paged) → `GetStalledOutboxEventsResponse` (the LIST, reusing the EXACT T-0126 stalled predicate).
5. `GET /api/v1/payout-batches` (paged) → `GetPayoutBatchesResponse` (Unscoped cross-maker).
FE: country-config pre-fill + diff-modal + banner downgrade; order-detail header from the real DTO; delete-user proactive pre-disable; outbox/payout browsable paged lists.
No migration, no domain mutation, no outbox event. **Expected zero new `BusinessErrorMessage` codes, zero new unique indexes.**

## Ground-truth confirmations (already verified in master)
- **Both reused error codes + their cs-CZ keys EXIST — no T8 surface:**
  - `BusinessErrorMessage.CountryConfigurationNotFound = "countryConfiguration.notFound"` is used at
    `UpdateCountryConfiguration.cs:185` (+ PricingService/IssueInvoice); cs-CZ key at `cs-CZ.ts:540`. The GET 404 reuses it.
  - `BusinessErrorMessage.OrderNotFound = "order.notFound"` is used across the customer/maker hosts; cs-CZ key at `cs-CZ.ts:454`. The order-detail 404 reuses it.
- **The country-config GET field set the response must mirror** — `UpdateCountryConfigurationResponse`
  (`UpdateCountryConfiguration.cs:60-72`) carries exactly: `StandardVatRateBp, ReducedVatRateBp, InvoicingMode,
  PlatformFeeRateBp, DefaultShippingPriceMinor, DefaultPaymentProvider, DefaultShippingCarrier, DefaultRegistry,
  DefaultEmailProvider` (plus the two write-only echoes `InFlightOrderCount` + `ProviderChanged` which the GET MUST NOT carry —
  they are mutation-result fields, not config state). The GET must echo the **nine config fields**, byte-round-trippable with the PUT.
- **The read-side config loader is correct + AsNoTracking** — `ICountryConfigurationRepository.GetByCodeAsync`
  (`ICountryConfigurationRepository.cs:18`) is the documented `AsNoTracking` read path. The GET feature reads through it
  (NOT `GetByCodeForUpdateAsync`, which is the tracked write path). Null → 404 `CountryConfigurationNotFound`. AC-1's "AsNoTracking confirmed" is satisfied by this method.
- **The canonical stalled predicate is in `OutboxConsumerRepository.CountStalledAsync`**
  (`OutboxConsumerRepository.cs:44-60`):
  ```
  ProcessedAt == null && NextRetryAt == null && LastErrorKind != OutboxErrorKind.None
  ```
  The new `GetStalledPagedAsync` LIST predicate MUST be byte-identical to this. (See HIGH-2 — this is the silent-correctness trap.)
- **`IOrderRepository.GetByIdUnscopedReadOnlyAsync`** (`IOrderRepository.cs:158`) already exists — `.IgnoreQueryFilters()` +
  `.AsNoTracking()`, Unscoped, admin-only. The order-detail projection should compose over this (read-only; the handler only projects, never mutates). The tracked `GetByIdUnscopedAsync` (`:140`) is the write-path variant — do NOT use it for the projection.
- **`OrderState`** members (`OrderState.cs`): `PendingPayment=0, Paid=1, Accepted=2, Shipped=3, Delivered=4, Completed=5,
  Cancelled=6, Refunded=7, Disputed=8`. The in-flight set per §A.3 / AC-10 is **`{ PendingPayment, Paid, Accepted, Shipped, Disputed }`** — confirm the FE/filter uses exactly these five (NOT Delivered/Completed/Cancelled/Refunded — those are terminal-or-settled).
- **All target controllers exist** on Web.Admin: `AdminQueriesController` (the `admin-orders`/`admin-invoices`/`audit-log` reads — order-detail + the `customerUserId` filter slot here), `CountryConfigurationsController` (the GET slots beside the existing PUT), `PayoutBatchesController` (the LIST GET slots beside the `CreatePayoutBatch` POST + the T-0126 count), `OutboxEventsController` (the stalled LIST slots beside the T-0126 stalled count). All are `[Authorize]` admin-audience.
- **`IPayoutBatchRepository`** (`IPayoutBatchRepository.cs`) is admin-host-only per ADR 0013, already has `CountByStateAsync` (T-0126); the new payout LIST adds a paged read method here (Unscoped — admin sees cross-maker; the global soft-delete filter applies, NOT `IgnoreQueryFilters` unless an AC needs deactivated batches — none does).
- **`IAdminQueries`** (`IAdminQueries.cs`) is the read-side seam (ADR 0023) — order-detail, the `customerUserId`/`makerId` filter, and the payout LIST extend it OR sit as sibling one-file features under `Features/Admin/` (implementer judges by T-0111 precedent; either is acceptable). The interface doc already states AsNoTracking + IgnoreAutoIncludes + two round-trips for the listy reads.

---

## Pre-flight HIGH items — what the final review MUST verify

### HIGH-1 — Country-config form re-wire (the headline; removes a BLOCKER fence) — AC-1/AC-2/AC-8
This is the load-bearing item. Final review must confirm, end to end:
- [ ] **The GET returns the full editable set (the NINE config fields), byte-round-trippable with the PUT.**
      The response carries exactly `StandardVatRateBp, ReducedVatRateBp, InvoicingMode, PlatformFeeRateBp,
      DefaultShippingPriceMinor, DefaultPaymentProvider, DefaultShippingCarrier, DefaultRegistry, DefaultEmailProvider`.
      It must NOT carry `InFlightOrderCount` / `ProviderChanged` (those are mutation-result fields, not config state). A
      drift between the GET shape and the PUT command shape re-introduces the silent-overwrite hazard (§A.1 rejection). If the
      GET returns a partial/extended set → request changes.
- [ ] **Reads through `GetByCodeAsync` (AsNoTracking), not `GetByCodeForUpdateAsync`.** Null → `404 countryConfiguration.notFound` (reused — **no new code**, AC-2).
- [ ] **The form pre-fills from the GET (SSR).** Server Component fetches on render; no `useEffect` data fetch (checklist B / Gate-1 FE).
- [ ] **The provider retype modal now gates on an ACTUAL diff (loaded vs entered), not "any code present."** The FE must
      compare each `Default*Provider` form value against the loaded config and fire the modal **only when a provider value
      differs**. A VAT-only save must NOT trigger the modal (AC-8: "the provider retype modal fires only when a
      `Default*Provider` value differs from the loaded config"). This mirrors the backend `RequiresConfirmation` /
      `ConfirmationMatches` semantics (`UpdateCountryConfiguration.cs:77-108`) — the FE diff is the proactive UX layer; the
      backend retype gate (AC-3) stays authoritative. **T-0118c AC-4/AC-5 are now MET** — verify the modal logic actually
      diffs, not just "a provider field is non-empty."
- [ ] **The warning banner downgrades to an info note.** The PUT is still full-replace, but the pre-fill removes the
      re-enter-from-memory hazard, so the prominent BLOCKER warning becomes a brief info note ("save replaces the full
      config"). Confirm it is downgraded, not removed entirely (the full-replace fact is still worth an info line).
- [ ] **A 404 from the GET → graceful blank-form fallback** (the original blank-form + warning-banner behaviour). The page
      must not error/500 on an unseeded country — it falls back to the create-from-blank path with the original warning. Verify the SSR fetch handles the 404 as a `Result` branch, not a thrown error.

### HIGH-2 — Stalled-predicate consistency (the silent-correctness trap)
The stalled-outbox LIST predicate (`GetStalledPagedAsync`) MUST be **byte-identical** to `CountStalledAsync`:
```
ProcessedAt == null && NextRetryAt == null && LastErrorKind != OutboxErrorKind.None
```
- [ ] **DEMAND a shared predicate OR both pinned against the same canonical definition.** If the implementer copies the
      predicate into a second method by hand, the count tile (T-0126) and the LIST can DRIFT — the operator sees N in the
      stalled count but a different set in the list, and neither is trustworthy. **Strongly prefer** extracting a shared
      predicate (a static `Expression<Func<OutboxEvent,bool>>` or a private helper on the repo that both `CountStalledAsync`
      and `GetStalledPagedAsync` consume). If the implementer instead duplicates the WHERE, **require** a unit test pinning
      BOTH methods against the same canonical definition over the same seeded set (assert `count == list.TotalCount` and the
      same row ids), so a future edit to one without the other fails.
- [ ] Uses `OutboxErrorKind.None` (the enum member), not a magic int/string.
- [ ] Does NOT add `AcknowledgedAt == null` as a separate clause (redundant — `ProcessedAt == null` already excludes
      acknowledged rows; explicitly rejected in T-0126 §A.2). Does NOT use the looser `NextRetryAt IS NULL` alone (counts
      freshly-processed rows). Does NOT use `RetryCount >= Max` (misses Permanent/Config/Unknown immediate stalls).
- [ ] **This predicate is pure logic → the LIST predicate test is a Gate-5 TDD obligation** (see Gate-5 row below). The
      load-bearing assertion: stalled IN; processed / acknowledged / due / fresh-enqueued OUT; and the LIST returns the SAME
      rows `CountStalledAsync` counts (AC-5).
- [ ] **Paging:** `PagedData<T>`, page clamp `[1,50]` default 20, `CreatedAt DESC` + `Id` tie-break, two round-trips (the T-0111/T-0080 precedent). `AsNoTracking` — note `OutboxConsumerRepository` is documented "intentionally TRACKED" for its mutating callers; the new paged read must NOT inherit that — its projection/`.AsNoTracking()` is a pure read (mirror `CountStalledAsync`, which already opts to `.AsNoTracking()`).

### HIGH-3 — Admin Unscoped / IDOR posture (the audience IS the shield) — AC-3/AC-6/AC-7, ADR 0013
All four reads are INTENTIONALLY Unscoped — admin sees everything; the ONLY gate is `[Authorize]` + the admin host audience.
There is no owner predicate, so there is **no IDOR oracle to hide** (a valid admin JWT may read any order/payout/config/outbox row — that is correct, ADR 0013 §"Unscoped escape hatch is admin-host only"). Final review must verify the **audience gate**, NOT an owner predicate:
- [ ] `[Authorize]` (admin scheme) on all four endpoints; each lives on a **Web.Admin** controller (host audience boundary).
- [ ] A customer/maker JWT (`aud != admin`) → **401/403** on each (AC-7). This MUST be pinned by an integration cross-host
      probe (precedent: `AdminQueriesIntegrationTests` cross-host 401, `JwtAuthMiddlewareTests`). The ticket's integration
      plan items (2) + (6) cover the order-detail + payout/outbox cross-host 401 — confirm they exist and are not stubbed.
- [ ] The Unscoped reads are reachable from **no non-admin host** (grep: no `Unscoped()` / `GetByIdUnscoped*` call in
      Web.Customer/Web.Maker/Web.Public; the documented Comgate-webhook `GetByProviderRef` exception aside).
- [ ] **Order-detail carries `customerEmail` + full contact snapshot — intentional, NO GDPR redaction** (AC-3, operator
      surface; mirrors the T-0111 list-DTO divergence from the maker surface). Confirm `customerEmail` is non-empty in the
      projection and the DTO is NOT reusing the owner-scoped T-0082 customer/maker detail DTO (those load via
      `GetByIdForCustomerAsync`/`ForMakerAsync` — not reachable on the admin host; §A.2 rejection). Unknown/inactive id → `404 OrderNotFound`.
- [ ] **The `customerUserId`/`makerId` admin-orders filter** (AC-4) reuses the same `Unscoped()` projection — an empty
      result IS the "no in-flight" signal (`200`, `TotalCount = 0`, never 404). The filter must AND with the in-flight state
      set when the FE drives the delete-user pre-disable.
- [ ] **Payout LIST is Unscoped cross-maker** (AC-6), `CreatedAt DESC`, the projection shape
      `{ batchId, batchNumber, state, totalAmountMinor, orderCount, makerCount, createdAt, completedAt }` — `totalAmountMinor`
      is money-as-minor-units (checklist C); confirm no `decimal`.

### HIGH-4 — Delete-user proactive pre-disable (UI surfaces, doesn't replace) — AC-10
- [ ] The per-user in-flight signal disables the destructive delete button **PRE-call** when the target user has any order
      in `{ PendingPayment, Paid, Accepted, Shipped, Disputed }`, with the `user.cannotDeleteWithInFlightOrders` reason inline.
- [ ] **The backend gate stays authoritative** — this is a UX pre-disable, NOT a replacement for the T-0110 server-side
      re-check. Confirm the FE SURFACES the signal (disables + explains) but the actual delete still hits the backend, which
      re-checks. If the implementer removes/weakens the backend interlock in favour of the pre-disable → request changes (a
      client-only gate is bypassable).
- [ ] The in-flight states used by the FE match the §A.3 / AC-10 set exactly (the five above) — not Delivered/Completed/terminal.

### HIGH-5 — T8/T9 (codified gates) + check-consistency exit 0 — AC-12
- [ ] **Zero new `BusinessErrorMessage` codes** (GET 404 reuses `CountryConfigurationNotFound`; detail 404 reuses
      `OrderNotFound`; all four reads are pure GETs — empty LIST → `PagedData` `TotalCount = 0`, never 404). If any new code
      sneaks in without a parallel `cs-CZ` key → **T8 HARD FAIL**. Quote checklist J verbatim:
      > **T8 (i18n parity, `hard`):** every new `BusinessErrorMessage` code has a parallel `cs-CZ.ts` key OR is in the `T8_NO_KEY_REQUIRED` allowlist … Hard-fail — never baselined.
- [ ] **Zero new NAMED unique indexes** (no migration in scope) → no T9 surface. If ANY migration appears, it is
      out of scope — flag it. Quote checklist J verbatim:
      > **T9 (unique-index→translator, `hard`):** every new NAMED unique index (`.IsUnique().HasDatabaseName("x")`) is a `UniqueConstraintTranslator` key OR carries a `// no-translator: <reason>` marker … Hard-fail — never baselined.
- [ ] `node scripts/check-consistency.mjs` exits **0** with no NEW T1–T7 vs the 147-tracked baseline: query features
      follow `<Entity>/<UseCase>.cs` shape with a `public static class` wrapper if MediatR (T1); no inline `Error.*`
      strings (T5); globally-unique Response names (PR #38 / NSwag) — `GetCountryConfigurationResponse`,
      `GetAdminOrderDetailResponse`, `GetStalledOutboxEventsResponse`, `GetPayoutBatchesResponse`.

### HIGH-6 — NSwag regen (admin host) + contract parity (Gate 6) — AC-12
- [ ] One regen commit, **admin host only**; `frontend/src/lib/api-client/admin-api.v1.ts` types all four new methods
      (the GET, the order-detail, the two paged LISTs) + their globally-unique Response types + `AdminOrderDetailDto` + the
      two list-item DTOs. `.spec-hashes.json` updated by the regen.
- [ ] **No manual edits to `lib/api-client/*`** (pre-commit hook). FE re-wires are read-only consumers of the regenerated client.
- [ ] **`npm run check:api` re-enabled and green** (the ticket calls for re-enabling the parity check — confirm it is
      un-skipped in CI/scripts, not just locally green). Contract parity: generated client matches `openapi/v1.json`.

---

## Checklist walk (final-review template)
- **A (CLAUDE self-check):** no `dynamic` / `any`; no `Console.WriteLine` / `console.*`; no dead/commented code; no inline
  error strings (both 404s from `BusinessErrorMessage`); FE props typed, no unsafe `!`.
- **B (architecture):** Server Components default (the country-config + order-detail pages fetch SSR — no `useEffect` data
  fetch); controller one-liners (`HandleResult(await Mediator.Send(...))`) for the query features; `Core.Domain` stays
  package-free; `Core.AppServices` no EF (the EF projections live in `Infra.Database/Admin/AdminQueries.cs` or the
  repo impls). All FE data via `lib/api-client/` + `apiFetch`.
- **C (domain / extension points):** Unscoped reachable from admin host only (ADR 0013 — HIGH-3). No country/provider
  branch in app code (the provider modal diffs values, doesn't `if (code == "CZ")`). `totalAmountMinor` is money-as-minor-units, no `decimal`.
- **D (security):** `[Authorize]` admin on all four; no secrets; `customerEmail` exposure is the intentional privileged
  admin surface (AC-3). **Gate 3 SecOps is MANDATORY** (`security_touching: true` — admin Unscoped reads of cross-tenant
  order/payout data + the delete-user pre-disable consumes a per-user in-flight signal). **Ping SecOps at PR-open.** Note
  Q-0011 (rate-limit) remains TOUCHED-not-closed (admin-JWT reads, 2 trusted users) — flag for a one-line re-confirm.
- **E (UI/UX):** responsive 375/768/1280; loading + error states on the SSR fetches (the 404 blank-form fallback IS the
  error state for the GET); Czech copy via i18n (no leaked English; the banner downgrade copy + the
  `user.cannotDeleteWithInFlightOrders` reason use i18n keys); `components/ui/` primitives; no inline layout `style={}`;
  URL-state pagination on the outbox/payout lists (T-0087a precedent).
- **F (AC traceability):** AC-1..AC-12 each need a proof. AC-7 (cross-host 401) MUST be an integration probe, not a unit
  test. AC-8 (the diff-modal) needs the FE diff-logic proof (provider change fires; VAT-only skips; 404 → blank fallback).
  AC-10 (proactive pre-disable WITH the backend gate intact). AC-12 (check-consistency exit 0 + regen + check:api green).
- **G / Gate 5 (tests):** the **stalled-predicate LIST test is pure logic** → TDD discipline applies (T-0067+; well past
  the grandfather line). Per Gate 5, an after-the-fact predicate test is a **HARD FAIL** — the commit order must show the
  predicate test red→green BEFORE or alongside the `GetStalledPagedAsync` impl. The commits-hint lists `test(T-0127)` as
  commit 2 AFTER `feat` commit 1 — **scrutinize `git log` on the branch**: if the stalled-LIST predicate test (and any other
  pure-logic test — the country-config field-set parity, the order-detail no-redaction projection, the `customerUserId`
  filter pass-through) lands only in a post-implementation `test(...)` commit with no red→green evidence, request a rewrite
  under TDD before approval. (The country-config GET field-set parity and the projection shape are pure mapping — borderline
  pure logic; the stalled predicate is unambiguously in-scope for the mandate.)
- **I (performance / Optimizer):** the order-detail handler composes over a single `GetByIdUnscopedReadOnlyAsync` + a couple
  of projections (maker name) — confirm no N+1 (a LEFT JOIN projection, not a per-row maker lookup in a loop). The two paged
  LISTs are two-round-trip `Count + Skip/Take` (no `COUNT(*) OVER ()` full-scan). `CancellationToken` propagated on every
  read. No `.Result`/`.Wait()`. **Optimizer ping:** the order-detail is a single-aggregate header read and the LISTs are
  standard two-round-trip paged reads — below the >5-entity / multi-step-pipeline hot-path bar. **No Optimizer ping required**
  unless the order-detail projection fans out to >5 joined entities (verify the join count when the diff lands).
- **J (mechanical):** T8 + T9 clean (HIGH-5); check-consistency exit 0 against the 147-tracked baseline.

## RDD parity (ADR 0015)
- No new aggregate / value object / domain service. The reads extend the existing `IAdminQueries` read seam +
  `IOutboxConsumerRepository` (new `GetStalledPagedAsync`) + `IPayoutBatchRepository` (new paged read) + read through the
  existing `ICountryConfigurationRepository.GetByCodeAsync`. **If a NEW repository interface or a materially new
  responsibility lands on `IAdminQueries` / the outbox / payout repos, the corresponding role file under
  `docs/architecture/roles/` must be updated in the same PR** (the T-0126 precedent updated `roles/outbox.md` +
  `roles/payout-batch.md` for the count reads; T-0127 should note the LIST + order-detail seams on `roles/order.md`,
  `roles/outbox.md`, `roles/payout-batch.md`, and the country-config GET on the country-config role). Verify Gate 7 (docs) at PR-open.
- Each new query handler depends on ≤5 collaborators (the T-0111 handlers take a single `IAdminQueries` — the order-detail /
  LIST handlers should be similarly thin). Flag any handler exceeding ~5 collaborators.

## Harvest duty (note for PR-open)
No new recurring finding expected. #2 (T8) and #3 (T9) are codified — CI owns them; if either fires it is a
"violates ruleT8/ruleT9" comment, not a new log row. **One watch item for a potential NEW finding type:** if the
stalled predicate is duplicated by-hand (HIGH-2) rather than shared, and this is the kind of copy-the-predicate drift that
also showed up around the T-0126 count vs the T-0109 due-set — if a "duplicated/​drifting predicate instead of a shared
expression" finding has now landed a 3rd time across tickets, append/increment a recurring-findings row and ping Architect.
Otherwise only append/increment if a genuinely new finding type repeats a 3rd time.

## Preliminary verdict
**NOT YET APPROVABLE — no code to review (parallel run).** The design is sound and every locked decision has a clean
precedent in master (T-0111 admin-query shape; T-0126 stalled predicate + admin-read followup; the `UpdateCountryConfiguration`
field set; the existing `GetByIdUnscopedReadOnlyAsync` + `GetByCodeAsync` read paths; both reused error codes + cs-CZ keys
present). At PR-open, walk HIGH-1..HIGH-6 + the checklist above. The four things most likely to fail and block approval:
1. **Country-config GET shape drift** (HIGH-1) — the GET must echo the NINE config fields and NOT the two mutation-result
   fields, byte-round-trippable with the PUT, or the form drifts and the silent-overwrite fence is not actually removed.
2. **Stalled-LIST predicate drift** (HIGH-2) — demand a shared predicate (or both pinned to the same canonical definition);
   a copied WHERE lets the count tile and the list disagree.
3. **Cross-host 401 integration proof** for the four Unscoped reads (AC-7) — easy to omit; a unit test does not satisfy it.
4. **TDD order on the stalled-LIST predicate test** (Gate 5) — the commits-hint puts tests after impl; demand red→green
   evidence or reject.
Also: the FE diff-modal must gate on an ACTUAL loaded-vs-entered diff (AC-8, not "any provider field present"); the
delete-user pre-disable must SURFACE the signal without removing the authoritative backend gate (AC-10); SecOps Gate 3
sign-off is mandatory before merge (admin Unscoped reads of cross-tenant PII/financial data + the per-user in-flight signal).
