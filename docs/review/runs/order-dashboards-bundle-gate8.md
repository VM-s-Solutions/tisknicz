# Gate 8 (Performance) - order-dashboards bundle

- **Branch:** `feat/order-cleanup-bundle` (7 commits, T-0086a/b, T-0087a/b, T-0088, T-0089)
- **Reviewer:** optimizer
- **Date:** 2026-06-12
- **Verdict:** **GATE8_FOLD** - zero blockers; 2 HIGH / 3 MEDIUM, all with cheap folds; bundle-size overage is >90 % attributable to Q-0014 (owned by its standalone ticket)

## Backend - T-0088 invoice download

**PASS:**
- Order lookups use `AsNoTracking` read-only repository variants (`GetByIdForCustomerReadOnlyAsync` is new and correctly mirrors the maker variant).
- Blob is **streamed**, not buffered: `AzureBlobStorageClient.DownloadAsync` uses `DownloadStreamingAsync` (`AzureBlobStorageClient.cs:82`) - matches the T-0064 attachment precedent, no `byte[]` materialisation.
- `enableRangeProcessing: false` is sane: invoices are <= ~100 kB platform artifacts (documented T-0075 rationale in both controllers).
- `CancellationToken` propagated to every await; no `.Result`/`.Wait()` in the diff (B4/B5 clean).
- 304 path disposes the blob stream before returning.
- T-0089 is test-only - skipped per gate scope.

**Findings:**

```
[HIGH] backend/src/Makables.Infra.Database/Invoices/InvoiceRepository.cs:123 - B2
What: GetByOrderIdAsync is a tracked query, now reused by two read-only download endpoints
      (Web.Customer/OrdersController.cs:482, Web.Maker/OrdersController.cs:240).
Cost: cost model - one change-tracker snapshot per Invoice row per download; small absolute
      (single row), but breaks the CLAUDE.md Performance AsNoTracking convention on a
      read path, and this PR itself ships the fix pattern for Order.
Fix: add GetByOrderIdReadOnlyAsync (AsNoTracking) mirroring this PR's
     GetByIdForCustomerReadOnlyAsync; keep the tracked variant for the T-0068b webhook flow.
Refs: CLAUDE.md Performance; charter B2; T-0088.
```

```
[Nit] backend Web.Customer/OrdersController.cs:507 (and maker mirror) - conditional GET cost
What: ETag/304 is evaluated app-side after DownloadStreamingAsync, so a 304 still pays a
      full blob GET round trip (opened then disposed).
Cost: cost model - ~10-40 ms + 1 storage transaction per conditional hit. Cold path.
Fix: pass If-None-Match down via BlobRequestConditions so storage returns 304 directly.
Refs: same shape as T-0064 precedent - backlog, do not fold.
```

## Frontend - route bundle sizes (measured)

Next 16.2.6 Turbopack no longer prints First Load JS; sizes computed from
`page_client-reference-manifest` chunk unions, gzip level 6. Framework baseline
(`rootMainFiles`) = **129.7 kB gzip** (the "131.8 kB" Gate 8 baseline, re-measured).

| Route | Route chunks (gzip) | ~First Load | Bundle-unique marginal | Of which cs-CZ dict copies |
|---|---|---|---|---|
| `/dashboard/zakaznik/objednavky` | 32.2 kB | ~161.9 kB | **+14.6 kB** | ~12.4 kB (1 copy) |
| `/objednavka/[id]` (extended) | 37.5 kB | ~167.2 kB | **+6.1 kB** | 0 (dict chunk pre-existing/shared) |
| `/dashboard/maker/objednavky` | 44.5 kB | ~174.2 kB | **+27.4 kB** (over line) | ~24.8 kB (2 copies) |
| `/dashboard/maker/objednavky/[orderId]` | 49.4 kB | ~179.1 kB | **+18.6 kB** (over line) | ~12.4 kB (1 copy; thread chunk shared with customer detail) |

```
[HIGH - attributed to Q-0014] maker list +27.4 kB / maker detail +18.6 kB marginal - over the +15 kB line
What: both maker order routes exceed the marginal review line, but >90 % of the overage is
      duplicated cs-CZ dictionary copies (Q-0014 pattern, identical on the pre-existing
      maker produkty routes). Net new component code is ~2.2-6.2 kB gzip per route - good.
Cost: measured - see table; two dict copies ship in each maker route's first load.
Fix: none in this PR - Q-0014's standalone ticket owns de-duplication; this run updates
     the Q-0014 evidence (below). Real-code marginals all pass.
Refs: docs/questions/open.md Q-0014, Q-0015; charter F6.
```

