# Sprint 7 — status

**Period:** 2026-06-02 → ongoing
**Goal (per `INDEX.md`):** Phase 4 first third — orders foundation through payment-paid event. End of sprint: a customer can place an order, pay via Comgate, and the system transitions to `Paid` end-to-end with the invoice queued for generation.

The original 10-sprint proposal in [`docs/tickets/INDEX.md`](../tickets/INDEX.md#sprint-plan-proposed) put **Phase 4 first third (§60–69) under Sprint 5**, predicting that each sprint would cover more tickets. Actual progress diverged by two sprints (Sprints 1–4 covered Phases 1–2 + Phase 3 backend; Sprints 5–6 covered Phase 3 frontend). Sprint 7 picks up where the proposed plan said Sprint 5 would — same goal, two cycles later:

> "Order placement → payment → invoice generation works end-to-end (no UI for tracking yet)."

## Sprint 6 carry-overs picked into Sprint 7

Before opening the order tickets, three Sprint-6 carry-overs land first. Each is small but unblocks downstream work without growing scope inside the order tickets themselves.

| Ticket | Why now | Size |
|---|---|---|
| T-0049c | `IOperationFilter` rewriting multipart schemas (`{ file: binary }` + `required: true`) so NSwag emits typed multipart parameters. T-0064 (order attachments) is a new multipart endpoint; closing this now means the order frontend lands typed instead of replicating T-0049's `FileParameter | undefined` workaround. **(2026-06-02: expanded to full ticket → `in_progress`, owner `dotnet-backend`; see [`T-0049c-multipart-operation-filter.md`](../tickets/T-0049c-multipart-operation-filter.md).)** | S |
| Latent-platform-issues audit | Read every shipped `parseErrorResponse`-like consumer + every helper that mirrors a backend DTO. Either add tests pinning the wire shape or open follow-up tickets. Sprint 6 surfaced 8 latent bugs (mostly Sprint-2 era); the order layer's state-machine + outbox + payment-webhook surfaces will amplify any contract drift. Runs as its own branch, not under a single ticket. | M |
| `patterns.md` catalog update | Eight Sprint-6 primitives need explicit subsections (URL-state pagination, `buildProductImageUrl`, `formatWeight`, `<section>`-not-`<main>`, `generateMetadata` branching, multipart through `apiFetch`, SSR cookie forwarding, shared display-only constants). Six others overlap existing entries and need cross-references. Catalog stays the source of truth before Phase 4 produces more primitives. | M |

After those three land, the Phase 4 backlog opens with **T-0060**.

## Phase 4 ticket plan (first third)

Per `INDEX.md` Phase 4 (Orders) §60–69:

| Ticket | Title | Size | Depends on |
|---|---|---|---|
| T-0060 | Order entity + state machine + IOrderRepository (scoped ForCustomer / ForMaker / Unscoped) | L | T-0033, T-0041 |
| T-0061 | OrderPricing domain service + PricingService orchestrator | M | T-0010, T-0041 |
| T-0062 | OrderNumber + IOrderNumberGenerator integration into CreateOrder | S | T-0007, T-0060 |
| T-0063 | CreateOrder command + Validator + Handler + controller; persists Order in `PendingPayment` | L | T-0060, T-0061, T-0062 |
| T-0064 | Order attachments upload endpoint + streaming download | M | T-0042, T-0063 |
| T-0065 | IPaymentProvider + ComgatePaymentProvider; IPaymentProviderFactory | L | T-0063 |
| T-0066 | Comgate webhook controller — IP allowlist + status re-fetch + idempotency | M | T-0065 |
| T-0067 | MarkOrderPaid — transitions PendingPayment → Paid; enqueues outbox events | M | T-0066, T-0011 |
| T-0068 | Invoice entity + IInvoiceRepository + IInvoiceNumberGenerator + InvoiceService.IssueAsync + QuestPDF | L | T-0011, T-0042, T-0061 |
| T-0069 | GenerateInvoice Function (queue-triggered from outbox); attaches PDF to outbox customer email event | M | T-0068, T-0029 |

10 tickets, mostly L/M, all sequentially-coupled. The biggest blast-radius work in the platform; payments + money + state machines + outbox + invoice generation all converge here.

## Open blockers

None. All Phase 4 first-third tickets' dependencies are on `master` (Phase 1, T-0011 outbox, T-0033 Maker, T-0041 Product, T-0042 BlobStorage).

## Carried follow-ups (still open from Sprint 6)

- Czech `Intl.PluralRules` in `t()` — small ticket, deferred.
- Toast primitive in `components/ui/` — defer until two callers want it.
- FluentValidation nested-rules support in the field flattener — defer until a nested rule appears.
- Categories list endpoint (T-0119 placeholder) — wire up when admin category CRUD ships.
- Duplicate `verified` i18n key consolidation — informational; consolidate when convenient.
- ADR-emergence-from-review pattern documentation — worth formalising as a `docs/processes/` entry or ADR 0025. Not blocking.

## Definition of done (sprint level)

- [ ] T-0049c merged
- [ ] Latent-platform-issues audit merged (either as test additions or follow-up tickets opened)
- [ ] `patterns.md` catalog update merged
- [ ] T-0060 → T-0069 merged
- [ ] Sprint 7 retrospective added to this file
