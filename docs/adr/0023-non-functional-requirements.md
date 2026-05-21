---
id: 0023
title: Non-functional requirements — performance, scale, availability, observability, accessibility, testing, deployment
status: accepted
date: 2026-05-21
deciders: [Architect]
---

# 0023 — Non-functional requirements

## Context

ADRs 0001–0022 cover functional architecture. Non-functional choices need their own record so they're measurable and reviewable. This ADR sets the targets and the rationale; specific mechanics (App Insights configuration, deployment pipelines) live in implementation tickets.

## Decisions

### 1. Performance budgets

| Surface | Target (p95) | Hard ceiling (p99) | Measurement |
|---|---|---|---|
| Catalog page TTFB (SSR) | 400 ms | 1000 ms | App Insights |
| Catalog page LCP | 1.8 s | 3.0 s | Real User Monitoring |
| Product page TTFB (SSR) | 350 ms | 1000 ms | App Insights |
| Order creation API | 600 ms | 1500 ms | App Insights |
| Payment redirect URL receipt | 1500 ms | 3000 ms | App Insights |
| ARES lookup (cache hit) | 50 ms | 150 ms | App Insights |
| ARES lookup (cache miss) | 1500 ms | 4000 ms | App Insights |
| Customer dashboard list | 400 ms | 1000 ms | App Insights |
| Maker dashboard list | 400 ms | 1000 ms | App Insights |
| Image (1080w product photo) | 200 ms (CDN-warm) | 800 ms | RUM |

Targets apply at the **MVP scale** (next section). Backend queries that miss the budget go on a perf-todo list; pages that miss the LCP budget get a perf ticket within the next sprint.

### 2. Scale assumptions (MVP launch year)

- **DAU:** up to 1,000.
- **Orders/day:** up to 200 at peak.
- **Concurrent users:** up to 100.
- **Catalog browse RPS:** up to 50 (mostly bots + occasional spikes).
- **Database size:** up to 5 GB at end of year 1.
- **Blob storage:** up to 50 GB (product photos, invoices, labels).
- **Outbox throughput:** up to 5,000 events/day.

These numbers shape default infrastructure sizing. Doubling each is the "headroom" target before we re-provision.

### 3. Availability

- **Customer-facing surfaces** (catalog, product, order placement, customer dashboard, frontend pages): **99.5%** monthly. ~3.6 hours of downtime allowed per month.
- **Maker dashboard**: **99.0%** monthly.
- **Admin dashboard**: **99.0%** monthly, business hours only.
- **Webhook endpoints** (Comgate, Packeta, Resend): **99.9%** monthly. These mustn't lose data — if the host is down for a minute, Comgate retries; if down for an hour, customers see payments succeed but order state lag, and admin gets paged.
- **Background jobs (Azure Functions)**: best-effort; eventual consistency tolerated. Outbox lag > 5 minutes triggers an alert.

We do not run a multi-region setup at MVP. West Europe only. RTO/RPO targets formalized when we cross 5,000 orders/day.

### 4. Observability

#### Logging

- Structured logs via `ILogger<T>` everywhere; JSON output in production.
- Required structured properties on every log line:
  - `correlation_id` — propagated from `traceparent` header
  - `user_id` (if authenticated)
  - `country_code`
  - `request_id` (per-HTTP-request UUID)
- Sensitive fields redacted at the logger layer (`PasswordHash`, full payment payloads, refresh-token raw values).

#### Tracing

- OpenTelemetry traces via `.AddServiceDefaults()` (Aspire pattern).
- Sampling: 100% of error traces, 10% of successful traces, 100% of webhook traces.
- Exported to Application Insights.

#### Metrics

