# Sprint 0 — Discovery

**Dates:** 2026-05-21 → 2026-05-21 (single discovery push)
**Owner:** PM (with Architect, BA running the bulk of the work)
**Status:** **Phase 1 complete. Awaiting Batch 10 sign-off from user.**

---

## Completed batches

### ✅ Phase 0 — Team & process setup
- Roster: 10 sub-agents (PM, BA, Architect, dotnet-backend, dotnet-db, frontend, L10n, QA, Reviewer, SecOps)
- Process docs: discovery, ticket lifecycle, quality gates, communication
- Templates: ticket, ADR, user story, test plan
- Pattern catalog (`patterns.md`) — initial dual-stack-pivot version
- Escalation channel: `questions/open.md`

### ✅ Batch 1 — Personas
Locked. See [`../personas.md`](../personas.md).
- Customer: B2C + B2B unified flow, CZ-only, escrow model
- Maker: hybrid (solo OSVČ + 1–2 anchor workshops), single user per maker
- Admin: 2 people, 1 role, daily checks + weekly payouts

### ✅ Batch 2 — Foundational ADRs
- [0001](../adr/0001-layering.md) Four-layer architecture
- [0002](../adr/0002-command-query-and-result.md) Command/Query, Result, pipeline middleware
- [0003](../adr/0003-money-and-currency.md) Money as minor units
- [0004](../adr/0004-country-configuration.md) `CountryConfiguration` control plane
- [0005](../adr/0005-route-groups-and-audience-separation.md) Per-audience routes + customer-as-authenticated
- [0006](../adr/0006-dependency-injection.md) tsyringe DI — **superseded by 0008**

### ✅ Batch 2.5 — Stack pivot (Supabase → .NET)
- [0007](../adr/0007-stack-pivot-dotnet-backend.md) Pivot record
- [0008](../adr/0008-dotnet-dependency-injection.md) .NET DI (supersedes 0006)
- `patterns.md` rewritten for dual-stack (Section A backend, Section B frontend)
- All architecture docs updated
- Agent charters rewritten: `architect`, `dotnet-backend`, `dotnet-db`, `frontend`; removed old `backend.md` and `db.md`
- Repo reorganized: Next.js moved under `/frontend/`; Supabase artifacts deleted; `/backend/` placeholder created
- `CLAUDE.md` rewritten as dual-stack guardrails
- Frontend pages broken-until-rewritten (option 1: no mocks)

### ✅ Batch 3 — Domain ADRs
- [0009](../adr/0009-numbering.md) Numbering (orders, invoices gap-free, payout batches)
- [0010](../adr/0010-address-model.md) Address + Mapbox geocoding
- [0011](../adr/0011-file-storage.md) Azure Blob via backend
- [0012](../adr/0012-authentication.md) Custom auth (password + magic link + Google OAuth)
- [0013](../adr/0013-data-scoping-and-soft-delete.md) Application-layer scoping; EF Core soft-delete filter
- [0014](../adr/0014-admin-audit-log.md) Append-only admin audit log

### ✅ Batch 3.5 — RDD discipline
- [0015](../adr/0015-responsibility-driven-design.md) Responsibility-Driven Design adopted
- Roles catalog scaffold + template
- Updated user-story template to include "Roles in play" block
- Charters updated (architect, ba, reviewer) to enforce role-file parity

### ✅ Batch 4 — Integration ADRs
- [0016](../adr/0016-payments-comgate.md) Comgate as launch payment provider
- [0017](../adr/0017-shipping-packeta.md) Packeta as launch shipping carrier
- [0018](../adr/0018-company-registry-ares.md) ARES + caching
- [0019](../adr/0019-email-resend.md) Resend + MJML templates
- [0020](../adr/0020-background-jobs.md) Azure Functions + outbox
- [0021](../adr/0021-api-versioning.md) URL-path versioning + deprecation policy
- [0022](../adr/0022-nswag-pipeline.md) NSwag pipeline + CI parity check

### ✅ Batch 5 — NFR
- [0023](../adr/0023-non-functional-requirements.md) Perf budgets, scale, availability, observability, accessibility, testing, deployment

### ✅ Populate roles catalog
24 role files written:
- 11 aggregates: Order, Maker, Product, Invoice, PayoutBatch, User, Category, Review, OrderMessage, CountryConfiguration, AdminAuditLogEntry
- 2 value objects: Money, Address
- 4 domain services: OrderPricing, OrderNumbering, InvoiceNumbering, Outbox
- 1 application service: AuthService
- 6 adapters: PaymentProvider, ShippingCarrier, CompanyRegistry, EmailProvider, AddressGeocoder, BlobStorage

### ✅ Batches 6–8 — User stories
- Customer: 20 stories ([`user-stories/customer/README.md`](../user-stories/customer/README.md))
- Maker: 17 stories ([`user-stories/maker/README.md`](../user-stories/maker/README.md))
- Admin: 17 stories ([`user-stories/admin/README.md`](../user-stories/admin/README.md))

### ✅ Batch 9 — Backlog
97 tickets across 6 phases ([`tickets/INDEX.md`](../tickets/INDEX.md)).
- Phase 1 Foundation: 16 tickets (~10 days)
- Phase 2 Identity: 17 tickets (~12 days)
- Phase 3 Catalog: 10 tickets (~6 days)
- Phase 4 Orders: 28 tickets (~20 days)
- Phase 5 Post-order: 20 tickets (~12 days)
- Phase 6 Polish: 6 tickets (~5 days)
- **Total: ~65 agent-days; 10-week sprint plan**

### ✅ Batch 10 — Handoff package
[`HANDOFF.md`](../HANDOFF.md) — single reviewable document. Awaiting user sign-off.

---

## What's next (after sign-off)

### Phase 0.6 — Solution scaffold
PM picks ticket T-0001. `dotnet-backend` scaffolds the .NET solution (13 projects per [`agents/dotnet-backend.md`](../../.claude/agents/dotnet-backend.md)). `dotnet-db` follows with the `MakablesDbContext` and audit interceptor. Sprint 1 closes when the four hosts run, OpenAPI emits, NSwag pipeline works, and an empty Azure environment deploys.

### Phase 1 build — autonomous, PR-only checkpoints
Per the user's directive in setup: **fully autonomous through sprints; PR-only checkpoints**. PM reports each sprint's status via a new `status/sprint-N.md` file. User sees PRs in GitHub; signs off on merges only when something material is up for review.

---

## Open questions (non-blocking)

3 entries in [`../questions/open.md`](../questions/open.md). All have defensible defaults applied. None block sign-off.

---

## Risks tracked at handoff

See [`HANDOFF.md`](../HANDOFF.md) §9. None require pre-sign-off mitigation.
