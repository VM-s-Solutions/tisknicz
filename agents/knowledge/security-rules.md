# Security Rules (S1–S10) — Non-Negotiable

> These rules exist because a marketplace that moves money with near-zero manual intervention cannot
> afford a security regression between weekly admin checkpoints. Treat them as **laws, not
> guidelines.** When rules conflict, the priority is:
> **security > correctness > cleanliness > consistency.** Never trade a security rule for shorter
> code.

This doc is the **how-we-build security discipline** that `secops` audits and that `dotnet-backend` /
`dotnet-db` / `frontend` self-check against before hand-off. It **complements** the canonical pattern
catalog — it does not restate it. For the mechanics (`BusinessResult`, `IUserSessionProvider`, MediatR
pipeline behaviors, `Auditable`, `CountryConfiguration`, per-audience hosts, idempotent webhooks) read
[`docs/architecture/patterns.md`](../../docs/architecture/patterns.md) and the ADRs it cites
([0005](../../docs/adr/0005-route-groups-and-audience-separation.md) audience separation,
[0012](../../docs/adr/0012-authentication.md) auth,
[0013](../../docs/adr/0013-data-scoping-and-soft-delete.md) scoping/soft-delete,
[0014](../../docs/adr/0014-admin-audit-log.md) admin audit,
[0016](../../docs/adr/0016-payments-comgate.md) Comgate). This file names the **risk classes** and the
**verified reference call-sites** — the pattern doc names the shapes.

The `secops` agent audits every `security_touching` ticket (front-matter boolean, DoR item 6 in
[ticket-lifecycle.md](../../docs/process/ticket-lifecycle.md)) against this list and names the
**specific** risk when something fails, per **Gate 0 / Gate 3** in
[quality-gates.md](../../docs/process/quality-gates.md). A theoretical risk that a guard already blocks
is REFUTED — say so and move on. `dotnet-backend` self-checks against it before handing off.

---

## S1 — Caller identity is server-truth, not client input

Never trust `userId`, `makerId`, `role`, `email`, or `countryCode` from the request body or query
string. Derive the caller from the validated JWT — in the controller, then enrich the command:

```csharp
var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
var enriched = command with { CustomerUserId = userId };
var result = await Mediator.Send(enriched, ct);
```

Handler / service code injects `IUserSessionProvider` and calls `GetUserId()` (see `CreateOrder.Handler`
in `patterns.md §A.9` — `customerUserId` only ever comes from the session provider; that is the IDOR
shield). If a `Command` record carries an identity field it must: default to `""` (NSwag generates
strict required fields; the frontend sends empty, the backend overwrites), be commented as
server-enriched, and be set by the controller from the JWT **before** `Mediator.Send`. `[AllowAnonymous]`
endpoints should need no identity field at all. `role` and `aud` are **never** read from the body —
they come from the token the per-host middleware already validated (ADR-0012).

## S2 — Authorization on every endpoint

Every controller action has exactly one of:
- a **named policy** attribute (`[Authorize(Policy = ...)]`) — the default expectation for anything
  role- or audience-specific, or
- `[AllowAnonymous]` — only for genuinely public routes (catalog, product detail, register, magic-link
  request, password-reset request, public order-lookup-by-confirmation-code, webhooks, ARES proxy), or
- bare `[Authorize]` (no policy) — only for "any authenticated caller of this host" routes
  (e.g. `GetMyProfile`).

A new endpoint with **none** of these is a hole. **`Web.Public` is the trap**: it accepts *any* of the
three audiences (`customer|maker|admin`), so a protected endpoint on Public MUST mount a named policy
that checks `role`/`aud` explicitly — **bare `[Authorize]` on Public is not enough** (ADR-0012 §JWT
structure). On `Web.Customer` / `Web.Maker` / `Web.Admin` the host's accepted-audience table
(`MakablesAuthExtensions.AcceptedAudiencesFor`, pinned by `JwtAuthMiddlewareTests`, T-0027) already
narrows the token, but a missing policy still lets any authenticated caller of that host hit the route.

