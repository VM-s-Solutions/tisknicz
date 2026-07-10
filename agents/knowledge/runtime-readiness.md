# Runtime Readiness — Observability & Outage Safety

Code that's shaped correctly can still fall over in production. This catalog covers what happens at
**runtime**: can you see what the system is doing, and does it **degrade gracefully** when a
dependency is down? Checked before a feature is `done` — this is the runtime half of
[**Gate 5**](../process/quality-gates.md) (Performance, cost & runtime readiness), which fires for
anything touching an external service, an Azure Function / queue, or a hot path. The bar: a
**self-running marketplace that requires minimal manual intervention.**

This doc is process/how-we-build guidance. It **complements**, and does not restate, the canonical
pattern catalog — for the concrete shapes (idempotent webhooks, provider adapters, the pipeline,
error envelope) see [`docs/architecture/patterns.md`](../../docs/architecture/patterns.md), especially
**§A.20 (idempotent webhooks + UoW)** and the `IPaymentProvider` / `IShippingProvider` adapter roles.

The real infra (from [`docs/architecture/`](../../docs/architecture/) + [ADR 0008](../../docs/adr/0008-dotnet-dependency-injection.md)):
four .NET API hosts — `Web.Customer` (5001), `Web.Maker` (5002), `Web.Admin` (5003),
`Web.Public` (5104) — plus **Azure Functions** (invoice generation, weekly payout batch, fiscal
retry), PostgreSQL 16, Azure Blob/Queue Storage, and the provider adapters: **Comgate** (payments),
**Packeta** (shipping), **ARES** (registry), **SendGrid** (email), **Mapbox** (geocoding).
`AddServiceDefaults()` (Aspire) wires **OpenTelemetry** traces/metrics and health checks for free;
**Application Insights** is the production sink. There's already a request-logging middleware and a
global exception handler that emits the RFC-7807 `Error` envelope carrying a `correlationId`.

---

## Observability

- **Structured logging, not string-concatenation.** Log with named properties
  (`logger.LogInformation("Order {OrderId} cancelled by {ActorRole}", id, role)`), never interpolated
  blobs — and never `Console.WriteLine`; inject `ILogger<T>` (a CLAUDE.md hard rule). **No PII above
  Debug** — log `userId` / `makerId`, never email, phone, address, or `IČO`. (This is the runtime face
  of the SecOps no-PII rule — see [`.claude/agents/secops.md`](../../.claude/agents/secops.md) and the
  security rows in `docs/architecture/patterns.md`.)
- **Correlation id on every request.** The request/trace id flows through the request-logging
  middleware into the `Error` envelope (`correlationId`), into logs, into **queue messages**, and into
  the **Function** that processes them — so a customer action and its async side effects (invoice,
  receipt email, payout) can be stitched together. When you enqueue a message, carry the correlation
  id; when a Function picks it up, log with it. OpenTelemetry propagates the trace context; don't drop
  it at the queue boundary.
- **Errors reach Application Insights with context** — country code, user/maker id (not PII),
  correlation id, the operation, the `BusinessErrorMessage` code. A swallowed exception with no
  telemetry is invisible in PROD.
- **Every external call is logged at its boundary** — inside `Infra.Clients/<Provider>/`, never
  scattered — with outcome + duration + error classification
  (`Transient | Permanent | Configuration | Unknown`), so a Comgate/Packeta/ARES/SendGrid/Mapbox
  slowdown is visible before it becomes an incident. (Reminder: no `HttpClient` lives outside
  `Infra.Clients`.)

## Health & readiness

- **Each of the four API hosts exposes a health check** (from `AddServiceDefaults()`) that verifies it
  can reach its critical dependencies — DB, and the queue/blob it needs — so Azure routes traffic only
  to healthy instances. `Web.Public` additionally must stay healthy for **webhook** and **cron**
  ingress; if it's down, Comgate/Packeta callbacks are lost, so its readiness matters most.
- **The migration-runner ordering is a readiness concern** ([ADR 0023](../../docs/adr/0023-non-functional-requirements.md)):
  `Web.Customer` applies EF Core migrations at startup; the other three hosts wait on a startup
  readiness check. A new critical dependency must be reflected in the right host's health check.
- **Functions are observable** — each background job (`GenerateInvoice`, `RunWeeklyPayoutBatch`,
  fiscal-retry) logs start/finish/outcome and emits a metric or log the owner can alert on
  (e.g. "payout batch processed N, failed M").

## Graceful degradation (the dependency-down matrix)

