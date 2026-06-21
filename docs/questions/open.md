# Open questions for the user

> Append entries here when an agent needs a decision from the user that cannot be made internally. Reviewed at sprint checkpoints. Once answered, the decision moves into the relevant ADR / user story / ticket, and the entry is marked `answered`.

## Template

```
## Q-NNNN — <short title>
- **From:** <agent>
- **Ticket / context:** T-NNNN or "general"
- **Asked:** YYYY-MM-DD
- **Blocking:** yes | no
- **Question:** <one or two sentences>
- **Options the agent has considered:** <bullets, optional>
- **Status:** open | answered | obsolete
- **Answer (filled by user):**
```

---

## Q-0001 — Multi-user per maker account
- **From:** BA
- **Ticket / context:** general; arose during Batch 1 personas
- **Asked:** 2026-05-21
- **Blocking:** no
- **Question:** Workshops will eventually want multiple users per maker (owner + operators). When do we plan to enable this — post-MVP v1.1, or earlier?
- **Options the agent has considered:**
  - Defer to post-MVP v1.1 (default — keeps MVP simple, schema can accommodate via `maker_user` join table later)
  - Build now (adds permissions UI, invitation flow, audit trail on maker actions)
- **Status:** open
- **Answer (filled by user):**

## Q-0002 — Custom-quote flow for on-request products
- **From:** BA
- **Ticket / context:** general; trust-model decision deferred this
- **Asked:** 2026-05-21
- **Blocking:** no
- **Question:** Spec mentions `price_type = 'on_request'`. Without pre-purchase contact (locked in Batch 1), how should on-request products work at launch?
- **Options the agent has considered:**
  - Hide on-request products at launch — only `fixed` and `from` prices live in catalog
  - Show on-request products but the "Order" button submits a brief that creates an order in a new pre-existing state `quote_pending`; maker responds with a price; customer accepts → pays. Requires extra state in the order machine.
  - Allow on-request listings but disable the order CTA, instead show a generic "Coming soon — direct quotes" placeholder until post-MVP
- **Status:** open
- **Answer (filled by user):**

## Q-0003 — Admin assistant background
- **From:** BA
- **Ticket / context:** general; admin persona
- **Asked:** 2026-05-21
- **Blocking:** no
- **Question:** Is the admin assistant technical (developer-adjacent) or non-technical (operations / customer-support background)? Affects how much we invest in admin UX vs. CSV/SQL escape hatches.
- **Status:** open
- **Answer (filled by user):**

## Q-0004 — Ghost lockout slots for unknown emails
- **From:** dotnet-backend (T-0020 reviewer)
- **Ticket / context:** T-0022 (AuthService.Login)
- **Asked:** 2026-05-23
- **Blocking:** before T-0022 starts
- **Question:** ADR 0012 §Lockout says "if the email doesn't exist, we still consume ghost lockout slots (rate limit by EmailNormalized even if user is missing) to prevent enumeration." The `User` entity stores its own `FailedLoginCount`/`LockedUntil`; there is no place to record lockout state for emails that have NO user row. Where should ghost lockout state live?
- **Options the agent has considered:**
  - In-memory cache on the AuthService host with a per-process LRU. Simple. Loses state on restart and per-instance — defeats the rate-limit intent under multi-host scale-out.
  - A new `login_attempt_buckets` table keyed by `email_normalized` with `attempts` + `locked_until`, written on both unknown-email and known-email failed logins. Persistent, scale-out safe; one extra write per failed login. Probably the right call.
  - Lean on the ASP.NET RateLimiter at the endpoint level keyed by email (request body parsing in the limiter is awkward) — covers DoS but does not match the ADR's per-email semantics.
- **Status:** open
- **Answer (filled by user):**

## Q-0005 — Google OAuth PKCE
- **From:** dotnet-backend (T-0026 reviewer)
- **Ticket / context:** T-0026 Google OAuth, deferred
- **Asked:** 2026-05-25
- **Blocking:** no — confidential client + client secret already binds the token-exchange leg
- **Question:** Should the Google OAuth flow add PKCE (RFC 7636) on top of the existing confidential-client + signed-state pattern? Google recommends it; ADR 0012 doesn't mandate it; the current state binding (redirect URI + CSRF cookie hash, HKDF-derived signing key) already covers the headline OAuth login-CSRF and code-injection vectors PKCE was designed against. PKCE adds one server-side `code_verifier` table or cookie + an extra round-trip.
- **Options the agent has considered:**
  - Add PKCE before launch — defense-in-depth against future client-secret leak.
  - Defer to post-launch hardening — accept the residual risk because the secret is Key-Vault-only.
  - Skip permanently — confidential clients with rotating secrets are sufficient per OAuth 2.1 draft.
- **Status:** open
- **Answer (filled by user):**

## Q-0006 — Czech ČSÚ právní-forma číselník — full mirror, on-demand, or pass-through?
- **From:** dotnet-backend (T-0032 CQ reviewer n-4)
- **Ticket / context:** T-0032 — `Makables.Infra.Common.Czech.CzechLegalForms`
- **Asked:** 2026-05-25
- **Blocking:** no — unknown codes already fall through as the trimmed numeric, so launch is safe.
- **Question:** The map currently covers 12 of the most common ARES `pravniForma` codes. The Czech ČSÚ číselník has roughly 100 entries (with revisions). What should the source of truth be?
- **Options the agent has considered:**
  - Mirror the full list now from the ČSÚ open-data CSV. Pro: any registered Czech entity renders correctly. Con: pulls 100 entries into source for a UX nicety; revisions become a code-change.
  - On-demand: extend the map only when production traffic surfaces an unknown code (current behaviour). Pro: no premature work. Con: occasional ugly "611" appearing in the UI until backfilled.
  - Pass-through always: drop the resolver, surface the raw code, let the frontend render its own translation. Pro: zero domain-knowledge here. Con: same ugly-code problem, plus loses the server-side rendering hook for invoices / labels.
- **Status:** open
- **Answer (filled by user):**

## Q-0007 — Comgate HttpClient missing timeout (parity with Packeta T-0070 fold)
- **From:** dotnet-backend (T-0070 Gate 8 fold)
- **Ticket / context:** T-0070 — shipping-pipeline bundle Gate 8 review
- **Asked:** 2026-06-08
- **Blocking:** no — Polly retry pipeline caps total wall-clock; HttpClient default ~100s only matters during a hung-socket scenario that the retry budget would not recover from anyway.
- **Question:** The T-0070 Gate 8 fold added `client.Timeout = TimeSpan.FromSeconds(30)` to the Packeta `AddHttpClient` registration to cap single-attempt latency. While applying it, the agent noticed `services.AddHttpClient(ComgatePaymentProvider.HttpClientName)` at `AddMakablesClients.cs:201` registers Comgate with NO explicit timeout — same bug pattern Gate 8 flagged for Packeta. Should we apply the same 30s timeout to Comgate?
- **Options the agent has considered:**
  - Apply 30s timeout to Comgate now in a follow-up ticket (T-0091?). Pro: closes the same hot-path risk for payments. Con: needs its own PR + reviewer pass.
  - Wait for Gate 8 to flag it during a payment-ticket review. Pro: scope-creep avoidance. Con: latent risk during payment 5xx storms.
  - Add `Comgate:TimeoutSeconds` to ComgateOptions + bind from config. Pro: more flexible. Con: bigger change; out of scope for a Comgate timeout fix.