**Accountability (ADR-0014).** Every admin **write** (a command invoked from a `Web.Admin` controller)
must implement `IAdminAuditableCommand` / `IAdminAuditableCommand<TResponse>`, so `AdminAuditPipelineBehavior`
writes an append-only `admin_audit_log` row **inside the same `UnitOfWorkPipelineBehavior` commit** — you
write no audit code; the command's properties (`ActionCode`, `TargetEntity`, `TargetId`, `Notes`) supply
the metadata. An admin write with **no** row, a behavior that **computes** a diff (it must store the full
serialized `before_json`/`after_json` snapshot, not a diff), a snapshot carrying **raw** `PasswordHash` /
`TokenHash` (the `AuditSerializer` redact list must cover every new sensitive field), or a non-atomic /
best-effort *success* audit are ADR-0014 violations — the success row rides the action's commit. The
narrow **read-side** carve-out (T-0137: `invoice.pdf.download`, `payout.csv.download`, `order.detail.view`)
uses the separate `IAdminReadAuditWriter` (its own `IDbContextFactory` context, self-committing) and is
**fail-closed**: `await`ed BEFORE the PII streams, NOT in a swallowing try/catch — an audit-DB failure
faults the request (500) and no PII is disclosed. Never soften that to fire-and-forget.

## S3 — Resource-by-id endpoints must check ownership

Anything that takes a resource id and operates on it must verify the caller owns the resource —
**in the handler or domain service**, not the controller (so it holds regardless of which of the four
hosts exposes it). Prefer the **scoped repository method** from ADR-0013 so the scope is visible in code:

```csharp
var order = await orderRepo.ForCustomer(cmd.CustomerUserId).FirstOrDefaultAsync(o => o.Id == cmd.OrderId, ct);
if (order is null)
    return BusinessResult.Failure(Error.NotFound("orderId", BusinessErrorMessage.OrderNotFound)); // NotFound, not Forbidden — don't leak existence
```

Repositories expose `ForCustomer(userId)` / `ForMaker(makerId)` / `Unscoped()`; `Unscoped()` is callable
**only from `Web.Admin` controllers** (Reviewer-enforced, no runtime guard). Project convention: return
**NotFound** for cross-owner access so we don't confirm a resource exists to someone not allowed to see
it. For `[AllowAnonymous]` endpoints there is **no** identity claim, so a scoped method has nothing to
scope by — anonymous routes must not return owner-scoped data unless gated by a different shared secret
(a confirmation code or unguessable token in the URL).

## S4 — DTO leak prevention

