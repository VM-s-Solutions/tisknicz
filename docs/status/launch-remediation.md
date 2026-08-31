# Launch remediation — execution tracker

**Opened:** 2026-08-31
**Baseline:** `8cf9992` (master, 2026-08-24, clean tree)
**Owner:** PM
**Status:** in progress

> **Verify against code, never against a ticket.** `docs/tickets/INDEX.md` and the
> per-ticket `status:` front-matter still disagree with merged code in both
> directions. 161 ticket files carry five different `status:` values (70 `ready`,
> 67 `done`, 12 `in_review`, 3 `in_progress`, 2 `draft`) plus 7 with no status
> field at all, against 167 INDEX rows. The mismatch is structural, not a simple
> count: only 149 distinct IDs across the files and 155 across the rows, because
> 12 files and 12 rows are duplicates; **10 IDs have a row but no file**, and
> **4 have a file but no row**. T-0191–T-0196 have no INDEX row at all, and
> T-0193 / T-0196 have neither a row nor a file despite shipping. Every item
> below was checked against source on this baseline.
>
> **This file is time-boxed, not a parallel backlog.** Holding sized work outside
> `docs/tickets/` is the drift this plan exists to repair, so it must not outlive
> the repair. As each lane closes its rows resolve into `INDEX.md`. What stays
> here permanently is the gate narrative and the verification log.

---

## How this effort runs

| Dimension | Choice |
|---|---|
| Execution | Direct implementation, spawning `reviewer` / `secops` subagents per the quality gates |
| Checkpoints | One approval per gate or lane, not per task |
| Git | One branch + PR per lane |

A deliberate deviation from `agents/process/ticket-lifecycle.md` (one PR per ticket):
the critical path is ~12 items across 4 lanes, and per-ticket PRs would mean as many
agent handoffs, each starting without the audit context that found the work.

Gate 8 applies unchanged: each lane records its **own** verification run below —
command, exit code, test counts. A reported pass with no recorded run is a fail.
Where a toolchain is absent locally it is recorded `DEFERRED-TO-CI`, never `PASS`.

---

## Where the project actually is

Measured on `8cf9992`, not inferred:

| Gate | Result |
|---|---|
| `dotnet build -c Release` | 0 warnings, 0 errors |
| `dotnet test` | 2,283 unit + 360 integration passing |
| `tsc` / `eslint` / `vitest` / `next build` | clean · 0 errors (12 warnings) · 317 passing · succeeds |
| `node scripts/check-consistency.mjs` | exit 1 — 170 baselined + **33 new** |
| `Deploy → dev` | **green**, `/health` smoke passing, most recently 2026-08-24 |

**The dev environment is live.** The "dev App Services are STOPPED" banner in
`docs/test-plans/T-0153-e2e-walk.md` is dated 2026-07-20 and is stale — the deploy
pipeline has run green many times since.

**Production is not live and cannot be.** `main.bicep` gates the Postgres firewall
rule to `envSlug == 'dev'` and there is no VNet or private endpoint anywhere in the
IaC, so prod App Services have no network path to the database. `/health` is
deliberately dependency-free, so a prod deploy would report **green** while every
real request 500s.

**Dev cannot validate the payment path.** `envSlug == 'dev'` enables the
`DevPaymentProvider` bypass, which mints a synthetic session and marks the order
paid through `MarkOrderPaid` without any gateway call or webhook. Good for walking
checkout; it means the Comgate webhook is exercised for the first time in
production unless someone deliberately points a non-prod environment at the sandbox.

---

## Critical path

> **A customer can place and pay for an order, end to end, in a deployed environment.**

The application code for this is complete and traced: `CreateOrder` →
`CreatePaymentSession` → `ComgatePaymentProvider` → customer redirect →
`ComgateWebhookController` → `MarkOrderPaid`. What blocks it is configuration and
network, not features.

| # | Item | Size | State |
|---|---|---|---|
| 1 | `UseForwardedHeaders` wired on all four hosts | M | 🔍 **in review — PR #168**, not merged |
| 2 | `Comgate__WebhookAllowedIps` / `Comgate__BaseUrl` settable per environment | S | 🔍 **in review — PR #168** (plumbing only) |
| 3 | Supply Comgate's published IP ranges as a GitHub secret | S | ⬜ **operator** |
| 4 | Point a non-prod environment at Comgate's sandbox before any real walk | S | ⬜ **operator** |
| 5 | Run T-0153 phases 2+ (the walk itself — verification, not a build ticket) | M | ⬜ |
| 6 | Payment-status reconciliation job for orders stuck in `PendingPayment` | M | ⬜ |

