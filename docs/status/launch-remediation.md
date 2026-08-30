# Launch remediation — execution tracker

**Opened:** 2026-08-30
**Baseline commit:** `79383cc` (master, clean tree, last commit 2026-07-15)
**Owner:** PM
**Status:** in progress

> **This file supersedes ticket metadata for planning — temporarily and by
> design.** `docs/tickets/INDEX.md` and the per-ticket `status:` frontmatter both
> drifted after 2026-06-10: 40 tickets marked `draft`/`ready`/`in_progress` are
> in fact fully merged, and several marked `done` are not reachable over HTTP.
> Every task below was verified against source, not against a ticket.
>
> **This is a time-boxed exception, not a parallel backlog.** Holding sized,
> owned work items outside `docs/tickets/` is the very drift this plan exists to
> repair, so it must not outlive the repair. The Docs lane re-baselines ticket
> state; as each lane closes, its rows resolve into `docs/tickets/INDEX.md` and
> are struck here. What stays in this file permanently is the gate narrative and
> the verification log below — which is what `docs/status/` is for.

---

## How this effort runs

Decided with the user 2026-08-30:

| Dimension | Choice |
|---|---|
| Execution | Direct implementation, spawning reviewer / qa / secops subagents per the repo's quality gates |
| Checkpoints | One approval checkpoint per gate or lane, not per task |
| Git | One branch + PR per lane (e.g. `feat/gate-0-build-unblock`), not one per task |

Gate 8 discipline applies: every lane records its **own** verification run below
— command, exit code, and test counts. A developer-reported pass with no
recorded run does not close a lane.

---

## Origin

Derived from a two-stage audit run 2026-08-30 against `79383cc`:

1. **Teardown** — 14 parallel subsystem surveys reading source directly, then 4
   adversarial cross-checks (rule compliance, code-vs-docs drift, launch
   readiness, completeness critique). 18 agents.
2. **Gap audit** — 6 parallel gap auditors, then 2 reconcilers (adversarial
   refutation + delivery sequencing). 8 agents. 126 candidate tasks; 2 refuted,
   9 duplicate pairs merged, 4 sequencing prerequisites added → **~118 tasks**.

Findings still owe a write-up into `docs/audits/` per that folder's own
(subsystem × dimension) template — tracked in the Docs lane below.

---

## Critical path

The goal that defines "done" for the critical path:

> **A customer can place and pay for an order, end to end, in a deployed environment.**

Serial for the first two gates, then four independent lanes that must all close.

```
Gate 0 (build)  →  Gate 1 (contract)  →  ┬─ Lane A  auth
                                          ├─ Lane B  maker supply
                                          ├─ Lane C  demand & discovery
                                          └─ Lane D  deployed environment
                                                   ↓
                                          Email templates
```

---

## Gate 0 — build unblock ✅ DONE

**Branch:** `feat/gate-0-build-unblock` · **Completed:** 2026-08-30

Blocked 100% of the other work: `dotnet restore` exited 1 with 6 × `NU1903`,
and CI's `backend` job, the `api-parity` job (`needs: [backend]`) and the
`migrate` job in *both* deploy workflows all begin with that restore.

Root cause was time-decay, not a bad edit: `Directory.Build.props:8` sets
`TreatWarningsAsErrors=true` with `WarningsNotAsErrors` deliberately empty, so
NuGet audit advisories become hard restore errors.

| # | Task | Resolution |
|---|---|---|
| 1 | `System.Security.Cryptography.Xml` 10.0.8 flagged by 5 advisories | → 10.0.11 |
| 2 | `SSH.NET` 2023.0.0 (GHSA-q939-rpr3-3284) transitive from Testcontainers | Bumped `Testcontainers.PostgreSql` 4.0.0 → 4.14.0 so the fix comes from upstream rather than a transitive override |
| 3 | Fallout: Testcontainers 4.14 obsoletes the parameterless `PostgreSqlBuilder()`, and `TreatWarningsAsErrors` promoted CS0618 to an error | Image moved into the constructor in `PostgresHarness.cs`; same pinned `postgres:16-alpine` |

**Verification (own run, 2026-08-30):**

