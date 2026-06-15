# T-0127 — Admin read gaps — FINAL review

**Verdict: APPROVE.** Branch `feat/admin-read-gaps`, T-0127 scope = commits `ec1c5bc` (groom) + `61261e4..ba593a7`.
Reviewed against the preliminary draft (HIGH-1..6), the checklist, ADR 0013/0014/0015/0023, and quality-gates Gate 5.
Every checklist row passes; the two deferred/fenced T-0118c ACs (AC-4 + AC-5) are now MET. No BLOCKERs.

> Scope note: `git diff master...HEAD` is misleadingly large (master is behind; it pulls in payout-batch
> domain, reviews, maker-payouts, delete-user from merged siblings). The PR's actual surface is the 8
> T-0127 commits above (42 files), which is what this review walks.

---

## (b) Country-config fence-removal disposition — AC-4 + AC-5 MET

The headline BLOCKER (PR-2 full-replace silent-overwrite) is structurally removed:

- **GET returns the NINE editable fields, not the mutation-result fields.**
  `GetCountryConfiguration.GetCountryConfigurationResponse` (`GetCountryConfiguration.cs:40-50`) carries exactly
  `StandardVatRateBp, ReducedVatRateBp, InvoicingMode, PlatformFeeRateBp, DefaultShippingPriceMinor,
  DefaultPaymentProvider, DefaultShippingCarrier, DefaultRegistry, DefaultEmailProvider` + `CountryCode`. It does
  NOT carry `InFlightOrderCount` / `ProviderChanged`. Byte-round-trippable with the PUT request shape
  (`CountryConfigurationsController.cs:41-52`). Reads through `GetByCodeAsync` (AsNoTracking), null → reused
  `CountryConfigurationNotFound` (`GetCountryConfiguration.cs:72-76`). No new error code.
- **SSR pre-fill + 3-outcome branching** (`countries/[code]/page.tsx:57-100`): a `ConfigResolution` discriminated
  union — `found` → info banner (`fullReplaceNote`); `notFound` → blank + warning (`noPrefillNote`); `error` →
  error + warning. Warning downgraded to info on the found path (**AC-4 met**). 404 → graceful blank fallback,
  not a 500.
- **Modal gates on an ACTUAL diff** (`country-config-form.tsx:157-175, 238-248`): `changedProviders` filters each
  provider value against `loadedProviders[key]`. A VAT/fee-only edit yields `anyProviderChanged === false` and
  `submit(undefined)` runs with NO modal (**AC-5 met**). The 404/blank path treats any non-empty provider as a
  change (correct friction-preserving default — nothing to diff). Backend
  `country.providerConfirmationMismatch` stays authoritative; the diff is UX-only.

## (c) In-flight-signal completeness — COMPLETE (all 5 states)

`userHasInFlightOrders` (`admin-orders.ts:261-294`) probes **all five** in-flight states
`[PendingPayment, Paid, Accepted, Shipped, Disputed]` — loops each, `pageSize:1`, returns `true` on the first
non-empty page. This is NOT the "probes one state" partial that the draft flagged as a possible UX-completeness
nit; the pre-disable is complete and the state set exactly matches §A.3/AC-10 (no terminal/settled states). On a
transient read failure it returns the error → the panel maps it to `unknown` and does NOT pre-disable
(`delete-user-panel.tsx:81-86, 201, 208`). The backend T-0110 gate stays authoritative: `canSubmit` includes
`!preBlocked` but `eraseUser` re-checks server-side and the panel still renders
`user.cannotDeleteWithInFlightOrders` reactively. UI surfaces, does not replace — correct. Cost: 5 sequential
reads on a one-shot lookup-submit, off any hot path — acceptable.

## (d) BLOCKERs

None.

## (e) Fold list (non-blocking — address in this PR or a follow-up note)

1. **Doc-comment cruft in `IAdminQueries.cs:65-66`** — a thinking-out-loud fragment survived into the XML doc:
   *"Composes over `IPayoutBatchRepository.Unscoped()` — wait, the batch repo has no Unscoped queryable, so..."*.
   Behaviour is correct (reads the DbSet directly); just delete the "— wait, ..." aside.
2. **Stale method name in `admin-orders.ts:237-239`** doc comment says order-detail is *"over `GetByIdUnscopedAsync`"*;
   the backend actually composes `orders.Unscoped()` in `AdminQueries.GetOrderDetailAsync` (`AdminQueries.cs:198`),
   not the by-id method. Cosmetic.