**Q-0014 marginal update (this bundle):** dictionary-bearing chunks **17 -> 19**; per-copy size
**10.0 -> 12.4 kB gzip** (+218 dictionary lines = +2.4 kB/copy); aggregate duplicated payload across
the build now **266 kB gzip**. The shared `OrderMessageThread` chunk (13.7 kB) is correctly shared
between customer and maker detail routes - chunking itself is healthy; only the dictionary
inlining is the problem.

## Frontend - behaviour checks

**PASS:**
- **Thread component** (`components/shared/order-message-thread.tsx`): exactly 1 request/30 s while visible, paused on `visibilitychange`, in-flight ref prevents overlap; mark-read is serialized **after** the refetch and only fires on counterparty news (plus once on mount, by Q6 lock) - no parallel storm; dedupe is one `Set` pass over loaded messages (paged 20; trivial at low hundreds). Both consumers wrap callbacks in `useCallback` keyed on order id per the poll-identity contract. The poll `useEffect` is the sanctioned Q5 exception - initial data arrives as an SSR prop, the timer pulls deltas only; not an F2 violation.
- **List pages**: exactly one SSR list request per render (maker tabs map to at most one `state`; metadata is static); pagination and tabs are `<Link>`-based - zero client fetching.
- **Images**: N/A - order rows are text-only; no `<img>`/`next/image` surface added.
- **state-labels widening**: template-literal union + string-literal switch cases - type-level only, zero runtime delta (verified in diff).
- Blob downloads client-side use `parse: 'blob'` with a dedicated 120 s timeout (8 s JSON default untouched).

**Findings:**

```
[MEDIUM] frontend/src/app/(customer)/objednavka/[id]/page.tsx:51 + (maker)/.../[orderId]/page.tsx:54 - duplicate SSR detail fetch
What: generateMetadata and the page body each call getCustomerOrderDetail/getMakerOrderDetail;
      apiFetch composes a fresh AbortSignal.timeout per call (api-fetch.ts:143), which defeats
      Next/React fetch memoization - 2 identical backend GETs per detail view.
Cost: cost model - doubles backend load on both detail endpoints (ownership-scoped order query
      + projection x2 per page view); TTFB mostly unaffected (Next 16 streams metadata) but it
      is pure waste on every order-email click and maker workflow visit.
Fix: wrap the detail getter in React cache() (per-request memo) in the helper or page module.
Refs: charter F5 (re-fetch of data the SSR render already produced); Q5 freshness lock unaffected.
```

```
[MEDIUM] frontend/src/app/(customer)/objednavka/[id]/page.tsx:144 + (maker)/.../[orderId]/page.tsx:100 - detail -> thread waterfall
What: thread page-1 prefetch is awaited after the detail fetch (TrackingDetail is an async
      Server Component with no Suspense boundary), serializing two backend round trips.
Cost: cost model - +1 sequential backend RTT (~30-80 ms intra-Azure, more at p95) added to every
      order-detail SSR render, both audiences. No ADR 0023 budget row exists for these surfaces
      (Q-0015), so no budget breach - but the fix is one line.
Fix: Promise.all the detail + messages(route param, page 1) fetches - the messages endpoint is
     itself ownership-scoped so the parallel call is safe; customer side wastes one request only
     in the PendingPayment branch (acceptable trade-off; note as Alternatives Considered).
Refs: charter workflow item 4; ADR 0023 (no row - see Q-0015).
```

```
[MEDIUM] frontend/src/app/(customer)/dashboard/zakaznik/objednavky/ - missing error.tsx
What: customer order list ships loading.tsx but no error.tsx, and no error boundary exists
      anywhere up the (customer) tree; the maker list (this same bundle) ships one.
Cost: a thrown render error (anything apiFetch does not catch) falls through to the default
      Next error surface instead of the styled retry the maker side has.
Fix: add error.tsx mirroring (maker)/dashboard/maker/objednavky/error.tsx.
Refs: gate item 9; T-0086a vs T-0087a parity.
```

## Summary

| Severity | Count | Items |
|---|---|---|
| BLOCKER | 0 | - |
| HIGH | 2 | B2 tracked invoice query; >15 kB maker-route marginals (attributed Q-0014, fix owned elsewhere) |
| MEDIUM | 3 | duplicate generateMetadata detail fetch (both detail pages); detail->thread waterfall (both detail pages); customer list error.tsx |
| Nit | 1 | 304 still pays the blob GET |

**Verdict: GATE8_FOLD.** Nothing ships a budget breach or breaks a non-negotiable. Fold this sprint:
(1) React cache() on the two detail getters - highest leverage, halves detail-endpoint load;
(2) GetByOrderIdReadOnlyAsync; (3) parallelize the thread prefetch; (4) customer list error.tsx.
Q-0014 evidence updated above for the standalone ticket; Q-0015 (route JS budget) remains the open
gate-line question.