- **Status:** open
- **Answer (filled by user):**

## Q-0008 — Function-host DbContext MARS race with iterate-while-mutating pattern
- **From:** dotnet-backend (T-0077/T-0078 integration test fix)
- **Ticket / context:** T-0077 AutoDeliverOrdersFunction + T-0078 SyncShipmentStatusesFunction
- **Asked:** 2026-06-09
- **Blocking:** no — workaround applied in the production Function code at T-0078 Gate 8 fold (BLOCKER finding; commit applied option 1: materialize-up-front before per-row `mediator.Send`). Architect input still wanted for the longer-term posture (option 2 per-row IServiceScope is the cleaner end-state; option 1 is the pragmatic MVP solution).
- **Question:** Both new Timer-trigger Functions iterate an `IAsyncEnumerable<Order>` (or `<string>`) returned from a repository method while dispatching `mediator.Send` per row. The mediator's command handlers load + mutate the same Order via the SAME scoped DbContext. PostgreSQL/Npgsql does NOT support MARS (Multiple Active Result Sets) — the AsyncEnumerable reader is still open when the handler tries to reuse the connection. The integration tests work around this by materializing the stream into `List<T>` BEFORE the loop. Should the Functions adopt the same pattern, OR open per-row scopes, OR is there a cleaner architectural solution?
- **Options the agent has considered:**
  - Materialize the enumerable up-front in the Function (matches the integration test workaround; simple; loses streaming benefit at high batch sizes — MVP volume is ~10-200/day so non-issue).
  - Open a per-row IServiceScope inside the Function loop (each row gets a fresh DbContext; clean separation; more allocations per run; matches AspNetCore request-scope semantics).
  - Configure the repository method to return materialized `IReadOnlyList<T>` instead of `IAsyncEnumerable` (changes the interface contract; explicit non-streaming).
  - Wait for evidence of production issues — current MVP volume is small and Function runs are infrequent (daily/6h).
- **Status:** open
- **Answer (filled by user):**

## Q-0009 — IMakerRepository.GetByUserIdAsync over-fetch on Maker host read paths
- **From:** optimizer (T-0081/T-0082 Gate 8 fold)
- **Ticket / context:** order-queries-bundle Gate 8 review
- **Asked:** 2026-06-09
- **Blocking:** no — current cost is small on the 400 ms p95 budget; concern is CPU/change-tracker overhead per request, not SQL.
- **Question:** Both Maker-host order-list and order-detail handlers call `IMakerRepository.GetByUserIdAsync(userId)` to resolve `makerId` from session. This returns a fully tracked `Maker` aggregate (~15-20 columns + AutoIncludes) when only `maker.Id` is read. Should we add a projection-only `GetIdByUserIdAsync(string userId, CancellationToken) -> Task<string?>` and switch the Maker-host read paths to it?
- **Options the agent has considered:**
  - Add the projection-only method + swap call sites. Two-line repo change + 2 controller/handler swaps. Drops change-tracker cost from every Maker dashboard render. ~30 min work.
  - Wait for evidence of perf issue in production (Maker dashboard is the lowest-traffic surface — orders only fetched on-demand). Accept the wart as part of consistent repository surface.
  - Cache the makerId in a per-request scoped IMakerSessionContext so multiple handlers in the same request resolve it once.
- **Status:** open
- **Answer (filled by user):**

## Q-0010 — Composite indexes for alternate sort orders on Orders
- **From:** optimizer (T-0080/T-0081 Gate 8 audit)
- **Ticket / context:** order-queries-bundle Gate 8 review
- **Asked:** 2026-06-09
- **Blocking:** no — MVP scale (<100K orders); sequential scan + in-memory sort under 400 ms p95 budget. Degrades past ~500K rows.
- **Question:** `OrderSort` exposes 5 arms (CreatedAtDesc default, CreatedAtAsc, TotalAmountDesc, TotalAmountAsc, StateAsc). Default `CreatedAtDesc` is covered by composite indexes (`ix_orders_customer_created`, `ix_orders_maker_state_created`). The alternate sort arms (`TotalAmountDesc/Asc`, `StateAsc`) trigger a sequential scan + in-memory ORDER BY at the database. Index migrations to cover them are out-of-bundle (reads-only bundle). Should we add covering indexes now or wait for production volume to warrant?
- **Options the agent has considered:**
  - Add per-customer + per-maker composite indexes on (CustomerUserId, TotalAmountMinor) etc — 3-4 new indexes. Pre-emptive but cheap in PG.
  - Wait for production volume past ~100K orders / per-customer cardinality past ~50 orders to trigger.
  - Drop the alternate sort options at MVP — UI only ships CreatedAtDesc as the default; alternate sorts are a forward-looking feature for power users.
- **Status:** open
- **Answer (filled by user):**

## Q-0011 — Rate limiter "default" policy mounted nowhere
- **From:** secops (order-cleanup-bundle Gate 3, check 13)
- **Ticket / context:** order-cleanup-bundle (T-0079 + T-0083); pre-existing gap, not introduced by this bundle
- **Asked:** 2026-06-09
- **Blocking:** no
- **Question:** `AddMakablesRateLimiting.cs:57-63` defines a *named* "default" fixed-window policy, but there is no `GlobalLimiter` and no `[EnableRateLimiting]` attribute on any controller — only `addresses-autocomplete` and `shipping-widget-config` are actually limited. `PostMessage` (2000-char bodies, authenticated) and every other endpoint are effectively unlimited. Email spam is already capped by the 5-min digest debounce (1 email / 5 min / order / direction); the residual risk is DB-bloat spam from a valid-JWT caller. How should the "default" policy be mounted?
- **Options the agent has considered:**
  - Mount "default" globally per host (`RateLimiterOptions.GlobalLimiter` or `[EnableRateLimiting("default")]` on `MakablesApiController`) + add a per-user partition for message posting, mirroring the autocomplete policy shape.
  - Per-endpoint attribute on `PostMessage` only — narrowest change, leaves the rest of the surface unlimited.
  - Defer until traffic data exists — the surface requires a valid JWT and MVP volume is small.
