# Gate 8 (Performance) -- admin-ops bundle

Scope: backend + admin surface. T-0108, T-0110, T-0111, ProviderRegistry. No frontend (admin UI is T-0118 -- skipped per brief).
Branch: feat/order-cleanup-bundle (admin-ops commits 2a9ee86..9e7f5c0, 8 commits).
Verdict: GATE8_FOLD -- ships; index gaps folded as Medium (admin paths absent from ADR 0023 budget table, low-frequency, business-hours).

## Scope note: admin surfaces are NOT in the ADR 0023 budget table

ADR 0023 section 1 budgets cover catalog/product TTFB and customer/maker dashboard lists (400 ms p95). The three T-0111 admin reads, the T-0108 advisory, and the T-0110 erasure are admin-host, 99.0 percent business-hours (ADR 0023 section 3). No p95 budget exists for them. I do not invent one -- so an admin-path index gap is Medium (fix this sprint, not a BLOCKER), and the missing budget rows are raised as an open question rather than gated on a fabricated number.

## T-0111 -- three paged admin queries (AdminQueries.cs)

Shape is clean: Unscoped + AsNoTracking + IgnoreAutoIncludes + straight DTO projection + two-pass count (count then page). B1/B2/B6 pass. Maker/Product labels are correlated subqueries in the projection keyed on PK Id -- index-served point lookups, not N+1 round-trips (one SQL statement). Good.

### Index coverage -- audit log: PASS
AdminAuditLogEntryConfiguration.cs:32-37 ships exactly the three composite indexes the filters need, each leading with the filter column + created_at (the sort key):
- admin_user_id, created_at  -> ix_admin_audit_log_admin_user_id_created_at
- target_entity, target_id, created_at  -> ix_admin_audit_log_target_created_at
- action_code, created_at  -> ix_admin_audit_log_action_code_created_at

The TargetEntity-only filter (AdminQueries.cs:150) rides the leading column of the 3-col index -- covered. created_at-only range filters fall back to a scan, immaterial at MVP audit volume (admin writes only; nowhere near the 5k/day outbox rate, ADR 0023 section 2). No gap.

### Index coverage -- orders / invoices: TWO GAPS (Medium)

[Medium] AdminQueries.cs:42-43 -- B3 (orders)
What: GetAllOrders filters on o.CountryCode with no supporting index. OrderConfiguration.cs has (customer_user_id, created_at), (maker_id, state, created_at), state-partial, payout_batch_id-partial -- none lead with country_code.
Cost (model): country filter alone yields a seq scan of orders. At ADR 0023 section 2 year-1 ceiling (about 200 orders/day x 365 ~= 73k rows, under 5 GB DB) a seq scan is tens of ms, not a budget breach -- and CZ-only at launch makes the filter a near-no-op (one country). Real only at multi-country + larger tables.
Fix: add a partial (country_code, created_at DESC) WHERE is_active index on orders when multi-country lands; defer at CZ-only MVP.
Refs: B3; OrderConfiguration.cs:187-212; ADR 0023 section 2.

[Medium] AdminQueries.cs:100-101 -- B3 (invoices)
What: GetAllInvoices filters on i.CountryCode (and created_at range) with no leading index. InvoiceConfiguration.cs:154-180 has invoice_number-uniq, order_id-uniq-partial, (maker_id, created_at), type -- none lead with country_code.
Cost (model): same as above -- seq scan, immaterial at CZ-only MVP volume, grows with multi-country.
Fix: defer; add (country_code, created_at DESC) when multi-country lands.
Refs: B3; InvoiceConfiguration.cs:148-180; ADR 0023 section 2.

### Customer-email + recipient filters: leading-wildcard ILIKE -- flagged, NOT gated

[Medium] AdminQueries.cs:46-49 (and :102-105) -- B3
What: CustomerEmail / Recipient filters use EF.Functions.ILike with a leading wildcard. No btree index serves a leading wildcard; always a seq scan.
Cost (model): full scan of the filtered set per search. At the 73k-row year-1 ceiling, tens of ms on the admin host -- tolerable for a business-hours lookup, but does NOT scale and no index fixes it.
Fix: if it must scale, pg_trgm GIN on contact_email / recipient_name, or anchor to a prefix match. At MVP accept the scan; note the cost on the ticket.
Refs: B3; T-0111 AC (admin search).

