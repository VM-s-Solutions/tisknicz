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