- **Status:** ANSWERED 2026-06-21 → **T-0136** (`feat/secops-hardening-bundle`).
- **Answer (filled by user):** Mount the per-audience "default" policy as the per-host `GlobalLimiter` (covers `PostMessage` + the whole un-attributed surface) AND add a tight per-IP `"auth"` policy (10/min, no queue) class-level on `AuthController` (the brute-force/credential-stuffing surface; matches the ADR 0023 §4 "failed login >50/min/IP" alert intent). 429 stays a raw middleware rejection (no `BusinessErrorMessage`, no i18n key).
- **Note (2026-06-14, admin-ops bundle):** touched but not closed by the admin-ops bundle (T-0108/T-0109/T-0110/T-0111); admin endpoints are admin-JWT-gated (low spam risk); stays open as a standalone secops follow-up against the customer/maker hosts. Flagged for secops Gate 3 re-confirmation of the admin mutation surface; no scope expansion in any bundle ticket.

## Q-0012 — Email-enrichment collaborator sprawl (ADR 0015 budget)
- **From:** reviewer (order-cleanup-bundle Gate 4, MEDIUM-3)
- **Ticket / context:** order-cleanup-bundle (T-0079 + T-0083); escalated to Architect per ADR 0015
- **Asked:** 2026-06-09
- **Blocking:** no
- **Question:** `PostCustomerOrderMessage.Handler` + `PostMakerOrderMessage.Handler` take 11 constructor dependencies (ADR 0015 budget ~5); `CancelExpiredOrder.Handler` takes 7. The repeated email-payload enrichment block (users + makers + languageResolver + publicAppUrls to resolve recipient email/name/language/action-URL at enqueue time) is the 3rd+ occurrence of this pattern (T-0067/T-0071/T-0076 precedents). Should the enrichment collapse behind a seam, and if so where?
- **Options the agent has considered:**
  - Extract a single `IEmailRecipientResolver` collaborator that owns the users + makers + languageResolver + publicAppUrls cluster (Architect to design the seam; handlers drop to ~7 deps).
  - Accept the sprawl as inherent to enrichment-at-enqueue — the deps are real, the pattern is consistent, and the budget is a heuristic.
  - Move enrichment to dispatch-time (`EmailSendService` side) so handlers only emit `{orderId, messageId}` — minimal payloads per the original T-0079 §C.7 shape, at the cost of dispatch-time DB reads and losing the snapshot-at-enqueue semantics.
  - Harvest-duty candidate for `docs/review/recurring-findings.md` once a direction is approved.
- **Data point (2026-06-12, refund-dispute bundle):** `ResolveDispute.Handler` takes 10 constructor deps (`ResolveDispute.cs:89-99` — orders, disputes, mediator, users, outbox, clock, session, languageResolver, publicAppUrls, logger) vs the ADR 0015 budget of ~5. This is the **4th occurrence** of the enrichment-collaborator pattern (T-0067/T-0071/T-0076 → T-0079/T-0083 → T-0105/T-0106). Architect seam design is now warranted.
- **Status:** open
- **Answer (filled by user):**

## Q-0013 — /auth/login latent 404 (middleware + ~10 links)
- **From:** reviewer (checkout-flow-bundle final review, N-6)
- **Ticket / context:** checkout-flow-bundle (T-0084a + T-0084b + T-0085); pre-existing, not introduced by the bundle
- **Asked:** 2026-06-09
- **Blocking:** no
- **Question:** The `(auth)` route group adds no URL segment, so the login page serves at `/login`, but `middleware.ts:24` redirects dashboard routes to `/auth/login` and ~10 `<Link href="/auth/login">` exist repo-wide (`register-form.tsx`, `verify-client.tsx`, `reset-client.tsx`, `profile-client.tsx`, `pro-makery/page.tsx`, `register-maker-form.tsx`) — all 404. The checkout bundle's new redirects correctly use `/login`. How should the stale references be fixed?
- **Options the agent has considered:**
  - Quick-fix ticket (S): sweep the ~10 references + middleware to `/login`. Recommended.
  - Rename the folder to un-grouped `auth/` so `/auth/login` becomes real — URL churn for an already-live route.
  - Leave until the next auth-area ticket picks it up — latent 404 on every affected link in the meantime.
- **Status:** answered
- **Answer (filled by user):** Fixed in T-0125 (debt-codification bundle, 2026-06-15) — all frontend-nav `/auth/login` refs swept to `/login` (the `(auth)` route group adds no URL segment). API-client `/api/v1/auth/login` refs left untouched (correct backend routes).

## Q-0014 — i18n dictionary inlined into 17 client chunks
- **From:** optimizer (checkout-flow-bundle Gate 8, HIGH finding)
- **Ticket / context:** checkout-flow-bundle Gate 8; pre-existing pattern, inflated by every dictionary growth
- **Asked:** 2026-06-09
- **Blocking:** no
- **Question:** `cs-CZ.ts` (~10 kB gzip) is bundled into every client chunk that imports `t()` — 17 private copies, 44–63% of each new checkout route chunk; every cross-route navigation re-downloads ~10 kB gzip that a shared chunk would cache once. How should the dictionary be de-duplicated?
- **Options the agent has considered:**
  - Extract `lib/i18n` into a shared cached chunk (Turbopack chunking / `optimizePackageImports` config).
  - Split the catalog per domain (`checkout.*`, `order.*`, …) with per-route imports so client leaves carry only their slice.
  - Server-only `t()` + prop-drilling resolved strings into client leaves.
  - Standalone perf ticket recommended — single largest measurable win available (~10 kB gzip saved on every route transition, shrinks all 17 client chunks).
- **Status:** open
- **Answer (filled by user):**

## Q-0015 — Frontend bundle budget undefined (ADR 0023 gap)
- **From:** optimizer (checkout-flow-bundle Gate 8, same run)
- **Ticket / context:** checkout-flow-bundle Gate 8; ADR 0023 §1
- **Asked:** 2026-06-09
- **Blocking:** no
- **Question:** ADR 0023 defines no First Load JS budget (and no checkout TTFB row); the shared root baseline alone is 131.8 kB gzip, so the de-facto ~150 kB Gate 8 review line is mathematically unreachable for any route (all routes measure 157–170 kB). What budget should Gate 8 enforce? Architect input wanted.
- **Options the agent has considered:**
  - Amend ADR 0023 with a realistic absolute budget (e.g. baseline + 40 kB marginal per route).
  - Adopt a marginal-cost budget (per-route delta over the shared baseline) instead of an absolute line.
  - Reduce the root baseline first (Q-0014 helps materially), then set the absolute line.
- **Status:** open
- **Answer (filled by user):**
- **Note (2026-06-21, quality-gates bundle T-0132):** the k6 load test (T-0132) does **NOT** close this. k6 measures **server-side API/SSR latency** (catalog/product TTFB, order-API), not **client-side JS bundle size** — orthogonal axes. The k6 thresholds bake the ADR 0023 §1 *latency* budgets; the *First-Load-JS bundle budget* this question raises is unaffected and stays a separate **frontend-perf concern, deferred**. Recorded in `deploy/load-tests/README.md` so a reviewer doesn't conflate "we ran load tests" with "we have a JS bundle budget" — we don't, and that's this Q's job (architect input still wanted).