| Command | Exit | Result |
|---|---|---|
| `dotnet restore Makables.Api.slnx` | 0 | 16/16 projects, zero `NU1903` |
| `dotnet build Makables.Api.slnx -c Release` | 0 | 0 warnings, 0 errors |
| `dotnet test Makables.Api.slnx -c Release --no-build` | 0 | **1,837 unit + 268 integration passed**, 0 failed, 0 skipped |

Note: the advisory set will keep accumulating against a `TreatWarningsAsErrors`
build while the repo is idle. This exact failure recurs unless dependency
updates are automated — see `NEW-dependency-update-automation` in the Quality lane.

---

## Gate 1 — contract parity ✅ DONE

**Branch:** `feat/gate-0-build-unblock` (same PR) · **Completed:** 2026-08-30

Masked by Gate 0. Commit `f284f62` (2026-07-10) added `MapGet("/health")` to all
four hosts and touched zero client files. Minimal-API endpoints *do* enter the
OpenAPI document, so all four committed spec hashes were stale — meaning the
moment Gate 0 cleared, **every PR would have gone red on `api-parity`**.

| # | Task | Resolution |
|---|---|---|
| 4 | Ops endpoints described in the OpenAPI document | Added `.ExcludeFromDescription()` to `/health` and `MapGet("/")` on all four hosts. `grep ExcludeFromDescription backend/src` previously returned zero hits — ops endpoints were permanently coupled to the frontend contract. This decouples them for good, and stops the planned `/health/ready` endpoint breaking the gate a second time. |
| 5 | Stale generated clients + `.spec-hashes.json` | Regenerated all four via `npm run generate:api` against locally-run hosts using CI's exact env |
| 6 | **Guard** — nothing stopped the next ops endpoint repeating this | `OpsEndpointsExcludedFromContractTests` asserts every described path on every host starts with `/api/`, plus that `/health` stays *reachable* while undescribed. Asserted as a prefix rule, not a deny-list of known ops paths, so it catches endpoints nobody remembered to list |
| 7 | **Guard** — CI's parity failure message pointed the wrong way | `check-api-parity.mjs` told the developer to regenerate and commit, which *bakes the ops endpoint into the contract* — the opposite of the fix. Message now branches: regenerate for a genuine contract change, `.ExcludeFromDescription()` for an ops endpoint |

**Client surface change:** exactly one method removed per host — `anonymous()`,
the generated binding for `MapGet("/")`. Verified by diffing the method surface
before/after: 1 removed, 0 added, on each of the four clients. No call site
existed (`grep` outside `lib/api-client/` → 0 hits). The residual diff is
**declaration reordering**, not formatting — an order-independent line diff
shows 0 lines added and 43 removed per client, i.e. the new client is a strict
content subset. NSwag emits DTO declarations in controller-discovery order.

**Verification (own run, 2026-08-30):**

| Command | Exit | Result |
|---|---|---|
| `/health` occurrences in each live spec | — | 0 on all four hosts |
| `npm run generate:api` | 0 | 4 regenerated, 0 skipped |
| `npm run check:api` | 0 | all four hashes match the live specs |
| `dotnet test` (full, after guard) | 0 | **1,837 unit + 273 integration passed** (+5 from the new guard) |
| Guard negative test | — | Removed `.ExcludeFromDescription()` from the Public host → `Public_Host_Describes_Only_Api_Paths` **failed**, other 4 passed. Restored; suite green. The guard demonstrably catches the regression |
| Regen idempotency | — | Ran `generate:api` a second time; all five client artefacts **byte-identical** (md5). Partially closes the ADR 0022 idempotency question, which CI never checks (logged as Q3) |
| `npx tsc --noEmit` | 0 | clean |
| `npx eslint` | 0 | clean |
| `npx vitest run` | 0 | 14 files / 64 tests passed |
| `npx next build` | 0 | compiled successfully |

---

## Lane A — authentication ⬜ NOT STARTED

Nothing authenticated works today. The backend registers `AddJwtBearer` with
token-validation parameters only — no `OnMessageReceived`, no `JwtBearerEvents`
— and never reads the `makables_access_*` cookie. The frontend never sets an
`Authorization` header: `api-fetch.ts` guards on an `accessToken` option that no
call site supplies.