**Never return an entity from a handler — always map to a `record` Response DTO.** Even if every field
is safe today, the entity gains a sensitive field tomorrow. Audit every Response/DTO for fields that must
not reach the client:
- `UserId`/`MakerId` of **other** parties (the caller may know their own id)
- `PasswordHash`, `RefreshToken.TokenHash`, magic-link / reset / confirmation token hashes
- Comgate / Packeta `provider_ref`, transaction ids, payout bank-transfer detail
- email / phone / full name / address of non-self parties (documented exceptions only, e.g. a maker's
  shipping label needs the customer's delivery address — that is intent, written down)
- soft-deleted rows leaking through a query that dropped the `IsActive` filter (see S10)

## S5 — Rate limiting on auth + side-effecting endpoints

Auth endpoints (login, register, magic-link request, magic-link consume, password-reset request/confirm,
email-confirm/resend, refresh) use the shared partitioned `"auth"` window via `[EnableRateLimiting("auth")]`.
Mutations that cost money or send email (create-order, request-refund, send-invoice, magic-link) get a
narrower per-caller limit. Decide the limit whenever you add a side-effecting mutation. Additional hard
limits from ADR-0012: **3 magic-link requests per email / 10 min**; **5 failed password attempts → 15-min
lockout keyed on `EmailNormalized`** (ghost-lockout even for non-existent emails, to defeat enumeration).
The public **ARES proxy is per-IP throttled (10/min)** — it is the one anonymous outbound-integration surface.

**Windows MUST be partitioned AND cardinality-bounded.** A named limiter with **no** partition key is one
global bucket shared by all callers — that is an S5 *violation*, not compliance (one client can DoS-lock
every other caller, and it does not throttle brute-force per attacker). The shared policies partition
**per real client IP** for anonymous requests and **per JWT `sub`** for authenticated ones, with
`UseForwardedHeaders` (narrow trusted-proxy `KnownNetworks` only) at the top of the pipeline and
`UseRateLimiter` **after** `UseAuthentication`. Anonymous per-IP partitions sit **behind a global
cardinality cap** so a botnet of distinct real IPs cannot trade the rate-DoS for a memory-DoS. Reuse this
shape for any new per-caller side-effect window — do not hand-roll an un-partitioned
`AddFixedWindowLimiter`, and do not ship an unbounded per-IP partition.

**Partitioning is not coverage.** A correctly partitioned policy applied to *some* endpoints does not
satisfy S5 for the money/side-effect endpoints that carry **no** `[EnableRateLimiting]` at all — those
remain S5 gaps. When you find one, name the exact controller + action (Gate 0), don't hand-wave "rate
limiting is missing."

## S6 — Logging hygiene (no PII above Debug)

No email, phone, name, address, Comgate/payout/bank detail, JWT, refresh/magic-link/reset token, or
confirmation code in logs at Information level or higher. Log `userId`, not `user.Email`. `ILogger<T>` is
the only logger — **no `Console.WriteLine`**. `LogDebug` is acceptable for PII during local investigation
only. This mirrors ADR-0012's audit item ("raw refresh token never logged") and ADR-0014's redact list.

## S7 — Idempotency on side-effecting commands

Any command that creates a Comgate charge, books a Packeta shipment, sends an email (Resend), grants
loyalty/referral credit, or writes a financial record (invoice, receipt, payout) **must be idempotent** —
check whether the side effect already happened (ledger entry / `provider_ref` / transaction id exists)
before doing it again. This protects against webhook re-delivery (Comgate/Packeta retry on 5xx/socket
reset), pipeline retries, double-clicks, and admin re-triggers. Webhook handlers follow the
`patterns.md §Idempotent webhooks` shape: verify origin (Comgate source-IP allowlist / Packeta signature),
**re-fetch status from the provider** before acting, look up by `provider_ref`, return 200 if already in
the target state, transition state in a single transaction.

**S7a — A check-then-act read is NOT atomic; under concurrency the DB must be the source of truth.**
A `if (await CountAsync(...) < cap)` / `if (await GetActiveAsync(...) == null)` guard followed by an insert
is a TOCTOU race: two concurrent requests both pass the read, both write, and the cap/uniqueness is
breached. The read is a fast-path optimization, not the guarantee. Enforce the invariant with one of:
- an **atomic conditional UPDATE** that returns rows-affected — `ExecuteUpdateAsync(... WHERE counter < max)`;
  **0 rows = limit reached** (no exception). Use this for promo/redemption caps and any counter that must
  not overshoot.
- a **unique index that you convert into a clean result, never an unhandled throw.** When a
  unique-violation can race (`RefreshToken.TokenHash`, `User.EmailNormalized`, `User.GoogleSub`, a
  payment `provider_ref`), catch the `DbUpdateException` (Postgres `SqlState == "23505"`) at the boundary
  that owns the write and resolve to the existing row / return the deterministic `BusinessErrorMessage`
  code — do **not** let it surface as a 500.

**S7b — Mind WHERE the violation surfaces vs. WHERE you catch it.** With `UnitOfWorkPipelineBehavior`,
the commit runs AFTER the handler returns — so a `DbUpdateException` from a tracked insert surfaces at the
*pipeline*, not in the handler, and a `try/catch` around the handler body won't catch it. If you need to
map the violation, **flush the insert in the handler** (its own `SaveChangesAsync` inside
`catch (DbUpdateException) when (IsUniqueViolation)`) so it's caught where you can resolve it; the
pipeline's final commit is then a safe no-op (the row is `Unchanged`). And never put a throwing
unique-insert inside a *larger* transaction whose rollback would be worse than the bug — e.g. a promo
redemption inside a paid-order `CreateOrder` txn should use the non-throwing conditional-UPDATE path so a
race can never roll back the paid order.

**Idempotency keys must be client-stable, not `Guid.NewGuid()` per call.** A fresh GUID per request
defeats the provider's idempotency (Comgate/Resend replay only on the *same* key). Derive the key from a
stable client-supplied token (one per logical attempt, new for a genuine retry-of-intent) with a
deterministic server-side fallback.

## S8 — Ownership & audience isolation (Makables has no RLS)

Post-pivot the .NET backend is the **only** writer and the DB is private, so there is **no Postgres RLS
and no `ITenantEntity`** (ADR-0013 chose application-layer scoping instead). The three defense layers are:

1. **JWT `aud` + `role`**, validated by per-host middleware before the request reaches a controller — a
   `customer` token cannot be replayed against `Web.Admin`; `Web.Public` accepts all three and therefore
   must gate protected routes with an explicit policy (S2).
2. **`[Authorize(Policy=...)]`** on the action.
3. **Repository scoping** — `ForCustomer(userId)` / `ForMaker(makerId)` / `Unscoped()` — where the method
   name reveals intent at every call site; `Unscoped()` is `Web.Admin`-only.

When adding an entity holding owner-scoped data, ask "could two customers / two makers both have rows
here?" — if yes, it is served through a scoped repository method, never a generic "give me everything"
call from a controller. Unique constraints on owner-scoped tables are scoped to the owner where the
business rule is per-owner (e.g. a maker's SKU is unique *per maker*), not globally.

**Anonymous-write / authenticated-read asymmetry (the silent-zero-rows trap).** The one global filter that
*does* exist is soft-delete on `IsActive` (S10). A row written on an **anonymous** path but read/updated
on a scoped path can silently match **zero rows** if the read is over-scoped — the side effect (confirm an
order by code, revoke a refresh token) never happens. The fix on the read side: an **explicit
caller-scoped predicate** that re-pins the surface by an unguessable secret (`TokenHash`, confirmation
code) or the caller's own `UserId` from the JWT — never a blanket "read everything." References: the
refresh-token revoke/rotate + reuse-detection reads (`RefreshToken` family, ADR-0012) and the
order-webhook existence check keyed by `provider_ref`. If you ever call `.IgnoreQueryFilters()` to reach a
soft-deleted row, pair it with an explicit predicate (see S10) — never just clear the filter.

## S9 — Migration & DTO-contract safety (NSwag is the contract)

- Add **nullable** columns freely. **Non-nullable** columns need a default or a backfill.
- **Never** rename a column in one migration — add new, deploy, dual-write, backfill, switch reads, drop
  old. Same for renaming a DTO field.
- **Dropping** a column: only after confirming no code **and no NSwag-generated client** references it —
  a stale generated DTO throws on deserialization.
- A DTO change is breaking unless: added fields are defaulted/nullable, removed fields were deprecated a
  release first, renamed fields expose both shapes for a release.
- **Any backend contract change regenerates `frontend/src/lib/api-client/` in the same PR** — CI (Gate 6)
  fails the PR if the generated client drifts from `openapi/v1.json`, and the client is never hand-edited
  (Gate 9 mechanical check). Schema + contract changes are flagged as `manual_steps` (`ef-migration`,
  `nswag-regen`) in the ticket (DoR item 5) — owner-only.

## S10 — Soft-delete / `IsActive` semantics (inverted from a "remember to filter" model)

`Auditable.IsActive` is the soft-delete flag and there **is** a global EF Core query filter on it
(ADR-0013) — deactivated rows are excluded from every `Set<T>()` read **automatically**. So the Makables
risk is **not** "you forgot to filter" — it is the opposite:

- **`.IgnoreQueryFilters()` misuse.** Every call site must have a comment justifying why it needs to see
  deactivated rows (admin audit trail, GDPR reconciliation). An unexplained `IgnoreQueryFilters()` is an
  S10 finding. When you do use it, pair it with an explicit predicate so you don't widen the read (S8).
- **The filter's blind spots.** The global filter applies to `Set<T>()` reads but **not** to
  `FromSqlRaw`/`ExecuteSqlRaw`, an `IQueryable` handed out of the wrong layer, or a join where only one
  side carries the filter — audit those paths.
- **Hard delete is a single audited path.** `Remove()` on user data is blessed only inside
  `DeleteUserPermanently` → `IUserDataDeletionService` (GDPR), which anonymizes retained orders/invoices
  and writes an `admin_audit_log` row. A raw `DELETE` anywhere else is a code smell.
- **The pause/resume collision.** On some entities (recurring templates / a maker's temporarily-hidden
  listing) `IsActive` may double as a user pause flag rather than soft-delete — don't conflate them; if a
  true soft-delete is ever needed there, add a separate column.

---

## Audit checklist for an existing endpoint

1. Named policy or `[AllowAnonymous]` present — and on `Web.Public`, a protected route has an **explicit**
   policy, not bare `[Authorize]` (S2)
2. Caller identity enriched from JWT; `userId`/`makerId`/`role`/`aud` never read from the body (S1)
3. Ownership checked for resource-by-id paths via a scoped repository method (S3)
4. Response is a `record` DTO with no leaked fields (S4)
5. No `IgnoreQueryFilters()` without a justifying comment + explicit predicate (S8 / S10)
6. `CancellationToken` propagated end-to-end
7. Rate-limited if it is an auth or external-side-effect endpoint, with a partitioned + bounded window (S5)
8. Idempotent if it has a doublable side effect; webhooks re-fetch provider status before acting (S7)
9. Soft-delete `IsActive` filter relied on (not bypassed) where deactivation matters (S10)
10. No PII in logs above Debug; `ILogger<T>` only (S6)
11. Admin-host write implements `IAdminAuditableCommand`; single-record admin PII read calls
    `IAdminReadAuditWriter` fail-closed (S2 / ADR-0014)
