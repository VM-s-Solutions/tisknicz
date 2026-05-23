---
id: T-0016
title: Deploy — Bicep templates, staging+production GitHub Actions, Husky pre-commit
status: done
size: L
owner: secops
created: 2026-05-23
updated: 2026-05-23
depends_on: [T-0014]
blocks: []
adrs: [0023]
phase: 1
---

# T-0016 — Deploy

## Scope

### Bicep IaC (`infra/bicep/`)
- `main.bicep` — orchestrator. Composes `app-insights`, `postgres`, `blob`, four `app-service` invocations (one per audience), `functions`, and `key-vault`.
- `envs/staging.bicepparam` — staging parameters: `envSlug='stg'`, `Standard_B1ms` Postgres, `B1` ASP, narrow CORS to `stg.makables.cz` + `stg-admin.makables.cz`.
- `envs/production.bicepparam` — production parameters: `envSlug='prod'`, `Standard_B2s` Postgres, `P0v3` ASP, CORS limited to the production domain.
- `modules/postgres.bicep` — Postgres Flexible Server v16, 14-day backups, Entra auth enabled, AllowAllAzureServices firewall rule (private endpoints deferred to ops hardening per the runbook T-0134).
- `modules/app-service.bicep` — single audience-tagged App Service. `linuxFxVersion: DOTNETCORE|10.0`, HTTPS-only, TLS 1.2, system-assigned managed identity, App Insights + Postgres connection strings injected as app settings.
- `modules/functions.bicep` — dotnet-isolated 10.0 Functions app + a dedicated storage account for AzureWebJobsStorage.
- `modules/blob.bicep` — storage account with four containers (`product-images` public, `order-attachments`/`invoices`/`labels` private), per ADR 0011.
- `modules/key-vault.bicep` — RBAC-authorized Key Vault; the five managed identities (four App Services + Functions) receive the `Key Vault Secrets User` role.
- `modules/app-insights.bicep` — workspace-backed App Insights component, 30-day retention.
- `README.md` — operator-led local deploy commands + CI pointers + out-of-band setup checklist.

### GitHub Actions (`.github/workflows/`)
- `ci.yml` — runs on every push to `master` and every PR.
  - `backend` job: restore + build (Release) + `dotnet test` on the full solution.
  - `frontend` job: `npm ci` + `tsc --noEmit` + ESLint + `next build`.
  - `api-parity` job: starts all four backend hosts in the background, then runs `npm run check:api` (ADR 0022).
- `deploy-staging.yml` — runs on every push to `master` and on manual dispatch.
  - `bicep` job: `az deployment group create` against `makables-stg`.
  - `backend` job: matrix-deploys all four App Services from the just-published artifacts.
  - `frontend` job: builds + deploys to Vercel preview.
- `deploy-production.yml` — manual dispatch only; guarded by a `confirm: PRODUCTION` input.
  - Same three-job shape as staging, against `makables-prod`.

### Husky pre-commit hook
- `.husky/pre-commit` — runs `node frontend/scripts/check-api-client-manual-edits.mjs` (T-0013's deferred hook wiring).
- `frontend/package.json` — adds `"husky": "^9.1.7"` devDep and a `"prepare": "cd .. && husky .husky"` script so `npm install` in `frontend/` registers the hook.

### INDEX flips + ticket close-outs
- `docs/tickets/INDEX.md` — T-0013/T-0014/T-0015/T-0016 flipped to `done`.

### T-0015 reviewer follow-ups folded in
Reviewer of `500a0a9` returned BLOCKER × 2 + MAJOR × 4. Addressed here:
- **B1 (cookie-name footgun)** — `apiFetch`'s JSDoc on `accessToken` now documents the real contract: callers read the audience cookie themselves until T-0027 builds the cookie → Bearer bridge.
- **B2 (`/objednavka/*` missing from matcher)** — added a `// TODO(T-0084)` comment to the matcher list explaining the Phase-1 omission (Comgate-redirect confirmation must remain reachable unauthenticated).
- **M1 (signal composition)** — switched to `AbortSignal.any([options.signal, AbortSignal.timeout(8000)])` so caller signal + 8 s timer race correctly.
- **M3 (`_debugUrl` unsafe cast)** — dropped; `transientError` no longer takes a URL parameter.
- **M4 (ADR 0022 drift)** — updated ADR 0022's "Apifetch wrapper" section to match the shipped `(host, path, options)` shape; recorded that refresh-on-401 lives in T-0027.
- **MINOR / MISC** — fixed `auth.login.forgot_password` Czech to `Zapomněli jste heslo?`; added a doc-block on `Role` vs `Audience` divergence.

## Out of scope
- DNS configuration, custom domain wiring, TLS managed certs — operator-led.
- Comgate / Resend / Packeta API credential upload to Key Vault — operator-led.
- Postgres role provisioning for the application — depends on T-0020 migrations.
- Private endpoints for Postgres + Storage — tracked in runbook T-0134.
- Production secret-rotation runbook — T-0134.

## Acceptance criteria
- **AC-1** All Bicep templates parse (Bicep linter clean; `az bicep build` smoke-test deferred to CI on the first `deploy-staging.yml` run with secrets configured).
- **AC-2** GitHub Actions workflows are valid YAML; each job has explicit `timeout-minutes` and the production deploy is dispatch-only with the confirmation gate.
- **AC-3** Husky `pre-commit` invokes the manual-edits check that T-0013 introduced.
- **AC-4** INDEX.md flips for T-0013…T-0016 are committed.
- **AC-5** T-0015 reviewer BLOCKERs B1 and B2 closed; MAJORs M1/M3/M4 closed in code/ADR.

## Status log
- 2026-05-23 done. Bicep + workflows + husky + INDEX flips + T-0015 reviewer fixes folded in.