Items 3–4 are operator actions this repo cannot perform. Comgate's ranges are
deliberately **not** hardcoded: a guessed range is fail-closed and silently breaks
the only route an order has to `Paid`.

> **Operator gates live in [`docs/launch-checklist.md`](../launch-checklist.md), not
> here.** That file is already the named home for "blocking pre-launch items that
> only the operator can resolve", and several of these rows exist there. Items 3–4,
> P2 and the `ALERT_EMAIL` row are restated here only for sequencing context —
> the checklist is authoritative, and duplicating gates across two PM-owned files
> is the same drift class this tracker exists to repair.

Item 6 matters because the webhook is the *sole* production route to `Paid` while
`CancelExpiredPendingPaymentOrdersFunction` actively cancels unpaid orders — a
missed callback becomes a cancelled paid order.

### Production cutover — additional, and strictly ordered

| # | Item | Size | Note |
|---|---|---|---|
| P1 | Prod Postgres network path — VNet + delegated subnet + private endpoint | L | Migrations already have a break-glass path; this is App Service runtime only |
| P2 | Custom domain + DNS + TLS binding | M | **Must precede the first prod deploy.** `publicWebBaseUrl` and `jwtIssuer` are already `https://makables.cz`, and `PublicAppUrlsOptionsValidator` runs at startup — deploying first mints JWTs with an issuer nothing serves and sends emails linking to a host that does not resolve. Those emails are unrecallable |
| P3 | First-admin bootstrap | M | `Register.cs` rejects `UserRole.Admin`; the seeder hard-refuses any target containing `prod` |
| P4 | Gate the public catalog on `Maker.IsVerified` | S | Becomes load-bearing in prod, which has no pre-verified seeded makers |
| P5 | Go-live data runbook | M | A fresh prod DB has reference data but zero makers/products. Chain: bootstrap admin → maker registers → ARES → admin verifies → maker creates product. Contains an external party; cannot be compressed into a deploy window |
| P6 | Approved VOP / GDPR legal text | M | **Blocked on counsel** — Q-0030. Scope is `/vop` + `/gdpr` only; `/kontakt` already ships real operator identity |

⚠️ **There is no staging environment.** `deploy-staging.yml` deploys *dev*; only two
bicepparam files exist. Dev differs from prod in exactly the dimensions a launch
rehearsal would test (blanket firewall rule, data seeder, `*.azurewebsites.net`
hostnames). Either add a prod-shaped third environment or make the
accept-the-risk decision explicit, with a written rollback runbook.

---

## Lanes off the critical path

Counts are **distinct work items after de-duplication**. Six parallel auditors
produced 85 overlapping candidates that reduce to roughly 45 — five separate rows
all edited `PacketaShippingCarrier.cs:112`, six all wired the same script, four all
edited the same lines across the four `Program.cs`.

That arithmetic is not reproducible from this repo: the audit ran as a transient
workflow and its raw output was never written to `docs/audits/`. Doing so is itself
a Documentation lane item, and until it lands, treat the counts below as the
summary they are — the individual rows are each independently verified against
source, the totals are not auditable.

### Security & correctness (10)

| Item | Size | Note |
|---|---|---|
| Rotate + untrack the Google API key in `.mcp.json` | S | **In git history — treat as permanently compromised.** Needs a human at the Google console |
| Gate `MapOpenApi()` out of Production | S | Full admin contract currently served anonymously. Recon exposure, not authz bypass — every admin controller has `[Authorize]` |
| Global exception handler + ProblemDetails | M | None exists on any host; faults surface as bodyless connection failures the frontend's `Result<T, ApiError>` cannot parse. Should land **before** the T-0153 walk, or the walk cannot record what failed |
| Send real `Product.WeightGrams` to Packeta | S | Hardcoded `1.0` kg on both legs; the justifying comment is provably stale |
| `UseHttpsRedirection` + HSTS, and security response headers on both stacks | M | Split from the forwarded-headers work deliberately — HTTPS redirect behind a TLS terminator is a redirect-loop footgun |
| Blob soft-delete + versioning + non-LRS for invoices | S | |
| Fail the prod deploy when `ALERT_EMAIL` is unset | S | Currently empty-defaults, so prod ships with **zero alerting** |
| Untrack `.claude/settings.local.json` | S | Gitignored but tracked; contains no credential |
| Reactivation paths for soft-deleted entities (T-0180) | M | Marked ready, unbuilt |
| `cs-CZ` key for `order.refusalWindowExpired` | S | A maker hits an untranslated error today |