This is not "the SPA can't log in". `objednavka/page.tsx:56` SSR-calls
`getMyProfile`, gets 401, and **redirects checkout to `/login`**. Checkout is
dead, not degraded.

| # | Task | Size | Owner |
|---|---|---|---|
| A1 | Read the `makables_access_*` cookie in `AddMakablesAuth` (`OnMessageReceived`) | M | dotnet-backend |
| A2 | Wire the access token into `apiFetch` + 401 → refresh → retry | M | frontend |
| A3 | **Decide the cookie site boundary** — cookies are `SameSite=Strict`; in prod the frontend is `web-makables-*.azurewebsites.net` and the APIs are `app-makables-*.azurewebsites.net`, which are *different sites* under the public-suffix list | M | secops |
| A4 | Integration test exercising the **cookie** path | S | qa |
| A5 | Maker login on `/login` — `login-form.tsx:40` hardcodes `login('customer', …)` | M | frontend |

⚠️ **A3 is the trap.** Fixing only A1 makes auth work on localhost and fail in
every deployed environment — and fail *partially*: SSR pages render
authenticated while client-side calls 401. Solve A1 and A3 together.

⚠️ **A4 must exist before A1 is trusted.** Every current integration test
authenticates with an explicit `Bearer` header; the cookie seam has never been
exercised, which is precisely why this shipped broken.

---

## Lane B — maker supply ⬜ NOT STARTED

`CreateOrder` refuses orders from unverified makers; makers are created
unverified; the only caller of `MarkVerified()` is a handler with **no HTTP
endpoint**. Today a maker can only be verified by raw SQL against production
Postgres — which has no standing network path in (see Lane D).

| # | Task | Size | Owner |
|---|---|---|---|
| B1 | **First-admin bootstrap** — `Register.cs:80` rejects `UserRole.Admin`, no seed exists. *True head of this lane* | M | dotnet-backend |
| B2 | Admin maker **list + detail** read queries + GET endpoints — none exist, so verification has no discovery surface | M | dotnet-backend |
| B3 | HTTP endpoints for `VerifyMaker` / `DeactivateMaker` | S | dotnet-backend |
| B4 | Admin maker list + verify / deactivate UI | M | frontend |
| B5 | `RefreshMakerFromAres` endpoint (handler is dead code) | S | dotnet-backend |
| B6 | Staging seed fixture — verified maker + published product + customer | M | dotnet-backend |

B6 has a chicken-and-egg with B1/B3 and is a hard precondition for the k6 load
run and any smoke-testable environment. Ship it alongside B1.

---

## Lane C — demand & discovery ⬜ NOT STARTED

| # | Task | Size | Owner |
|---|---|---|---|
| C1 | **Fix the category slug/id mismatch** — highest value-to-effort on the whole plan | S | frontend |
| C2 | Gate the public catalog on `Maker.IsVerified` — it appears only in projections, never a WHERE clause | M | dotnet-backend |
| C3 | Public product list query + `GET /api/v1/catalog/products` | M | dotnet-backend |
| C4 | Public product listing page | M | frontend |
| C5 | Categories read query + public/admin endpoint | M | dotnet-backend |
| C6 | Admin categories CRUD page | M | frontend |
| C7 | Product draft/published gate — every product is live on creation | M | dotnet-backend |

**C1 detail.** Migration `20260527211229_Categories.cs:87` seeds
`id='cat-3d-tisk', slug='3d-tisk'`. `frontend/src/lib/catalog/categories.ts:23`
sends `'cat-3d-tisk'` **as the slug**. `CatalogQueries.cs:50` matches
`c.Slug == slug` → `PagedData.Empty`. Every category click on the storefront
returns an empty page, silently — no error, no log. Nothing in the backlog
covers it. Fix C1 before C5/C6 or the id-vs-slug confusion gets encoded into the
new data-driven path.

**C2/C7 together** are the trust model: today any self-registered maker is
publicly listed and orderable the moment they confirm their email, and every
product is live on creation.

---

## Lane D — deployed environment ⬜ NOT STARTED