- Standard ASP.NET metrics (request rate, latency, error rate).
- Custom metrics:
  - `outbox_lag_seconds` (now - oldest unprocessed row)
  - `outbox_stalled_count` (rows with `Permanent`/`Configuration` errors)
  - `payment_create_failures_total` (counter, labeled by provider + error_type)
  - `webhook_received_total` (counter, labeled by provider + outcome)
  - `auto_deliver_count` (gauge of orders auto-delivered per day)
  - `payout_batch_total_minor` (gauge of last batch's total)

#### Alerting (Azure Monitor)

| Alert | Threshold | Severity |
|---|---|---|
| Customer API 5xx rate | > 1% over 5 min | Sev 2 (page-able) |
| Webhook handler 5xx rate | > 5% over 5 min | Sev 1 (page-able, immediate) |
| Outbox lag | > 5 min | Sev 2 |
| Outbox stalled count | > 10 | Sev 3 (next business day) |
| Database CPU | > 80% over 10 min | Sev 2 |
| Failed login rate | > 50/min from same IP | Sev 3 (potential attack) |
| Auto-deliver crashed | any failure | Sev 2 |

Sev 1 alerts go to admin email + (future) SMS. Sev 2/3 to admin email only. Two admins on the rotation; same alert recipients at launch.

### 5. Accessibility

- **Target: WCAG 2.1 Level AA** for customer-facing surfaces (catalog, product, order placement, account pages).
- Maker and admin dashboards target AA but may have one-off issues (data tables, complex forms) that we accept as backlog items.
- Audit tooling: `axe-core` automated checks in frontend CI; manual keyboard-nav check per major release.
- Czech-language screen reader testing: NVDA + Firefox once before launch.
- Color contrast: 4.5:1 for body text, 3:1 for large text. Brand dark theme verified against contrast tooling.
- Focus indicators: visible on every interactive element. No `outline: none` without replacement.
- Form errors: associated with their input via `aria-describedby`; never color-only.

### 6. Testing strategy

#### Backend (.NET)

| Layer | Tool | Coverage target | Notes |
|---|---|---|---|
| Unit (`Core.Domain`, validators, services, money math, numbering) | xUnit + FluentAssertions + NSubstitute | 80% line | Pure logic; no DB |
| Handler (Mediator handlers) | xUnit, with `Substitute.For<IRepo>()` | every command + every query: happy path + 1 failure path | No HTTP, no DB |
| Integration (per host) | `WebApplicationFactory<Program>` + Testcontainers Postgres | every controller endpoint: happy path + auth check + one failure | Real DB, mocked external HTTP |
| Contract (NSwag spec parity) | Hash-comparison in CI | 100% | Per ADR 0022 |

Coverage measured on PR; CI fails if a PR drops coverage by more than 2 percentage points.

#### Frontend

- **Component tests:** vitest + Testing Library for pure logic and visual components. Coverage target 60%.
- **E2E**: Playwright, **post-MVP**. Manual test plans (per ADR-defined `qa` charter) cover MVP launch.
- **Visual regression**: not in MVP. Added if visual drift becomes a problem.

#### Manual testing

QA agent executes the test plan per `docs/test-plans/T-NNNN.md` on every PR before merge. Plans live in the repo, are versioned with code.

#### Load testing

- One synthetic load run before launch: 100 concurrent users, mixed catalog browse + order placement, 30 min, k6 script committed to `deploy/load-tests/`.
- Pass criteria: p95 latency under budget; zero 5xx; database CPU under 70%.

### 7. Deployment topology

#### Environments

| Env | Purpose | Backend | Frontend | Database |
|---|---|---|---|---|
| **dev** | Developer laptops | `dotnet run` | `npm run dev` | docker compose Postgres |
| **staging** | Internal preview before prod | Azure App Service (Linux B2) | Azure App Service (Node) | Postgres Flexible Server (Burstable B1ms) |
| **production** | Live | Azure App Service (Linux P1v3) | Azure App Service (Node, P1v3) | Postgres Flexible Server (General Purpose D2s_v3) |

Staging mirrors production topology at smaller scale. Production sizing reviewed quarterly.

#### Infrastructure as code

Azure resources defined in `deploy/bicep/` (Bicep over Terraform because Cleansia parity and tighter Azure integration). `main.bicep` parameterized per environment. Pipelines run `az deployment group create` on environment changes.

#### Deploy pipelines (GitHub Actions)

- **PR pipeline:** build + unit + integration + spec-parity checks. No deploy.
- **Merge to master:** build, run all tests, deploy to staging, run smoke tests, notify admin in Slack/email.
- **Production deploy:** manual approval from admin user. Deploys backend + frontend together (atomic). Runs smoke tests on prod. Easy rollback (App Service deployment slot swap).

#### Database migrations

- EF Core migrations applied at startup of `Web.Customer` (one host designated as the "migration runner"). Other hosts wait for the migration runner via a startup readiness check.
- Migrations are forward-only. Reverting requires a new migration that undoes the change.
- Migrations reviewed in PRs (the diff includes the generated SQL).
- For zero-downtime: every migration must be backward-compatible (no `DROP COLUMN` while old code is still running). The pattern: add column → deploy backend that writes both → deploy backend that reads new → drop old column in next release.

#### Secrets

- Production secrets in Azure Key Vault. App Service references them via Key Vault references (`@Microsoft.KeyVault(SecretUri=...)`).
- Staging secrets in a separate Key Vault.
- Developer-local secrets in `appsettings.Development.local.json` (gitignored).
- No secrets in CI logs. Pipelines mask known secret patterns.

#### Backup and recovery

- **Postgres backups:** Azure-managed automatic backups, 7-day point-in-time-recovery window in production, 1-day in staging.
- **Blob storage:** soft-delete retention 30 days; geo-redundant storage (GRS) in production.
- **Manual restore test:** once per quarter; admin runs through the restore playbook in a scratch environment. Findings logged.

### 8. Cost ceiling (target, not hard)

MVP launch year target: **CZK 5,000–15,000/month** total Azure spend at MVP scale. Reviewed quarterly; sized up before we cross 5,000 orders/day.

## Alternatives considered

- **99.9% availability across all surfaces** — rejected as overkill for MVP. The cost of multi-region + tighter monitoring isn't justified by current usage projections. Reconsider at 5,000 orders/day.
- **Skip integration tests, rely on unit + manual** — rejected. Integration tests are cheap with Testcontainers and catch the wiring bugs that unit tests miss.
- **E2E Playwright in MVP** — rejected for time/maintenance cost. Manual test plans are sufficient at launch.
- **WCAG AAA target** — rejected; AAA requirements (e.g. 7:1 contrast, no implicit timing) are too restrictive for a marketplace UI. AA is the industry standard.
- **Multi-region Azure** — rejected for MVP. West Europe only. Add Central US (or similar) at 5,000 orders/day.

## Consequences

### Positive
- Targets are explicit and measurable. Sprint reviews can ask "are we hitting our p95 budget?"
- Observability is in from day one — debugging production issues won't require a heroic logging retrofit.
- Test pyramid is balanced: more unit + integration than E2E avoids slow CI without sacrificing safety.

### Negative
- Premium SKUs in production carry cost. Acceptable for launch (~CZK 8,000/month projected); reviewable quarterly.
- Manual QA at MVP scale ties admin assistant time to release cadence. Mitigated by automated checks catching most regressions.
- Backward-compatible migrations are extra work per schema change. Accepted as the cost of zero-downtime deploys.

## Compliance / verification

- SecOps: every alert rule above is configured in Azure Monitor before launch.
- QA: every PR carries a `docs/test-plans/T-NNNN.md` linked from the ticket.
- Reviewer: PR comment confirms coverage didn't drop more than 2 pp.
- SecOps + Architect: quarterly review of perf, scale, availability against actuals.

## Related

- Patterns: §A.5 pipeline behaviors (logging is one of them), §A.14 error classification (alerts on stalled outbox)
- ADR 0020 (background jobs surface custom metrics)
- ADR 0021 (versioning enables zero-downtime deploys via parallel versions)
- ADR 0022 (NSwag spec parity is one of the CI checks)