### Catalog read-side — **serial, not parallel** (3)

`IsVerified` filter → product publish gate → public product list/search.

Being precise about why, because the obvious rationale is wrong: `CatalogQueries.cs`
has **no shared base query**. It holds three independent expressions over two roots
— `GetPagedMakersAsync` and `GetMakerBySlugAsync` over `Maker`, `GetProductByIdAsync`
over `Product`. The `IsVerified` predicate lands on the Maker queries, the publish
gate on the Product query, and only product search adds a method, so only it touches
`ICatalogQueries` and `CatalogController`.

The serialisation still holds on two narrower grounds: all three edit the same file
(merge conflicts), and product search regenerates the NSwag client, which the
pre-commit hook forbids anyone from hand-merging. Order also matters semantically —
shipping search before the two predicates exist means shipping an endpoint that
surfaces unverified makers and unpublished drafts, then reworking it twice.

### Quality gate (1 chain, strictly ordered)

`check-consistency.mjs` is wired into neither CI nor the pre-commit hook, while
three process documents claim it runs on every PR.

It cannot simply be wired. The 33 new findings are **23 × T1, 7 × T5, 2 × T3,
1 × T8**, and exactly one is `hard:true`: the T8 at `BusinessErrorMessage.cs:54`,
`order.refusalWindowExpired` with no cs-CZ key — the same item listed in Security &
correctness above. `--update-baseline` deliberately cannot silence a hard finding,
so one is enough to keep the gate red, and landing it red trains the team to bypass
it.

Order: fix that single hard finding → triage the 23 T1s → re-baseline → wire the
gate in, same PR. The T1s are **not** the previously-fixed false positive: the sole
"must declare a public static class wrapper" finding is
`Features/Admin/RevenueReportingTimeZone.cs`, which genuinely declares `internal
static class`. The call there is whether to allow `internal` or move non-use-case
helpers out of `Features/` — not a detector bug.

### Frontend gaps (5)

Maker dispute open + return-receipt UI (endpoints shipped, zero callers); admin
product moderation; address-autocomplete endpoint with no caller; and the money-path
behavioural test gap — 317 frontend tests, but the checkout money path, maker
fulfilment actions and the admin console have no functional coverage.

### Documentation re-baseline (5)

Reconcile `INDEX.md` with merged code — the six missing rows (T-0191–T-0196, of
which T-0193 and T-0196 have no ticket file either), the 10 IDs with a row but no
file, the 12 duplicate rows, and the front-matter that never followed the INDEX.
Add a sprint record covering the Phase 7–8 work (sprint numbers and phase numbers
are unrelated in this repo and merely collide — `sprint-7.md` exists but documents
*Phase 4*). Collapse the two divergent process-doc trees. Refresh `docs/README.md`,
which still claims "138 of 146 tickets are `done`". Write this audit into
`docs/audits/`, whose own trigger (Phases 4 and 5 shipped) passed long ago.

### Deferred to v1.1 (7)

T-0142 Stripe Connect (**XL — must split into the four ADR-0027 slices**; blocked on
Q-0036), T-0143 invoicing in the maker's name (blocked on tax advisor), T-0148 SLA
timers, T-0149 cart, T-0150 quote calculator, T-0151 newsletter, T-0163
maker-proposed categories.

---

## Decisions owed by humans

Engineering cannot schedule around these.