## Q-0016 — Maker invoice PDF embeds customer email (GDPR-lock conflict)
- **From:** secops (order-dashboards-bundle Gate 3, F-1)
- **Ticket / context:** order-dashboards-bundle (T-0088 maker invoice download vs. T-0081/T-0082 GDPR lock)
- **Asked:** 2026-06-12
- **Blocking:** no — authenticated ownership-scoped counterparty, not an exploit; reconcile before launch
- **Question:** The maker host's invoice download (T-0088) streams the Customer-type invoice whose QuestPDF template (`QuestPdfInvoiceRenderer.cs:178,321`) embeds `RecipientEmail` — every paid order hands the maker the customer email through a sanctioned button, contradicting the T-0081/T-0082 compile-time GDPR lock the dashboards enforce at DOM level. Docs conflict internally: US-maker-0010 AC-1 still grants makers "name + email + phone".
- **Options the agent has considered:**
  - (a) Accept email-in-invoice as sanctioned commercial-document content (Czech invoicing customs include contact; reconcile US-maker-0010 + annotate the T-0081/T-0082 lock as DOM-scope-only).
  - (b) Render a maker-variant invoice copy with email redacted (new QuestPDF variant; invoice integrity concern — the maker's copy differs from the customer's legal document).
  - (c) Drop the maker invoice button until ruled (revenue-path UX loss).
  - Recommend (a) with explicit doc reconciliation — the invoice is a commercial document between the parties; the DOM-level lock guards casual scraping, not contractual documents.
- **Status:** answered
- **Answer (filled by user):** (a) accepted 2026-06-12 — invoice is a commercial document between contracting parties; Czech invoicing customs include contact details. DOM-level GDPR lock (T-0081/T-0082) guards casual scraping, not contractual documents. US-maker-0010 reconciled; locks annotated.

## Q-0017 — DEFECT: email subject placeholders never substitute (4 prior seed migrations)
- **From:** dotnet-backend (refund-dispute-bundle implementation)
- **Ticket / context:** refund-dispute bundle (T-0105 + T-0106 + T-0107); defect dates back to the T-0067-era seeds
- **Asked:** 2026-06-12
- **Blocking:** no — subjects render the literal `{order_number}`; cosmetic but customer-visible on every affected order email
- **Question:** The seed migrations build their SQL with `$@"` interpolated strings, so the source's `{{order_number}}` collapses to single-brace `{order_number}` in the stored `email_template_translations.subject`; `SubstitutePlainTextPlaceholders` only matches `{{key}}`, so subject substitution silently no-ops. Affected: **16 subject rows** (8 templates × cs/en; initially reported as 14 — QA grep verification 2026-06-12 counts 16) across `20260606155359_SeedOrderEmailTemplates` (4), `20260608120000_ShippingPipelineBundle` (4), `20260609075803_DeliveryCloseBundle` (2), `20260609174208_OrderCleanupBundle` (6). The new T-0105/T-0106 seeds escape correctly (quadruple-brace in source → `{{key}}` stored) and are unaffected. How should the broken rows be fixed?
- **Options the agent has considered:**
  - Fix-up migration `UPDATE`ing the affected subject rows from `{key}` to `{{key}}` (S, recommended — applied migrations are immutable; a data-fix migration is the sanctioned path).
  - Regenerate all email templates in a new consolidated seed.
  - Leave until the template-editor admin UI ships and fix the copy by hand there.
- **Status:** resolved
- **Answer (filled by user):** Option 1 — data-fix migration shipping as the leading commit of the payout-core bundle PR (2026-06-12).
- **Resolution:** `20260613060609_FixEmailSubjectPlaceholders` (leading commit of the payout-core bundle) UPDATEs the 16 affected `email_template_translations.subject` rows from `{order_number}` to `{{order_number}}`, idempotently (only rows with the single-brace token and not already double-brace). Integration-verified.

## Q-0018 — Comgate refId idempotency handle for refunds
- **From:** reviewer + optimizer (refund-dispute-bundle Gate 8 M-1 disposition)
- **Ticket / context:** refund-dispute bundle (T-0105 `RefundOrder`); forward note for T-0118 admin UI
- **Asked:** 2026-06-12
- **Blocking:** no — the single-attempt fold removes the auto-retry exposure; admin re-issue remains a manual double-submit risk until T-0118's confirm UI
- **Question:** When the T-0118 admin UI ships, should `RefundOrder` pass a deterministic `refId` to the Comgate `/v1.0/refund` call (e.g. `orderId` + cumulative-refunded snapshot) so gateway-side idempotency holds even across manual re-issues of the same refund?
- **Options the agent has considered:**
  - Deterministic refId now (needs verification that the Comgate refund API accepts/echoes a caller-supplied refId).
  - Confirm-UI only at T-0118 (process guard; no gateway-side guarantee).
  - Both — belt and braces for a money-moving path.
- **Status:** open
- **Answer (filled by user):**

## Q-0019 — Payout eligibility scan index degrades over time
- **From:** optimizer (payout-core Gate 8 NOTE-1)
- **Ticket / context:** payout-core bundle; weekly payout claim scan
- **Asked:** 2026-06-12
- **Blocking:** no
- **Question:** The weekly claim scan filters `State == Delivered AND PayoutBatchId IS NULL`; only `ix_orders_state` serves it. Claimed orders KEEP `state = Delivered` + a non-null batch id, so they never leave the seek set — by end of year 1 the scan walks the full Delivered history every week.
- **Options the agent has considered:**
  - Add a partial index now (cheap, recommended — pre-empts the cliff): `ix_orders_payout_unclaimed ON orders(country_code) WHERE state = 'Delivered' AND payout_batch_id IS NULL AND is_active`.
  - Add when volume warrants (defer until the Delivered history is large enough to measure the scan cost).
  - Combine with a future "archive completed orders" policy so claimed/settled orders leave the hot table entirely.
- **Status:** answered
- **Answer (filled by user):** Fixed in T-0125 (debt-codification bundle, 2026-06-15) — added partial composite index `ix_orders_payout_unclaimed ON orders(state, payout_batch_id) WHERE state='Delivered' AND payout_batch_id IS NULL AND is_active`, matching the eligibility-scan predicate exactly. `ix_orders_state` preserved.
- **Note:** S follow-up.

