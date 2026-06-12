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
- **Status:** open
- **Answer (filled by user):**

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
- **Status:** open
- **Answer (filled by user):**

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