| Q | Subject | Owner | Blocks |
|---|---|---|---|
| Q-0030 | Approved VOP + GDPR text | legal counsel | `/vop` and `/gdpr` only — both still placeholder shells. **`/kontakt` is NOT blocked**: its placeholder-lock was released and it renders verified operator identity (IČO 29633443) |
| Q-0036 | Stripe fees / hold mechanics / CZ sole-trader KYC / **ČNB registration duty** | Stripe + counsel | T-0142, i.e. the entire payments rebuild |
| dopady §5.3 | Tax advisor — invoice-number series, VAT wording, 2M CZK threshold | tax advisor | T-0143 |
| dopady §5.1 | "ship within 24h of *what*?" | business | T-0148 |
| dopady §5.5 | MVP vs v1.1 for cart / calculator / newsletter | business | T-0149–T-0151 |
| — | Comgate's published webhook IP ranges + sandbox base URL | operator | Critical-path items 3–4 |

**24 of the register's 41 entries are `open`.** Only 9 entries anywhere carry both
Owner and Resolve-by, and of the 24 open ones only **5** do (Q-0034, Q-0035,
Q-0036, Q-0038, Q-0039) — against the file's own rule that a question with no
Resolve-by "is not allowed to stay `open`".

---

## Status log

**2026-08-31 — plan opened against `8cf9992`.**

Derived from a two-stage audit: 6 parallel gap auditors + 2 reconcilers (adversarial
refutation and delivery sequencing), all reading source on this baseline.

**2026-08-31 — first lane merged to review: PR #168** (`feat/lane-money-path-dev`).

`UseForwardedHeaders` wired first in `UseMakablesPipeline` with `ForwardLimit = 1` as
the anti-spoofing control; `/health` and `MapGet("/")` excluded from the OpenAPI
document and all four clients regenerated; `Comgate__BaseUrl` and indexed
`Comgate__WebhookAllowedIps__N` made settable per environment.

Verification (own runs): build 0/0; **2,283 unit + 369 integration** passing;
`generate:api` idempotent and `check:api` green on all four hosts; tsc / eslint /
vitest (317) / next build clean; `check-consistency` unchanged at 33.
Bicep lint was `DEFERRED-TO-CI` (no `az`/`bicep` locally) and **passed in CI** along
with all four jobs.

Review changed the outcome. Both charters requested changes:

- **Blocker:** both new Bicep parameters were **dead**. `.bicepparam` reads them via
  `readEnvironmentVariable()`, evaluated on the runner, and neither deploy workflow
  forwarded them — setting the secret would have done nothing, silently, with the
  symptom being 401 on every real payment. Fixed, plus **`check-consistency` rule
  T10** so the bug class cannot recur.
- The anti-spoofing control had **no test**: every case sent a single-entry header,
  so a one-line `ForwardLimit = 2` would have opened IP forging with all tests green.
- A claim in the change was **false**: forwarded headers do *not* fix anonymous rate
  limiting for browser traffic, which reaches the API through the frontend
  `/api-proxy` rewrite and still collapses to the frontend egress IP. Corrected;
  **Q-0039 stays open** with a note.
- `XForwardedProto` was dropped — it would have flipped `Request.Scheme`, which the
  OAuth `redirect_uri` is exact-match-verified against.
- Docs that contradicted the code were corrected rather than left: security-rules
  **S5** (its narrow-`KnownNetworks` demand is not implementable on App Service),
  the launch checklist, `webhook-verification.md`, `env-vars.md`.

Every new guard was verified by deliberate regression — each was confirmed to fail
when the defect it guards is reintroduced.

**2026-08-31 — correction: the first attempt at this plan was withdrawn.**

An earlier audit and an earlier PR (#167) were produced against a local clone that
was **204 commits stale** (`79383cc`, 2026-07-15) because nobody fetched. Recorded
here because the failure is instructive and the artifacts are gone:

- Most headline findings were already fixed upstream — the missing `VerifyMaker`
  endpoint, the absent cookie→JWT bridge, the broken catalog category filter and a
  red `NU1903` build were all resolved between 2026-07-15 and 2026-08-24.
- Worse, that PR regenerated the NSwag clients from the stale backend, which would
  have **deleted 23 admin client methods** plus comparable surface on the other
  three. It was closed unmerged.
- A clean working tree says nothing about whether the branch is current. This repo
  is developed from more than one machine, so it falls behind silently and by a lot.

**Standing rule:** `git fetch` and confirm `master == origin/master` before any
analysis, and state the head SHA in the output. `gh run list` is the cheap
cross-check — CI activity newer than the local head is the tell.