## Q-0020 — Year-boundary ISO-week payout batch number
- **From:** dotnet-backend (T-0102a risk note)
- **Ticket / context:** T-0102a — payout batch numbering `VYP-CZ-YYYY-Www`
- **Asked:** 2026-06-12
- **Blocking:** no
- **Question:** A Jan 1–3 batch falling in ISO week 52/53 of the prior year gets the new calendar year in `VYP-CZ-YYYY-Www` (cosmetic mismatch between the year and the ISO week); uniqueness still holds.
- **Options the agent has considered:**
  - Use the ISO-week-year (not the calendar year) in the number — strictly correct.
  - Leave it (cosmetic; uniqueness is unaffected).
  - Document the calendar-year convention so the discrepancy is intentional and on record.
- **Status:** open
- **Answer (filled by user):**
- **Note:** Recorded in the ADR 0009 amendment trail.

## Q-0021 — AdminAuditPipelineBehavior writes a no-op audit row on idempotent Silent-Success re-calls
- **From:** reviewer (payout-settlement T-0103 AC-3 conflict)
- **Ticket / context:** T-0103 (MarkPayoutBatchCompleted); payout-settlement bundle
- **Asked:** 2026-06-13
- **Blocking:** no
- **Question:** The shared `AdminAuditPipelineBehavior` writes an audit row on EVERY successful `IAdminAuditableCommand` regardless of whether state changed; so an idempotent re-call (`MarkPayoutBatchCompleted` on an already-`Completed` batch, `RefundOrder` re-refund, etc.) writes a benign no-op audit row. T-0103's AC-3 "no second audit row" is therefore unattainable without changing the shared pipeline. The bundle kept `IAdminAuditableCommand` (money attribution is mandatory) + asserts robust idempotency (no second outbox, state unchanged, first bank-ref authoritative) instead.
- **Options the agent has considered:**
  - (a) Accept the no-op audit rows as benign noise platform-wide (recommended — they record "admin attempted X", which is itself audit-worthy).
  - (b) Make the pipeline skip the audit write when before==after snapshot (touches Refund/Dispute/ChangeState/Payout — needs its own ticket + careful snapshot-timing handling; a prior naive attempt suppressed live-transition rows).
  - (c) Per-command opt-out flag.
- **Status:** answered
- **Answer (filled by user):** (a) accepted 2026-06-14 (architect) — the shared `AdminAuditPipelineBehavior` correctly writes an audit row on EVERY successful `IAdminAuditableCommand`, including idempotent no-op re-calls; a no-op row is itself an audit-worthy "admin attempted X" record. NO change to the pipeline. The unattainable "no second audit row" AC wording (T-0103 AC-3) is dropped platform-wide; idempotency ACs assert robust state-idempotency (no second outbox/transition) instead.
- **Note:** Architect to rule; affects the AC-3 wording on T-0103 retroactively. RULED 2026-06-14 — T-0103 AC-3's "no new audit row" clause softened in the ticket Status log; the assertion is now "no second outbox row, state unchanged, first bank-ref authoritative".

## Q-0022 — Admin surfaces carry no ADR 0023 §1 performance budget row
- **From:** optimizer (admin-ops bundle, Gate 8)
- **Ticket / context:** admin-ops bundle (T-0108/T-0109/T-0110/T-0111); ADR 0023 §1
- **Asked:** 2026-06-14
- **Blocking:** no
- **Question:** The 3 admin list/query surfaces (GetAllOrders, GetAllInvoices, audit-log) + the GDPR erasure have no defined p95 in ADR 0023 §1. Reviewed against CZ-only MVP scale (fine — low-frequency, ~2 admin users), but there is no budget to gate against, and two multi-country-latent index gaps were noted: (a) GetAllOrders / GetAllInvoices filter `country_code` with no leading index on it (seq-scan-friendly once multi-country lands); (b) the customer-email / recipient admin search is a leading-wildcard `ILIKE` → forced sequential scan. What budget + indexes should gate these?
- **Options the agent has considered:**
  - Add an admin §1 budget row to ADR 0023 + the `country_code`-leading composite indexes (orders/invoices) when multi-country lands.
  - Accept admin as explicitly best-effort (low-frequency, ~2 users) — no budget row; document the latency posture as intentional.
  - Add a `pg_trgm` GIN index for the email/recipient search if it scales past the seq-scan threshold.
- **Status:** open
- **Answer (filled by user):**
- **Note:** Non-blocking; architect/optimizer to set when multi-country is on the roadmap. CZ-only MVP scale is unaffected.

## Q-0023 — T-0124 provider-registry email-provider mismatch
- **From:** dotnet-backend (T-0108 impl) + reviewer-confirmed
- **Ticket / context:** admin-ops bundle (T-0108 `UpdateCountryConfiguration` + `IProviderRegistry`); forward note for T-0124
- **Asked:** 2026-06-14
- **Blocking:** no — latent, not a T-0108 defect
- **Question:** The CZ seed sets `default_email_provider = 'resend'`, but `IProviderRegistry`'s static email fallback (`ProviderRegistry.EmailCodes`) expects `'sendgrid'` (email isn't keyed-registered until T-0124). So an admin CHANGING the email provider today would be rejected as `country.providerNotRegistered`, and the current seed value isn't itself in the fallback set. No test exercises it (email isn't keyed until T-0124). How should the registry + seed be reconciled?
- **Options the agent has considered:**
  - At T-0124 (when `IEmailProvider` becomes keyed): replace the static `EmailCodes`/`RegistryCodes` fallbacks with the same keyed-container probe used for payment/shipping, and ensure the registered key(s) match the CZ seed (`'resend'`).
  - Interim: align the static `EmailCodes` fallback to `{ "resend" }` now so the seed value validates, deferring the keyed probe to T-0124.
  - Leave as-is until T-0124 (no admin changes the email provider before then; the field is effectively frozen at the seed value).
- **Status:** open
- **Answer (filled by user):**
- **Note:** T-0124 owner must reconcile `ProviderRegistry` fallback + the seed when email providers become keyed. Recorded on `roles/country-configuration.md` (provider-validation seam).

