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
| `patterns.md` catalog update | Eight Sprint-6 primitives need explicit subsections (URL-state pagination, `buildProductImageUrl`, `formatWeight`, `<section>`-not-`<main>`, `generateMetadata` branching, multipart through `apiFetch`, SSR cookie forwarding, shared display-only constants). Six others overlap existing entries and need cross-references. Catalog stays the source of truth before Phase 4 produces more primitives. **(2026-06-03: complete. Added 13 new B subsections (B.7–B.19) covering layout + URL state, display helpers, API access, validation flattening, plural-neutral i18n, host-anchored blob URLs. Rewrote B.3 (Bearer-access-token claim was wrong — actual model is audience-scoped cookies + ADR 0024 SSR forwarding) + B.4 (the "401 → refresh → retry once" claim doesn't match the implementation — refresh isn't wired in yet). Cross-refs added to A.4, A.6 (`[ProducesResponseType]` discipline + honest-400 lesson), A.18 (formatCzk display mirror), A.21 (schema transformer convention). Verified via 6-agent workflow: 4 parallel miners drafted from code, 1 cross-ref miner, 1 adversarial verifier confirmed every cited file:line. Now 19 B sections total; patterns.md is the live source of truth before Phase 4 produces more primitives.)** | M |

After those three land, the Phase 4 backlog opens with **T-0060**.

**2026-06-03 update:** T-0060 expanded to a full ticket file → `ready`, owner `dotnet-backend`. See [`T-0060-order-entity-state-machine.md`](../tickets/T-0060-order-entity-state-machine.md). This is the first Phase-4 ticket in flight; it locks the Order aggregate shape, state machine, scoped repository, and EF mapping that the remaining ~20 Phase-4 + Phase-5 tickets depend on. Three open questions surfaced in the ticket's Technical notes — none block T-0060 itself; two need user input before T-0072 / T-0083 land.

**2026-06-03 update:** T-0060 done. Order aggregate + 9-state machine + `IOrderRepository` (ADR 0013 surface) + migration + 65 new tests (922 total pass). User confirmed both open questions: (a) cancel-state edges at the entity layer are `PendingPayment | Paid | Accepted → Cancelled` with role enforcement deferred to commands (customer cancels from `PendingPayment` only; maker from `Paid` only; admin from any state); (b) `Order.Ship(autoDeliverWindowDays)` takes the window as a required parameter — T-0072 will hard-code 7, and `CountryConfiguration.AutoDeliverWindowDays` is added only if a future country materially differs. Code-quality review folded 2 Mediums in the same commit (translator over-mapping removed per the file's own policy; `ix_orders_state` made partial-WHERE `is_active`). T-0061 (OrderPricing + PricingService) opens next.

## Phase 4 ticket plan (first third)

Per `INDEX.md` Phase 4 (Orders) §60–69:

| Ticket | Title | Size | Depends on | State |
|---|---|---|---|---|
| T-0060 | Order entity + state machine + IOrderRepository (scoped ForCustomer / ForMaker / Unscoped) | L | T-0033, T-0041 | **ready** |
| T-0061 | OrderPricing domain service + PricingService orchestrator | M | T-0010, T-0041 | draft |
| T-0062 | OrderNumber + IOrderNumberGenerator integration into CreateOrder | S | T-0007, T-0060 | draft |
| T-0063 | CreateOrder command + Validator + Handler + controller; persists Order in `PendingPayment` | L | T-0060, T-0061, T-0062 | draft |
| T-0064 | Order attachments upload endpoint + streaming download | M | T-0042, T-0063 | draft |
| T-0065 | IPaymentProvider + ComgatePaymentProvider; IPaymentProviderFactory | L | T-0063 | draft |
| T-0066 | Comgate webhook controller — IP allowlist + status re-fetch + idempotency | M | T-0065 | draft |
| T-0067 | MarkOrderPaid — transitions PendingPayment → Paid; enqueues outbox events | M | T-0066, T-0011 | draft |
| T-0068 | Invoice entity + IInvoiceRepository + IInvoiceNumberGenerator + InvoiceService.IssueAsync + QuestPDF | L | T-0011, T-0042, T-0061 | draft |
| T-0069 | GenerateInvoice Function (queue-triggered from outbox); attaches PDF to outbox customer email event | M | T-0068, T-0029 | draft |

10 tickets, mostly L/M, all sequentially-coupled. The biggest blast-radius work in the platform; payments + money + state machines + outbox + invoice generation all converge here.

## Open blockers

None. All Phase 4 first-third tickets' dependencies are on `master` (Phase 1, T-0011 outbox, T-0033 Maker, T-0041 Product, T-0042 BlobStorage).

## Open questions surfaced during T-0060 expansion (2026-06-03)

Two need user input before downstream tickets land; one is internal to the team and proposed for self-resolution. None block T-0060.

1. **Cancellation authorisation rules** (blocks T-0083 auto-cancel + T-0107 admin manual change, not T-0060). The Order entity in T-0060 exposes the state-graph edges (`PendingPayment | Paid | Accepted → Cancelled`); the command layer decides who may take them. Proposal: customer can cancel from `PendingPayment` only; maker can cancel ("refuse") from `Paid` only; admin can cancel from any state (audited). User confirmation requested before T-0083 / T-0107 are expanded to full tickets.
2. **Per-country `AutoDeliverAt` window** (affects T-0072 default, not T-0060). T-0060 hard-codes 7 days as a default parameter on `Ship(...)`. Should T-0072 read the window from `CountryConfiguration` (multi-country-ready) or stay hard-coded? Architecturally consistent move is country-driven; pragmatic move at single-launch is hard-coded. Proposal: hard-code in T-0072; add `CountryConfiguration.AutoDeliverWindowDays` field only if a second country materially differs. User input welcome but not blocking.
3. **`Order.Create` factory return shape** (internal). Throws `ArgumentException` on impossible inputs (negative amounts, blank ids, inconsistent pricing) — same pattern as `Product.Create`. User-input errors are caught upstream by the `CreateOrder.Validator` (T-0063), so `Create` only sees vetted inputs. No user input needed; surfaced for completeness.

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
- [x] `patterns.md` catalog update merged (this PR; B.7–B.19 added, B.3/B.4 corrected, A.4/A.6/A.18/A.21 cross-referenced)
- [ ] T-0060 → T-0069 merged
- [ ] Sprint 7 retrospective added to this file