The guiding rule: **a customer's core action must never be blocked by a non-core dependency being
down.** Each external dependency has a defined failure behavior. The maker-facing side follows the same
rule — publishing a product must not hard-fail because ARES is slow.

| Dependency down | Must NOT happen | Correct behavior |
|---|---|---|
| **Comgate** (payments) | Order creation hard-crashes; customer sees a 500 | Classify the error (`Transient` vs `Permanent`); on transient, surface a retry-able `payment.gatewayUnavailable`; never leave an order in half-paid limbo — order state + payment state are reconciled by **webhook idempotency** (patterns.md §A.20), and payment is verified server-side, never trusted from the Comgate redirect params. |
| **SendGrid** (email) | Order/confirmation fails because the email didn't send | Email is a **side effect** — enqueue it; a send failure is logged + retried by the queue/Function, it does **not** fail the command. |
| **Packeta** (shipping) | Checkout blocks because a label/pickup-point lookup failed | Pickup-point selection can degrade to a retry-able message; label creation is a **post-payment side effect** (enqueued), so a Packeta blip never blocks the paid order — it retries. |
| **ARES** (registry) | Maker onboarding / `IČO` validation hard-fails | ARES lookup is an enrichment, not a gate: on a transient outage, accept the maker-entered data and reconcile/enrich later, or surface a retry-able message — never a 500. |
| **Mapbox** (geocoder) | Address save or "makers near you" crashes | Geocoding is best-effort; store the address without coordinates and backfill later. Distance features degrade, addresses don't. |
| **Fiscal / invoicing** | Customer order completion is blocked | Driven by `CountryConfiguration.InvoicingMode`: `StandardVat` generates the invoice as a queue-triggered side effect after payment (never inline); only `StrictFiscalReporting` may hold the **receipt/invoice**, never the **order**. A failed registration retries via the fiscal-retry Function. |
| **Azure Queue/Blob** | Data loss; the command silently drops a side effect | If the enqueue is part of the transaction, a failure should fail the command **before** committing user-visible state, OR use the outbox pattern so the side effect is durable. Never "fire and hope". File access is backend-only — no direct browser→blob links. |
| **PostgreSQL** | Cascading crash | Connection resilience (Aspire/Polly) for transient blips; a hard outage returns a clean 503, not a stack trace. Handlers never call `SaveChangesAsync` — the `UnitOfWorkPipelineBehavior` owns the commit, so a failed commit fails the whole command cleanly. |

## Background jobs & retries

- Side-effecting work that can fail transiently goes through a **queue + Azure Function**, not inline
  in the request — so it's durable and retried, and the user isn't blocked waiting on it. Invoice
  generation and the weekly payout batch are already shaped this way.
- Retries use **backoff** and read the **error classification**: `Transient` → retry; `Permanent` →
  stop + flag for the owner (an admin failures area); `Configuration` → alert, don't retry forever (a
  rotated Comgate/SendGrid key, a changed provider contract).
- Every retry path has a **dead-end**: a max attempt count and a visible place a human can see what's
  stuck (a failures table / Admin screen), so nothing retries silently forever. The self-running-
  marketplace bar means a stuck payout or invoice must surface to the operator, not vanish.

## What to alert on (so the operator isn't surprised)

- A spike in `Permanent` / `Configuration` external errors (a key rotated, a provider contract changed).
- A background job's failure count crossing a threshold (payout batch, invoice generation, fiscal retry).
- Health check failing for any of the four API hosts — especially `Web.Public` (webhook/cron ingress).
- A queue backing up (messages not being processed).

## Reviewer / readiness checklist (for anything touching external services, jobs, or hot paths)

1. Structured logs with correlation id carried across the queue boundary; no PII above Debug.
2. Every external call (inside `Infra.Clients`) classifies its error and logs the boundary.
3. The feature degrades per the matrix above — the core customer/maker action is not blocked by a
   non-core dependency.
4. Side effects are enqueued (durable + retried), not inline-fire-and-forget.
5. Idempotent (patterns.md §A.20) so retries / webhook re-deliveries are safe; payments verified
   server-side, not from redirect params.
6. There's a visible dead-end for failures (a human can see what's stuck).
7. Health check covers any new critical dependency, on the right host.

---

**Where this sits in the process:** the Optimizer owns Gate 5 and applies this checklist; the author
of the change is expected to have already satisfied it (verify-not-trust, Gate 8). SecOps
([secops.md](../../.claude/agents/secops.md)) co-signs the no-PII and server-side-verification rows;
dotnet-backend and dotnet-db own the logging, classification, and durability shape; l10n owns the
`cs-CZ` string for any new retry-able `BusinessErrorMessage` surfaced to the user.