## Q-0024 — No admin order-detail read DTO (admin order detail renders only list-row + audit-log fields)
- **From:** BA (T-0118b grooming)
- **Ticket / context:** T-0118b admin order detail (`/dashboard/admin/orders/[orderId]`); US-admin-0009 AC-2
- **Asked:** 2026-06-15
- **Blocking:** no — T-0118b ships the richest detail the current contract allows (the T-0111 `AdminOrderListItemDto` fields + the order's audit trail via the audit-log query); the gap is bounded and labelled, not silent
- **Question:** T-0111 shipped only the admin cross-tenant *list* (`AdminOrderListItemDto`) and the *global* audit-log query — there is **no** admin order-detail read (line items, lifecycle timeline, payout/VAT breakdown, attachments, message thread). The T-0082 maker/customer detail DTOs are owner-scoped (loaded via `GetByIdForMakerAsync`/`GetByIdForCustomerAsync`) and not reusable on the admin host. Should a thin `GetAdminOrderDetail` query + DTO be added (composing over `IOrderRepository.GetByIdUnscopedAsync`, which already exists for T-0105/T-0107) so the admin detail page renders a full order, not a degraded list-row header?
- **Options the agent has considered:**
  - Add `GetAdminOrderDetail` (Query + `AdminOrderDetailDto` + admin endpoint) as a small backend follow-up ticket — composes over the existing unscoped lookup; the richest fit for US-admin-0009 AC-2.
  - Leave T-0118b on the bounded composition (list-row header + audit trail) at MVP — the audit trail is the load-bearing read; the header degrades gracefully; revisit if admin ops needs line items.
- **Status:** answered
- **Answer (filled by user):** Option a — groomed as **T-0127** (admin-read-gaps bundle, 2026-06-15). `GET /api/v1/admin-orders/{orderId}` → a privileged `AdminOrderDetailDto` (full header: number, state, all amounts/breakdown, country, maker id+name, `customerEmail`, contact snapshot, timestamps — no GDPR redaction, admin is privileged) composed over the existing `IOrderRepository.GetByIdUnscopedAsync`, AsNoTracking + Unscoped; 404 reuses `OrderNotFound` (no new code). T-0118b's order-detail header re-wires onto the real DTO (item 6). The per-user in-flight signal the delete-user screen needs is folded into the same bundle as a `customerUserId`/`makerId` filter on a thin admin-orders read (chosen over a `HasInFlightOrders` boolean — cleanest single seam), driving the T-0118c delete-user proactive pre-disable (item 7). Owner dotnet-backend; the FE re-wire ships in the same T-0127 PR after the admin NSwag regen.
- **Note:** Out of scope for the T-0118 frontend bundle (frontend slices are read-only consumers; no backend added). Closed by T-0127 (cross-stack); T-0118b's detail header upgrades to consume the new DTO with no UI restructure.

## Q-0025 — T-0118 INDEX `depends_on` omits T-0110 + T-0103 (slice-c dependencies)
- **From:** BA (T-0118b grooming — dependency-gap fix flagged in the split)
- **Ticket / context:** `docs/tickets/INDEX.md` T-0118 row + the T-0118a/b/c split
- **Asked:** 2026-06-15
- **Blocking:** no — documentation/dependency-graph correctness; does not block T-0118a or T-0118b
- **Question:** The INDEX `depends_on` for T-0118 lists `T-0102, T-0105, T-0106, T-0107, T-0108, T-0109, T-0111` but **OMITS T-0110 (GDPR DeleteUserPermanently — slice c) and T-0103 (MarkPayoutBatchCompleted — slice c payout "complete" action + BankReference capture)**. Both are consumed by T-0118c (ops + control-plane). The split's overall dependency set and T-0118c's `depends_on` must add T-0110 + T-0103.
- **Status:** resolved (documentation)
- **Answer (filled by user):** Flagged in the grooming commit. T-0118c (when written) must carry `depends_on: [T-0118a, T-0102, T-0103, T-0108, T-0109, T-0110]`; the INDEX T-0118 aggregate row gains T-0110 + T-0103 when PM splits the row into a/b/c. Recorded here so the gap is not lost between grooming and the slice-c ticket.

## Q-0026 — Admin invoice-PDF download endpoint absent (blocks US-admin-0012 AC-2)
- **From:** frontend + reviewer (T-0118a slice-a review)
- **Ticket / context:** T-0118a all-invoices view; US-admin-0012 AC-2 "Stáhnout fakturu"
- **Asked:** 2026-06-15
- **Blocking:** no — T-0118a ships the download button disabled-with-tooltip (no guessed path); the feature is degraded, not broken.
- **Question:** The admin host exposes the 3 read queries + the payout `csv(id)` bank-file stream, but NO invoice-PDF download method. T-0088 shipped invoice streaming on the customer + maker hosts (order-scoped + Fee-scoped), but not the admin host. US-admin-0012 AC-2 needs an admin-scoped invoice download (any invoice by id, `Unscoped()` — admin sees all). T-0118a's faktury "Stáhnout fakturu" button is disabled until it exists.
- **Options the agent has considered:**
  - Thin backend ticket: `GET /api/v1/admin-invoices/{invoiceId}/pdf` on Web.Admin, controller-direct streaming per T-0088, `IInvoiceRepository.Unscoped()` lookup (admin sees any invoice), ETag + private/no-store. ~S. Then re-enable the T-0118a button (one helper + remove the disabled state).
  - Defer until a sprint needs admin invoice download in production.
- **Status:** answered
- **Answer (filled by user):** Option a — groomed as **T-0126** (admin-reads-followups bundle, 2026-06-15). `GET /api/v1/admin-invoices/{invoiceId}/pdf` on Web.Admin, controller-direct streaming per the T-0088 precedent, Unscoped-by-invoice-id lookup (admin sees ANY invoice — new read-only `IInvoiceRepository.GetByIdUnscopedReadOnlyAsync` per ADR 0025), 404 reuses `InvoiceNotYetRendered` (no new code), `private, no-store` + ETag/304 + `faktura-{InvoiceNumber}.pdf` (T-0064/T-0088 PII policy). Backend-only; the T-0118a "Stáhnout fakturu" re-enable rides a tiny FE follow-up once T-0126 ships the endpoint + admin NSwag regen.

## Q-0027 — Admin overview KPI count reads absent (Processing payouts + stalled outbox)
- **From:** frontend (T-0118a overview)
- **Ticket / context:** T-0118a overview KPI tiles; US-admin-0002 AC-2 (outbox health banner)
- **Asked:** 2026-06-15
- **Blocking:** no — T-0118a renders the affected tiles as "—" + a forward link + an info banner (no fabricated 0); order-state counts work via existing `pageSize:1` totalCount probes.
- **Question:** The overview wants a pending-Processing-payouts count + a stalled-outbox-events count, but no read exposes either as a count (the payout + outbox reads live in slice c). The stalled-outbox red banner (US-admin-0002 AC-2) has no source signal in slice a. Should we add thin count endpoints, or let slice c's lists carry the counts?
- **Options the agent has considered:**
  - Thin count endpoints (`GET /api/v1/payout-batches/count?state=Processing`, `/outbox-events/stalled/count`) consumed by the overview. ~S backend.
  - Let slice c ship the payout + outbox LIST views; the overview deep-links to them and shows the count once those reads exist (no dedicated count endpoint; the list's totalCount serves the tile, same pattern as the order-state probes).
  - Defer the two tiles to slice c entirely (overview ships order-state counts only at PR 1).
- **Status:** answered
- **Answer (filled by user):** Option a — groomed as **T-0126** (admin-reads-followups bundle, 2026-06-15). Two thin admin-host count endpoints: `GET /api/v1/payout-batches/count?state=Processing` → `{ count }` (new `IPayoutBatchRepository.CountByStateAsync`, AsNoTracking/Unscoped) + `GET /api/v1/outbox-events/stalled/count` → `{ count }` (new `IOutboxConsumerRepository.CountStalledAsync` with the exact stalled-set predicate `ProcessedAt==null AND NextRetryAt==null AND LastErrorKind!=None`, matching T-0109's stalled set — acknowledged rows excluded by `ProcessedAt==null`). Globally-unique Response names; read-only (no new codes). The overview consumes these to replace the "—" tiles + drive the US-admin-0002 AC-2 stalled-outbox banner. Backend-only; the FE wire-up rides a tiny T-0118a follow-up once T-0126 ships + admin NSwag regen.

## Q-0028 — Admin invoice-PDF reads are not audited
- **From:** secops (T-0126 Gate 3, F1)
- **Ticket / context:** T-0126 admin invoice-PDF download (`GET /api/v1/admin-invoices/{id}/pdf`)
- **Asked:** 2026-06-15
- **Blocking:** no — the read is admin-audience-gated; the question is forensic-trail completeness, not access control.
- **Question:** The admin invoice-PDF download is controller-direct (not an `IAdminAuditableCommand`), so streaming a customer's invoice as admin leaves NO audit row — unlike T-0110 erasure (audited). Customer invoices carry PII (recipient name/address/line items). Should privileged admin reads of customer financial PII emit an audit row ("admin X downloaded invoice Y")?
- **Options the agent has considered:**
  - Audit all admin invoice-PDF reads (a read-side audit hook — the admin-audit pipeline is command-only today; a read needs a thin explicit AppendAsync in the controller, or a read-audit behavior). Strongest forensic trail; one extra write per download.
  - Audit nothing (status quo) — admin is a 2-person trusted role; access is gated; the invoice already exists as a legal record.
  - Audit only on a future "admin accessed customer PII" policy bucket (broader than invoices — would also cover the admin order list showing customerEmail).
- **Status:** ANSWERED 2026-06-21 → **T-0137** (`feat/secops-hardening-bundle`).
- **Answer (filled by user):** Audit the high-signal privileged PII READS — invoice-PDF download (`invoice.pdf.download`), payout CSV download (`payout.csv.download`), and single order-detail view (`order.detail.view`, full contact snapshot) — via a dedicated `IAdminReadAuditWriter` that owns its own DbContext (the T-0032 `IDbContextFactory` precedent) so a read never opens the request UoW. SKIP the paginated list reads (high-volume page-loads, low forensic value). Reuses `admin_audit_log` (no migration); `beforeJson=afterJson=null` for reads.

## Q-0029 — Admin read-side gaps for the dashboard ops/control-plane surfaces
- **From:** frontend + reviewer + architect (T-0118b/c final review, Gate 4)
- **Ticket / context:** T-0118c ops surfaces (country-config, payout, outbox, delete-user)
- **Asked:** 2026-06-15
- **Blocking:** no — every surface ships degraded-but-functional; the backend gates are authoritative; the country-config full-replace hazard is fenced by a prominent warning banner (folded into T-0118c) until the GET lands.
- **Question:** Four admin read endpoints don't exist, forcing degraded T-0118c surfaces. Which to build (each a thin S backend read on the admin host)?
- **Options the agent has considered:**
  - **GetCountryConfiguration GET** (highest priority — the architect's BLOCKER fence): `GET /api/v1/country-configurations/{code}` returning the current config so the edit form PRE-FILLS instead of starting blank. Removes the full-replace silent-overwrite hazard + lets VAT/fee-only edits skip the provider retype modal (T-0118c AC-4/AC-5 are currently unmet, fenced by a warning banner). Then re-enable form pre-fill.
  - **GetAdminOrderDetail** (Q-0024, already logged): a real admin order-detail DTO so T-0118b's header isn't a list-row scan + the delete-user in-flight block can pre-disable proactively (per-user-order read).
  - **Admin stalled-outbox LIST read**: so the outbox triage page browses stalled events instead of count + by-id (the operator currently needs event ids from App Insights). The architect rated this the weakest accept — ship FIRST among the list reads.
  - **Admin payout-batch LIST read**: so the payout page browses Processing batches instead of count + by-id (lower priority — one batch/week, T-0116 maker list covers visibility).
- **Status:** answered
- **Answer (filled by user):** All four — groomed as **T-0127** (admin-read-gaps bundle, 2026-06-15), one cross-stack PR. **(1 PRIORITY) GetCountryConfiguration GET** `GET /api/v1/country-configurations/{code}` returns the **exact** `UpdateCountryConfiguration` Response field set (`StandardVatRateBp, ReducedVatRateBp, InvoicingMode, PlatformFeeRateBp, DefaultShippingPriceMinor, DefaultPaymentProvider, DefaultShippingCarrier, DefaultRegistry, DefaultEmailProvider`) via `ICountryConfigurationRepository.GetByCodeAsync`; 404 reuses `CountryConfigurationNotFound` (no new code) — **removes the PR-2 full-replace fence**: the T-0118c form pre-fills SSR, the warning banner downgrades to an info note, and the provider retype modal gates on an **actual provider-code diff** (T-0118c AC-4/AC-5 now met). **(2) GetAdminOrderDetail** `GET /api/v1/admin-orders/{orderId}` → privileged `AdminOrderDetailDto` (see Q-0024) over `GetByIdUnscopedAsync`; plus a `customerUserId`/`makerId` filter on the admin-orders read = the per-user in-flight signal driving the delete-user proactive pre-disable. **(3) Stalled-outbox LIST** `GET /api/v1/outbox-events/stalled` (paged) reusing the **exact** T-0126/T-0109 predicate `ProcessedAt==null && NextRetryAt==null && LastErrorKind!=None`. **(4) Payout-batch LIST** `GET /api/v1/payout-batches` (paged, Unscoped — the GET on the existing CreatePayoutBatch POST route). All four mirror the T-0111 `IAdminQueries` precedent (AsNoTracking, Unscoped, globally-unique Response, `[Authorize]` admin); the form/order-detail/delete-user/outbox/payout surfaces re-wire in the same PR. NSwag regen admin host (4 methods); zero new codes / migrations / unique indexes.

## Q-0030 — Approved legal text for /vop (obchodní podmínky) + /gdpr (privacy/cookie)
- **From:** BA/PM
- **Ticket / context:** T-0130 (static public pages, public-polish bundle); BLOCKING pre-launch
- **Asked:** 2026-06-20
- **Blocking:** yes-for-launch-not-for-T-0130-merge — pre-launch. The page SCAFFOLDING (route, nav, i18n keys, visible placeholder banner) ships in T-0130; only the legal TEXT is missing. Go-live must replace the placeholder banner + populate the keys. T-0130 merges without it.
- **Question:** JVM YORE s.r.o. must supply the approved legal text for the two legal pages: (1) **VOP / obchodní podmínky** (terms of service for the marketplace — customer + maker obligations, escrow/payment terms, commission, shipping, returns/complaints per Czech consumer law), and (2) **GDPR / ochrana osobních údajů** (privacy policy + cookie disclosure — what personal data is collected, lawful basis, processors: Comgate/Zásilkovna/Resend/ARES, retention, data-subject rights). The agent will NOT draft legal text — it is not binding and risks publishing wrong obligations. When will the approved text be available, and who supplies it (in-house / external counsel)?
- **Options the agent has considered:**
  - **Ship placeholder shells now (T-0130), block the legal TEXT on this Q (default — user-locked 2026-06-20).** `/vop` + `/gdpr` render a working page with a visible "PLACEHOLDER — awaiting approved legal text (JVM YORE s.r.o.)" `Alert` banner; the `static.terms.*` / `static.privacy.*` i18n keys are wired empty for a drop-in replacement. Logged in `docs/launch-checklist.md` as a blocking line.
  - Omit the routes until text exists — rejected (T-0131 sitemap + footer nav link these URLs; would yield 404s + a sitemap pointing at missing pages).
  - Agent drafts best-effort text — rejected (legal liability; not approved/binding).
- **Sub-question (flag):** does launch also require a **cookie-consent banner / cookie-management UI** (separate from the GDPR page copy)? If yes, fold it into the legal-text deliverable or groom a distinct ticket.
- **Status:** open
- **Answer (filled by user):**

## Q-0031 — Frontend has no test harness (axe-CI + SEO unit tests blocked)
- **From:** reviewer + qa (public-polish-bundle final review)
- **Ticket / context:** standing frontend gap; surfaced by T-0131 (SEO unit tests) + T-0133 (axe-core)
- **Asked:** 2026-06-21
- **Blocking:** no — frontend slices have shipped throughout via tsc + lint + next build + manual QA plans; no domain logic lives in the frontend.
- **Question:** The frontend (Next.js) has NO test framework — no vitest/jest, no `test` script, zero `*.test.ts`. T-0131 specced 6 SEO unit tests (e.g. `canonicalUrl` is an unpinned pure predicate) and T-0133 needs axe-core wired into a test/CI step. Both need a harness first. Stand one up (vitest + @testing-library/react + axe-core), or keep relying on tsc/lint/build/manual-QA for the frontend?
- **Options the agent has considered:**
  - Stand up vitest now (own infra ticket) — unblocks T-0131 SEO unit tests + T-0133 axe-core-in-CI + pins pure FE predicates (canonicalUrl, resolveErrorMessage, the debounce/poller shapes). The right pre-launch move if FE pure-logic coverage matters.
  - Keep the status quo (tsc + eslint + next build + manual QA plans) — the FE has no domain logic; the backend carries the test weight. Defer a harness to post-launch.
  - Minimal: axe-core via a standalone CI script (Playwright-free) for T-0133 only, no general unit harness.
- **Status:** answered
- **Answer (filled by user):** Stand up the harness (option 1) — **vitest + @testing-library/react + jest-axe** as THE frontend harness (the real harness, not a standalone axe-only script), user-locked 2026-06-21 in the **quality-gates bundle** (`feat/quality-gates-bundle`, T-0132 k6 + T-0133 a11y, Bundle B = one PR). The harness lands in T-0133: devDeps in `frontend/package.json` + a real lockfile update + `vitest.config.ts` (excludes the generated `src/lib/api-client/**`) + a `"test"` script; axe-core component/page tests assert zero WCAG 2.1 AA violations (ADR 0023 §5) on the critical customer paths (catalog/product/checkout/static), wired into CI (`.github/workflows/ci.yml` frontend test step) so a11y regressions fail CI. **Retroactive unblock:** the same harness pins the T-0131 SEO predicate tests (`canonicalUrl` etc.) that were blocked for lack of a harness — T-0133 writes those too. The manual NVDA/Firefox Czech screen-reader + keyboard pass is a `manual_step` (human + assistive tech; pre-launch), checklist at `docs/test-plans/a11y-manual-checklist.md`.

## Q-0032 — og:type=product unsupported by Next 16 Metadata type union
- **From:** frontend (T-0131 impl) + reviewer
- **Ticket / context:** T-0131 product-page OG metadata; AC-8
- **Asked:** 2026-06-21
- **Blocking:** no — the product page ships a valid `og:type=website` card; T-0131 AC-8's literal `og:type=product` is the only AC not met, type-system-forced.
- **Question:** Next 16's typed `OpenGraph.type` union excludes `'product'` (only website/article/profile/...). The product page emits `type:'website'` (clean, valid, no duplicate tag) rather than a raw `<meta property="og:type" content="product">` passthrough (which would duplicate the framework's tag). Accept `website` for MVP, or add a raw-meta `og:type=product` (+ `product:price:*` tags) for richer commerce cards?
- **Options the agent has considered:**
  - Accept `website` for MVP (recommended) — valid card, no SEO harm; richer product OG is a post-launch enhancement.
  - Raw-meta passthrough for `og:type=product` + `product:price:amount`/`currency` — richer Google/FB commerce cards, but bypasses the typed API + risks a duplicate og:type tag; needs care.
- **Status:** open

## Q-0033 — Custom observability metrics registered but not emitted (outbox/payment/webhook/auto-deliver)
- **From:** reviewer + secops (T-0134 ops-runbooks review, B-2)
- **Ticket / context:** T-0014 observability + the ADR 0023 §4 alert table; surfaced writing monitoring.md
- **Asked:** 2026-06-21
- **Blocking:** no — the outbox-stall alert (the highest-value one) has a working DB-backed signal today (GET /outbox-events/stalled/count + the admin UI, T-0126/T-0118c); the monitoring runbook leads with it. But the ADR 0023 §4 metric-based alert rules (outbox_lag_seconds, payment_create_failures_total, webhook_received_total, auto_deliver_count) will read empty until emission ships.
- **Question:** The MakablesMeters meter NAMES are registered (T-0014) but only the makables.payouts.* instruments actually record values. The ADR 0023 §4 alert table (outbox lag/stalled, payment failures, webhook received, auto-deliver) assumes these emit. Add the metric emission (Counter/Gauge.Add/Record calls in the outbox dispatcher, payment provider, webhook controllers, auto-deliver Function) so the Azure Monitor alert rules have signal — pre-launch, or accept the DB-endpoint + log-query alternatives the runbook documents?
- **Options the agent has considered:**
  - Wire the emission pre-launch (a small instrumentation pass across the dispatcher/providers/webhooks/Function) so all ADR 0023 §4 alerts work as specified. ~M.
  - Accept the documented alternatives at MVP (the runbook leads with the DB endpoint + ProcessOutboxTimer tick log for outbox; 5xx/DB-CPU come from ASP.NET/Azure Monitor built-ins which DO work; only the custom-metric alerts degrade) — defer emission to v1.1.
  - Partial: emit only the highest-value outbox + payment-failure metrics now, defer the rest.
- **Status:** open