## T-0108 -- in-flight advisory (OrderQueries.CountInFlightByCountryAsync)

AsNoTracking + Where(country_code == cc and state in InFlightStates) + CountAsync. One-shot admin advisory on the country-config retype gate -- low frequency. B2 pass.

[Nit] OrderQueries.cs:315-318 -- B3
What: filters (country_code, state); nearest index is ix_orders_state (partial WHERE is_active, leads on state). Planner narrows to in-flight states via that index then filters country in-memory -- adequate. No (country_code, state) composite.
Cost (model): bounded COUNT over the active in-flight slice (only 4 transient states). Sub-budget at MVP; one-shot per admin config-change. Not worth a dedicated index.
Fix: none -- the partial state index suffices at this frequency/volume. Confirmed low-freq advisory, no per-request cost.
Refs: B3; OrderConfiguration.cs:203-205.

## T-0110 -- GDPR erasure (UserDataDeletionService.EraseAsync)

Correctly bounded to one user-scope of data, not a table scan, on the mutation paths:
- in-flight guard: HasInFlightOrderForUserAsync -> AnyAsync on (customer_user_id OR maker_id) + state. customer_user_id rides ix_orders_customer_created; maker_id rides ix_orders_maker_state_created. The OR may force a bitmap-OR / scan, bounded by one user order count (single-digit to low-hundreds). Indexed enough at MVP.
- order anonymize (:67-74): loads this user orders, tracked, per-row AnonymizeContact -- per-entity tracked UPDATEs, fine at one-user volume (the brief own call). Not a bulk-ExecuteUpdate candidate at this cardinality.
- reviews (:78-85): customer_user_id filter rides ix_reviews_customer_user (ReviewConfiguration.cs:78). Indexed.
- refresh tokens (:94-101), addresses (:111-118), user row (:122-128): all single-user-scoped.

[Medium] UserDataDeletionService.cs:107-110 -- B1/B3 (the one unbounded read in the matrix)
What: the unreferenced-address probe materializes EVERY maker RegisteredAddressId into app memory, then does an in-memory NOT-Contains anti-join.
Cost (model): full makers scan on every erasure -- O(total makers), not O(target user data). At MVP maker count (hundreds to low thousands) a few hundred KB pulled per erasure; erasure is rare so absolute cost is low, but this is the only spot in the seam scaling with TOTAL rows rather than the target user. Widens as the maker table grows.
Fix: push the anti-join into SQL as a NOT EXISTS correlated on RegisteredAddressId so Postgres evaluates it index-served instead of streaming all maker FK ids to the app. Defer-acceptable given rarity; capture on T-0110.
Refs: B1, B3; UserDataDeletionService.cs:107-114.

No SaveChangesAsync in the seam (correct -- UoW pipeline commits). CancellationToken propagated on every await (B4 pass). No .Result/.Wait()/.GetAwaiter().GetResult() anywhere (B5 pass).

## ProviderRegistry (deviation 5) -- ProviderRegistry.cs

Singleton built once from IServiceCollection at composition (:42-46); DiscoverKeys enumerates the descriptor list once at startup and caches two read-only sets. GetRegisteredCodes is a switch returning a cached set -- O(1), zero per-request and zero per-provider-change cost. The startup enumeration is O(descriptor count), one-time. No lazy per-request enumeration, no allocation on the validation path. PASS, no finding.

## Self-check
- Every finding has file:line, severity, cost model, one-sentence fix. OK
- No BLOCKER raised, so no ADR/CLAUDE.md citation owed; the index gaps are Medium because admin surfaces carry no ADR 0023 budget (not a budget breach). OK
- No fabricated measurements -- all costs labelled (model), reasoned from ADR 0023 section 2 ceilings. OK
- No finding contradicts an accepted ADR. OK

## Open question raised
Admin read surfaces (cross-tenant order/invoice/audit lists) have no row in the ADR 0023 section 1 budget table. Recommend an entry in docs/questions/open.md so the index gaps have a number to gate against rather than a guess.

Verdict: GATE8_FOLD -- 0 BLOCKER, 0 High, 4 Medium, 1 Nit. Ships; fold the four Medium items onto their tickets.