| # | Task | Size | Owner |
|---|---|---|---|
| D1 | Execute the `infra-migration-2026-07` operator prerequisites (RG, OIDC role grants, secrets, what-if) | M | **operator** |
| D2a | Prod Postgres reachability — flip `allowAllAzureServices` on for prod (`main.bicep:150`) | S | secops |
| D2b | Prod Postgres hardening — VNet + delegated subnet + private endpoint + private DNS | L | secops |
| D3 | `UseForwardedHeaders` (App Service front end), with a regression test | M | dotnet-backend |
| D4 | `Comgate__WebhookAllowedIps` app settings for both envs | S | secops |
| D5 | Set `postgresLocation` in prod params (dev is pinned to `northeurope`) | S | secops |
| D6 | Prod frontend origin / custom domain in the CORS allowlists | S | operator |
| D7 | Blob soft-delete + versioning, and GRS for prod | S | secops |
| D8 | `/health/ready` that touches Postgres + blob + queue, probed by the smoke gate | M | dotnet-backend |

⚠️ **D3 before D4.** With no `UseForwardedHeaders` the allowlist sees the App
Service front-end IP, so populating it alone produces a webhook that still 401s
— and it reads as "wrong IP values" rather than "wrong IP source".

⚠️ **D8 matters more than it looks.** `/health` is deliberately dependency-free,
so the post-deploy smoke job would go green on a production where every real
request 500s. Today there is no VNet integration or private endpoint anywhere in
the Bicep and prod has no Postgres firewall rule — so a prod deploy would report
healthy and be entirely non-functional.

---

## Email ⬜ NOT STARTED

All 17 seeded templates carry `d-placeholder-*` SendGrid ids. There is no guard
before the API call, so SendGrid 400s, the error maps to Permanent, and the
outbox row stalls forever. `EmailTemplate.UpdateProviderTemplateId` has zero
production callers and no admin route — there is no in-product way to fix it.
The launch checklist never mentions authoring the templates.

| # | Task | Size | Owner |
|---|---|---|---|
| E1 | Email-template query + update command + admin `GET/PUT /api/v1/email-templates` | M | dotnet-backend |
| E2 | Admin UI to view and set the template ids | M | frontend |
| E3 | **Author the 17 SendGrid dynamic templates** | M | **operator** |

---

## Off the critical path

Full task detail lives in the audit run; summarised here so nothing is lost.

| Lane | Count | Highlights |
|---|---|---|
| **Quality & security** | ~20 | Rotate the Google API key committed in `.mcp.json` (in git history — treat as compromised); untrack `.claude/settings.local.json`; global exception handler + ProblemDetails; HSTS + HTTPS redirect; frontend security headers; **Packeta's hardcoded 1.0 kg** on both forward and return legs while `Product.WeightGrams` exists; the 6 missing Czech error keys; ~50 hardcoded Czech strings; the `(dynamic)` cast; two `useEffect` initial-data loads; `Web.*` → `Infra.*` project references; plus the five items below raised by the Gate 0/1 reviews |
| **Testing** | ~6 | Checkout money path, maker fulfilment actions, order tracking, admin console, `apiFetch` runtime, and **one cross-host E2E test** (register → login → verified maker → order → webhook → Paid) |
| **Docs re-baseline** | ~18 | Ticket states (**XL — split Phase 5–7 first**, ~50 rows); INDEX structural defects; collapse the two process-doc trees; four stale READMEs; archive `HANDOFF.md`; ADR status hygiene (0006 superseded, 0020 vs the shipped zip-deploy reality); scrub Vercel from 18 QA docs; backfill sprints 2–5 and 8+; write this audit into `docs/audits/` |
| **Human decisions** | ~8 | Q-0030 legal text + operator identity; Q-0036 Stripe fees / hold / KYC / **ČNB duty**; dopady §5.3 tax advisor; §5.1 SLA; §5.5 MVP-vs-v1.1 scope. 19 of 25 open questions lack the Owner + Resolve-by their own triage rule mandates |
| **v1.1** | ~10 | T-0142 Stripe Connect (**XL — split into the 4 ADR-0027 slices**), T-0148 SLA timers, T-0149 cart, T-0150 quote calculator, T-0151 newsletter |
| **Never-started small** | ~6 | T-0050 public maker reviews (profile always returns an empty list), T-0113 registry cache eviction, T-0114 retention cleanup, T-0123 migration-pipeline tests, T-0124 keyed email/registry adapters, Google OAuth HTTP routes (handlers exist, no route, ticket says `done`) |