3. **RDD role-file touch (Gate 7, soft):** no role doc updated. Justified — no new aggregate/VO/domain-service/
   repository-interface was added; `GetStalledPagedAsync`, `GetPayoutBatchesPagedAsync`, `GetOrderDetailAsync` are
   new read methods on existing seams (`outbox.md` already lists "stalled-event surfacing" as a core
   responsibility). Optional: add a one-line "browse list" note to `outbox.md` / `payout-batch.md` read surfaces.

## (f) Checks

| Check | Result |
|---|---|
| **HIGH-1 country-config fence** | PASS — GET=9 fields, SSR 3-branch, diff-gated modal, AC-4/AC-5 met |
| **HIGH-2 shared stalled predicate** | PASS — one `IOutboxConsumerRepository.StalledPredicate` Expression used by BOTH `CountStalledAsync` + `GetStalledPagedAsync` (`OutboxConsumerRepository.cs:54, 66`); count + list cannot drift |
| **HIGH-3 admin Unscoped / IDOR** | PASS — all 4 reads `[Authorize]` admin host, no owner predicate; cross-host 401 integration probe on all 4 (`AdminReadGapsIntegrationTests.cs:364-380`); order-detail carries `customerEmail` intentionally (operator surface, no redaction) |
| **HIGH-4 delete-user pre-disable** | PASS — surfaces, does not replace; all 5 in-flight states; backend gate authoritative |
| **AC-1..AC-12** | All traceable (matrix below) |
| **Gate 5 / TDD** | PASS (carve-out) — predicate test + impl land together in `2944883` (test-alongside); repository WHERE Expression, not a domain method, consistent with the T-0126 ruling. `StalledOutboxPredicateTests.cs` pins all 5 cases (stalled-in / due-out / processed-out / acknowledged-out / fresh-out) via the entity's public transitions |
| **T8 i18n parity** | PASS — zero new `BusinessErrorMessage` codes (both 404s reuse `CountryConfigurationNotFound` / `OrderNotFound`); sampled FE keys present in `cs-CZ.ts` |
| **T9 unique-index** | PASS — no migration in T-0127 scope |
| **check-consistency** | EXIT 0 — clean (151 tracked); the +4 expected T1 false-positives are pre-existing Reviews/Users features, no NEW T-0127 violation |
| **NSwag regen** | PASS — admin host only (`.spec-hashes.json`: only `admin-api.v1` hash changed) |
| **i18n hardcoded Czech** | PASS — none in changed TSX (all via `t()`) |
| **Architecture** | PASS — handlers happy-path only, no `SaveChangesAsync`, controllers one-liners, `Core.AppServices` no EF, Server Components default, no `useEffect` data fetch (the one `useEffect` is modal escape/overflow side-effect), money as `_minor`/`long` |
| **RDD ≤5 collaborators** | PASS — each new handler takes ONE collaborator (`IAdminQueries` / `ICountryConfigurationRepository` / `IOutboxConsumerRepository`) |
| **Optimizer** | Not required — single-aggregate header read + standard two-round-trip paged lists; correlated maker/product subqueries are projection-time (no in-loop N+1) |

### AC matrix

| AC | Proof |
|---|---|
| AC-1 country-config GET (AsNoTracking, 9 fields) | `GetCountryConfiguration.cs:72`, integration round-trip `:248-267` |
| AC-2 GET 404 reuses code | `GetCountryConfiguration.cs:75-76`, integration `:269-278` |
| AC-3 order-detail privileged (customerEmail, no redaction) | `AdminOrderDetailDto.cs:37`, `AdminQueries.cs:198-250`, integration `:282-297` |
| AC-4 warning→info on found path | `countries/[code]/page.tsx:77-100` |
| AC-5 VAT-only save skips modal | `country-config-form.tsx:167-175, 243-247` |
| AC-6 payout LIST Unscoped cross-maker | `AdminQueries.cs:253-288`, integration `:349-360` |
| AC-7 cross-host 401 (integration) | `AdminReadGapsIntegrationTests.cs:364-380` (all 4 reads) |
| AC-8 diff-modal logic | `country-config-form.tsx:157-175` |
| AC-9 stalled LIST = count set | shared `StalledPredicate`; integration asserts exactly 2 stalled, due+processed excluded `:333-345` |
| AC-10 delete-user pre-disable + backend gate intact | `delete-user-panel.tsx:201-208`, `admin-orders.ts:261-294` |
| AC-11 order-detail re-wire / Q-0024 resolved | `orders/[orderId]/page.tsx:76-94` real DTO header + separate audit-log trail |
| AC-12 check-consistency 0 + regen + check:api | exit 0; admin-only regen |
