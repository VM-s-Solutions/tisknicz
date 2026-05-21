# Batch 10 — Handoff package

> Single reviewable document. Read top to bottom; sign off at the end if you're ready for the build phase to begin.

**Date prepared:** 2026-05-21
**Prepared by:** Architect + PM + BA (this discovery run)
**Awaiting decision:** user sign-off on backlog + ADRs

---

## 0. Summary

Phase 1 — Discovery — is complete. We have:

- **3 personas** (customer, maker, admin), confirmed
- **23 ADRs accepted** (foundational, pivot, domain, integration, NFR, RDD discipline)
- **24 role files** in the RDD catalog
- **54 user stories** across the 3 personas
- **97 tickets** in a 6-phase backlog, ~10 working weeks of work
- **2 codebases** in a monorepo: `/backend/` (.NET 10) and `/frontend/` (Next.js 16)
- **No code written** yet beyond what existed pre-pivot; Phase 0.6 (scaffold) begins on your sign-off

The next thing that happens after sign-off: PM picks ticket T-0001 (scaffold the .NET solution) and the team starts.

---

## 1. What you're signing off

Three artifacts:

1. **The ADR set** — 23 accepted architectural decisions in [`adr/`](./adr/). These are durable; changing one after sign-off requires writing a superseding ADR.
2. **The role catalog** — 24 role files in [`architecture/roles/`](./architecture/roles/). These are the domain model. Refactor-friendly: changing a role updates its file in the same PR.
3. **The backlog** — 97 tickets in [`tickets/INDEX.md`](./tickets/INDEX.md). The PM will expand each row into a full ticket file as it moves to `ready`. You don't sign off on every AC; you sign off on scope, sequencing, and Phase-1 readiness.