### Raised by the Gate 0/1 reviews (2026-08-30)

Neither review found a defect in the Gate 0/1 change itself. These are
pre-existing items they surfaced while verifying it:

| # | Finding | Severity | Source |
|---|---|---|---|
| Q1 | **`/openapi/v1.json` is served anonymously in Production.** `app.MapOpenApi()` is unconditional on all four hosts and `ASPNETCORE_ENVIRONMENT=Production` in Azure, so an unauthenticated GET returns the full admin contract — every route and schema. Not an authz bypass (every admin controller carries `[Authorize]` and the audience table narrows the JWT), so this is recon exposure, not breach. ADR 0022 gates `/swagger` to dev/staging but is silent on the JSON — an ADR gap, not a violation. Fix: gate `MapOpenApi()` to non-Production and record it in ADR 0022 | Medium | secops |
| Q2 | **The parity hash is sensitive to `servers[0].url`.** `canonical-json.mjs` sorts keys but does not strip `servers`, so a runner binding a different hostname than `localhost` turns the gate red with a "regenerate the client" message that regenerating cannot fix — the same false-red class Gate 1 just removed. Deterministic today because CI pins `--urls http://localhost:<port>`. Fix: strip `servers` before hashing (changes all four hashes, so it needs its own regen + PR) | Low | secops |
| Q3 | **ADR 0022's idempotency compliance line is not implemented.** It requires CI to prove `npm run generate:api` produces an empty diff against the committed clients; `ci.yml` only runs `check:api` (hash parity) and never regenerates. So "the committed `.ts` is byte-identical to generator output" is asserted by nobody | Low | reviewer |
| Q4 | **`generate-api.mjs:21` documents a prettier pass that does not exist.** Prettier is neither a dependency nor invoked, which is why raw NSwag CRLF survives into the working tree. Either implement it or drop the claim | Low | reviewer |
| Q5 | **No `.gitattributes`.** Line-ending normalisation depends on each contributor's local `core.autocrlf`. Harmless on this machine (`autocrlf=true`); a contributor at the default `false` would commit CRLF and produce whole-file diffs on the generated clients | Low | reviewer |

⚠️ **Wiring `check-consistency.mjs` into CI is blocked, not schedulable.** It
currently reports 18 new findings, 6 of them `hard:true` T8 which
`--update-baseline` deliberately cannot silence. Correct order: 6 i18n keys →
5 inline error codes → fix the T1 false-positive regex (it flags files that *do*
declare `public static class`, polluting ~108 baseline rows) → re-baseline →
then wire it in.

---

## Status log

- **2026-08-30** — Plan opened from the two-stage audit against `79383cc`.
  Gate 0 and Gate 1 implemented on `feat/gate-0-build-unblock`. Backend restore,
  build and 2,110 tests green; NSwag parity green on all four hosts; frontend
  tsc / eslint / vitest / next build all green. Lanes A–D and Email not started.
- **2026-08-30** — Reviewer and secops gates run in parallel on the Gate 0/1
  diff. **secops: APPROVE**, no regression introduced; it independently
  reproduced the `public-api.v1` parity hash byte-exact and confirmed SSH.NET now
  resolves to 2026.0.0. **reviewer: request changes** — one blocker: the PR fixed
  both instances of the ops-endpoint bug but shipped no guard against the next
  one, while `/health/ready` is already on this plan. Guard added
  (`OpsEndpointsExcludedFromContractTests` + the corrected parity message,
  rows 6–7 above) and verified by deliberate regression. Three minor findings
  also fixed: bare commit SHA in comments replaced with an ADR 0022 reference,
  the "formatting churn" claim corrected to "declaration reordering", and the
  scope of this file narrowed so it cannot become a parallel backlog. Five
  pre-existing findings the reviews surfaced are logged as Q1–Q5 in the Quality
  lane. **Escalated to the user: rotate the credential in `.mcp.json`** — HIGH,
  pre-existing, needs a human at the Google console.