You are **not** signing off on:
- Specific code (none written yet)
- The exact 10-week timeline (it's a rolling estimate; PM will report actuals each sprint)
- Open questions (3 are tracked in [`questions/open.md`](./questions/open.md) — none blocking)

---

## 2. The pivot (Batch 2.5) — what changed

You made a foundational pivot mid-discovery:

> "We have to completely remove Supabase and we will build our own .NET backend, so that it can be easily expanded in the future."

That decision reshaped everything that followed. The pre-pivot decisions (ADRs 0001–0006) were validated in spirit but moved into a different stack:

| Concern | Pre-pivot | Post-pivot |
|---|---|---|
| Backend | Next.js Route Handlers + Supabase | .NET 10 Clean Architecture, 4 per-audience hosts, EF Core + Postgres |
| Auth | Supabase Auth | Custom: `User`/`RefreshToken` + Argon2id + JWT + Google OAuth |
| Storage | Supabase Storage | Azure Blob, all access via backend (no direct browser → blob URLs) |
| ORM | `@supabase/supabase-js` | EF Core 10 |
| Realtime | Supabase Realtime | Polling at MVP; SignalR if needed later |
| Frontend | Calls Supabase directly | Pure presentation, calls .NET via NSwag-generated TypeScript client |
| Deploy | Vercel + Supabase | Azure (App Service + Postgres Flexible + Blob + Functions + Key Vault + App Insights) |
| RLS | Postgres RLS via Supabase | Application-layer scoping (repository methods + EF Core global query filters for soft delete) |

The pivot is recorded in [ADR 0007](./adr/0007-stack-pivot-dotnet-backend.md) with full rationale.

---

## 3. The 23 accepted ADRs at a glance

| # | Title | Topic |
|---|---|---|
| 0001 | Four-layer architecture | `Core.Domain` → `Core.AppServices` → `Infra.*`, dependencies inward |
| 0002 | Command/Query, Result, AppError, pipeline middleware | CQRS via MediatR, `BusinessResult<T>`, automatic validation + UoW |
| 0003 | Money as `long` minor units, currency-aware | Half-up rounding, basis-points VAT, CZK display strips haléře |
| 0004 | CountryConfiguration | Per-country control plane; code never branches on country directly |
| 0005 | Per-audience route groups | `(public) (auth) (customer) (maker) (admin)` + 4 API hosts |
| 0006 | tsyringe DI | **Superseded by 0008** (post-pivot) |
| 0007 | Stack pivot | The big change |
| 0008 | .NET `Microsoft.Extensions.DependencyInjection` | Keyed services for per-country adapters; constructor injection only |
| 0009 | Numbering | `M-CZ-20260001` orders, `FV-CZ-20260001` gap-free invoices, `VYP-CZ-2026-W21` batches |
| 0010 | Address model + Mapbox geocoding | Structured fields, autocomplete, per-country format validators |
| 0011 | File storage | Azure Blob; all access through backend; no direct browser links |
| 0012 | Authentication | Password + magic link + Google OAuth; Argon2id; refresh-token rotation with reuse detection |
| 0013 | Data scoping + soft delete | EF Core global filter for `IsActive`; application-layer country/ownership scoping |
| 0014 | Admin audit log | Append-only DB table; trigger-enforced; `AdminAuditPipelineBehavior` |
| 0015 | Responsibility-Driven Design | RDD discipline as governance; one role per file |
| 0016 | Payments (Comgate) | Webhook re-fetch pattern; outbox for side effects |
| 0017 | Shipping (Packeta) | Widget for pickup point; backend-mediated label PDFs; 6-h carrier status sync |
| 0018 | Company registry (ARES) | Two-layer cache (memory + DB); stale-cache fallback on outage |
| 0019 | Email (Resend) | MJML templates compiled at build time; locale-aware; all sends via outbox |
| 0020 | Background jobs | Azure Functions on Docker; outbox is the message hub; queues for fan-out |
| 0021 | API versioning | URL-path versioning (`/api/v1/...`); 3-month deprecation window |
| 0022 | NSwag pipeline | OpenAPI per host per version → typed TS client; CI parity check |
| 0023 | NFR | Perf budgets, scale assumptions, availability, observability, testing, deployment |

Each ADR has alternatives considered, consequences, and compliance criteria a reviewer can check.

---

## 4. The 24 role catalog

Roles are the domain model. Each one is one page, with responsibility, collaborators, knows, does-NOT-know, lifecycle, invariants.

**Aggregates (11):** `Order`, `Maker`, `Product`, `Invoice`, `PayoutBatch`, `User`, `Category`, `Review`, `OrderMessage`, `CountryConfiguration`, `AdminAuditLogEntry`

**Value objects (2):** `Money`, `Address`

**Domain services (4):** `OrderPricing`, `OrderNumbering`, `InvoiceNumbering`, `Outbox`

**Application services (1):** `AuthService`

**Adapters (6):** `PaymentProvider`, `ShippingCarrier`, `CompanyRegistry`, `EmailProvider`, `AddressGeocoder`, `BlobStorage`

Catalog index: [`architecture/roles/README.md`](./architecture/roles/README.md). Every story and ticket points back to the roles it uses.

---

## 5. The 54 user stories

| Persona | Count | File |
|---|---|---|
| Customer | 20 | [`user-stories/customer/README.md`](./user-stories/customer/README.md) |
| Maker | 17 | [`user-stories/maker/README.md`](./user-stories/maker/README.md) |
| Admin | 17 | [`user-stories/admin/README.md`](./user-stories/admin/README.md) |

Each story has a roles block (which domain roles it uses/extends), acceptance criteria in Given/When/Then, and an explicit out-of-scope list.

**Customer surface** covers the full marketplace experience: register → confirm email → browse → order → pay → track → message → confirm delivery → review → manage profile.

**Maker surface** covers business operation: register via ARES → activate → list products → accept orders → ship (Zásilkovna or personal pickup) → message → see payouts → respond to reviews.

**Admin surface** covers platform operation: verify makers → run weekly payouts → handle refunds and disputes → manage countries → see audit log → GDPR delete.

---

## 6. The 97-ticket backlog

Six phases. [`tickets/INDEX.md`](./tickets/INDEX.md) is the manifest.

| Phase | Goal | Tickets | Approx work |
|---|---|---|---|
| **1 — Foundation** | Solution scaffolds, hosts run, OpenAPI emits, NSwag works, empty Azure deploys | 16 | ~10 days |
| **2 — Identity** | Auth + ARES + maker registration end-to-end | 17 | ~12 days |
| **3 — Catalog** | Products + browse + maker profile + product detail | 10 | ~6 days |
| **4 — Orders** | Place → pay → escrow → ship → deliver, plus invoices, messages | 28 | ~20 days |
| **5 — Post-order** | Reviews, weekly payouts, admin operations | 20 | ~12 days |
| **6 — Polish** | Static pages, SEO, load test, accessibility, runbooks | 6 | ~5 days |
| **Total** | | **97** | **~65 agent-days** |

Sprint plan: roughly 10 working weeks from sign-off to soft launch. Conservative; PM reports actuals each sprint.

---

## 7. Critical design choices — sanity check

These are the choices most likely to bite us if wrong. If any feel off, **say so before sign-off** — they are still cheap to change.

### Architecture

- **`/backend/` and `/frontend/` in one monorepo, single Git history.** PRs that span both ship atomically.
- **Domain layer (`Core.Domain`) has zero third-party references.** Entities are pure C#. Even EF Core attributes are forbidden — schema mapping lives in `Infra.Database/Configurations/`.
- **Four backend hosts, not one.** `Web.Customer`, `Web.Maker`, `Web.Admin`, `Web.Public`. They share `Core.*` + `Config` + `Infra.*` but each has its own auth audience, CORS posture, rate limit.
- **No `if (countryCode == "CZ")` in domain code.** Look up `CountryConfiguration` instead. The future second country is a row insert + adapter registration, not a refactor.
- **No business logic in the frontend.** All state machines, validation rules, money math live in `.NET`. Frontend formats, displays, and submits.

### Operational

- **No Supabase Realtime replacement at MVP.** Order status uses polling (page refresh or explicit re-fetch on action). SignalR added if user research shows it's needed.
- **Custom auth, no managed IdP.** ~2-3 weeks of careful work, no vendor lock, no per-MAU pricing risk. Wrapped behind `IAuthService` so we could swap later.
- **Single Azure region (West Europe).** Multi-region added at 5,000 orders/day, not before.
- **Single Supabase replacement, single Postgres.** Per-country databases rejected; RLS-equivalent is application-layer.
- **Outbox is the message hub.** Every off-request side effect (email, invoice render, label fetch, payout notification) goes through `outbox_event`. Comgate webhook returns 200 the moment the order is `Paid`; emails arrive seconds later via outbox processor.
- **Weekly payouts, CSV bank export.** Admin runs the batch (or it runs Monday 02:00 UTC), exports CSV, imports to the bank, marks the batch `Completed`. Makers see `payout-sent` emails after.

### Compliance

- **Gap-free invoice numbering** per CZ tax law. `FOR UPDATE` lock; allocation only commits if the surrounding command succeeds.
- **Admin audit log is append-only.** DB trigger rejects `UPDATE`/`DELETE`. Two admins see each other's actions on the audit page.
- **GDPR delete** is the only hard-delete path. It anonymizes related orders (legal retention) and hard-deletes the User + refresh tokens.
- **No customer-supplied PII in logs.** Logger redact list covers `PasswordHash`, refresh-token raw values, customer phones in payloads (numbers OK; full records redacted).

### What we explicitly *don't* build at MVP

Captured in story out-of-scope sections, but worth surfacing once more:

- B2B-specific order fields (PO numbers, NET-30) — invoice carries IČO/DIČ if user enters them, but no special B2B flow
- Custom-quote flow for `OnRequest` products — placeholder "Coming soon"
- Pre-purchase messaging between customer and maker
- Multi-user maker accounts
- Subscriptions or recurring orders
- Promo codes / referral system
- Mobile apps (web is mobile-responsive)
- Marketing emails / preferences center
- Realtime UI updates
- E2E Playwright tests (manual test plans + integration tests cover MVP)
- Second country

Several of these (multi-user maker, custom-quote, B2B-NET-30) are likely v1.1 candidates.

---

## 8. Open questions (non-blocking)

Tracked in [`questions/open.md`](./questions/open.md). All three are post-MVP-flavored and have defensible defaults applied:

- **Q-0001 — Multi-user per maker account.** Default: post-MVP v1.1; schema accommodates via a future `maker_user` join table.
- **Q-0002 — Custom-quote flow for `OnRequest` products.** Default: show as "Coming soon" placeholder; no order CTA.
- **Q-0003 — Admin assistant technical background.** Default: assume non-technical; admin UI is built to be usable without SQL/CLI escape hatches.

If any of these need an immediate answer, flag at sign-off.

---

## 9. Risks and mitigations

| Risk | Mitigation |
|---|---|
| Custom auth has security pitfalls | ADR 0012 prescribes Argon2id, refresh rotation with reuse detection, audience-bound JWTs, lockout. SecOps reviews every auth-touching PR. |
| Comgate or Packeta outage during launch week | Outbox pattern decouples webhook acknowledgment from side effects. Customer experience degrades gracefully (retry after) rather than failing hard. |
| ARES rate limit or outage | Two-layer cache (memory + 24h DB); stale-cache-with-flag fallback on outage. Maker registration keeps working. |
| Frontend pages broken during backend build phase | Option 1 chosen: no mocks. Pages stay loudly broken until the corresponding endpoint exists. Catches "silently skipped" regressions early. |
| 10-week timeline slips | PM reports actuals each sprint; backlog priorities re-ranked at each sprint boundary; cosmetic Phase 6 work absorbs slack first. |
| Pivot rework risk | Pre-pivot ADRs 0001–0005 were validated under the new stack; all patterns transferred. ADR 0006 was explicitly superseded by 0008. No silent rework. |
| Cost overrun on Azure | NFR ADR sets CZK 5–15K/month target; reviewed quarterly. Premium SKUs in production are the bulk of the cost; downgrade-to-Standard is one config change. |
| Multi-country abstractions over-engineered | We ship CZ-only data + UI. Multi-country abstractions cost ~5% extra code surface; the alternative (rework when country #2 lands) costs 10-100x. Worth it. |

---

## 10. Phase 0.6 (next step after sign-off)

If you sign off, PM picks **T-0001 — Scaffold .NET solution skeleton** and the team begins.

Concretely the first sprint:
1. **T-0001** `dotnet-backend` scaffolds the solution: 13 projects, package references, namespaces.
2. **T-0002** `dotnet-db` writes the `MakablesDbContext`, audit interceptor, soft-delete query filter.
3. **T-0003** `dotnet-backend` wires MediatR + FluentValidation + pipeline behaviors.
4. **T-0004** `dotnet-backend` adds the shared types (`BusinessResult`, `Error`, `BusinessErrorMessage`, `ICommand`/`IQuery`, `MakablesApiController`).
5. **T-0005** `dotnet-backend` implements the `Money` value object with tests.
6. ... and so on through T-0016.

Each ticket is one PR. You see them in GitHub. PM reports sprint status weekly via [`status/sprint-N.md`](./status/).

---

## 11. Sign-off

If you accept this package as the basis for the build phase, your sign-off looks like one of:

- A reply saying "approved, proceed" → I move PM into action and ticket T-0001 starts.
- A reply saying "approved with changes: <list>" → I update the relevant artifact(s) and re-prepare for sign-off.
- A reply saying "hold, I want to discuss X" → no work starts; we converge on X first.

Whichever way, the next move is yours.

---

## Appendix — file index

```
docs/
├── HANDOFF.md                            # this file
├── README.md                             # docs root
├── personas.md                           # 3 personas
├── glossary.md
├── adr/                                  # 23 accepted ADRs + template
├── architecture/
│   ├── overview.md
│   ├── patterns.md                       # canonical pattern catalog (~27 patterns)
│   ├── extension-points.md
│   ├── multi-country.md
│   ├── money.md
│   └── roles/                            # 24 role files + index + template
├── user-stories/
│   ├── customer/README.md                # 20 stories
│   ├── maker/README.md                   # 17 stories
│   ├── admin/README.md                   # 17 stories
│   └── template.md
├── tickets/
│   ├── INDEX.md                          # 97-ticket manifest + sprint plan
│   └── template.md
├── process/
│   ├── communication.md
│   ├── discovery.md
│   ├── ticket-lifecycle.md
│   └── quality-gates.md
├── review/
│   └── checklist.md
├── security/
│   ├── rls-audit.md
│   └── webhook-verification.md
├── test-plans/
│   └── template.md
├── questions/
│   └── open.md                           # 3 open, all non-blocking
└── status/
    └── sprint-0.md
```
