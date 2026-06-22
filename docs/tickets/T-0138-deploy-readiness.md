---
id: T-0138
title: Close the 6 deploy-blockers so a staging deploy yields a working app
status: ready
size: M
owner:
created: 2026-06-22
updated: 2026-06-22
depends_on: [T-0016]
blocks: []
user_stories: []
adrs: [0020, 0023]
phase: 6
manual_steps: [set-github-deploy-secrets]
security_touching: true
layers: [infra, secops]
---

# T-0138 — Close the deploy-blockers (staging-first)

## Context

The Bicep IaC + CI/CD scaffolding from T-0016 exists and is well-built (main
orchestrator + 6 modules + per-env params + OIDC auth + staging/production
workflows). But a deploy today **succeeds at the `az` layer while producing a
non-working app**: two independent audits (verified against the code) found
**6 blockers**. This ticket fixes all 6 in code, **staging-first**, with no
live deploy and no real secret values in the repo (secrets flow from GitHub
Actions secrets at deploy time).

## Scope (the 6 blockers + a CI guard)

1. **No `makables` database** — `postgres.bicep` created only the server; the
   conn string targets `Database=makables`. → add the `databases/makables`
   resource (+ `require_secure_transport=on` server config; + DB-name output).
2. **App can't boot — missing `ValidateOnStart` settings + wrong CORS.** The 4
   Web hosts + the Functions host crash on startup because ~9 required keys
   (`Jwt:SigningKeyBase64`, `SendGrid:ApiKey`, `Comgate:MerchantId/Secret`,
   `Packeta:ApiKey/PublicWidgetKey`, `Mapbox:AccessToken`, `Jwt:Issuer`,
   `AzureBlobStorage:ServiceUri`) aren't injected, and CORS was set as **platform**
   CORS (which `AddMakablesCors` ignores) instead of the `Cors__AllowedOrigins__
   <audience>__N` config array the host reads (empty → fail-closed crash). → inject
   all of them; secrets as `@secure()` params sourced from GitHub secrets.
3. **Functions code never deployed.** Both workflows deployed only the 4 Web
   hosts — the Functions app shell was created but had no code, so every
   background job (outbox drain, auto-deliver, payout batch, expiry-cancel,
   shipment-sync) was dead. → add a `functions` deploy job (`Azure/functions-action`)
   to both workflows + inject the 5 timer `%schedule%` keys + 3 queue names +
   the outbox queue connection (the Functions storage account doubles as the
   queue store, ADR 0020).
4. **No EF migration step.** No startup migrator, no pipeline step → deployed
   app hits an empty schema; every query fails (and the CZ `CountryConfiguration`
   seed in the initial migration never lands). → add a `migrate` job to both
   workflows: `dotnet ef migrations script --idempotent` → apply via `psql`
   (temp runner-IP firewall rule on staging; prod note: private-endpoint /
   self-hosted-runner path). `backend` + `functions` jobs now `needs: [migrate]`.
5. **`payouts` blob container missing.** `blob.bicep` created 4 of the 5
   containers `BlobContainer.All` uses → the weekly payout CSV upload 404s. →
   add the private `payouts` container + a "keep in sync with BlobContainer.All"
   comment.
6. **Secrets wiring.** The above secrets are referenced by `@secure()` params in
   `main.bicep` + the modules, read in the `.bicepparam` via
   `readEnvironmentVariable(...)`, and passed from GitHub secrets in the bicep
   job's `env:` block. No secret value lives in the repo; a missing secret aborts
   the deploy (fail-closed).

**Plus a meta-gap:** the Bicep templates were never validated in CI. → add a
`bicep` CI job (`az bicep build` + `build-params`) so IaC type/reference errors
fail the PR, not the deploy.

## Alternatives considered

- **Startup migrator (`db.Database.Migrate()` in `Program.cs`)** instead of a
  pipeline step — rejected: 4 hosts racing to migrate the same DB on boot is a
  concurrency hazard, and a failed migration would crash-loop the app rather
  than fail one clearly-scoped deploy job. A single idempotent `migrate` job
  before the app deploy is safer and observable.
- **Secrets → Key Vault references now** — deferred (already a launch-checklist
  item). This ticket gets the app *booting* via direct `@secure()` app settings;
  the KV-reference cut-over is a separate hardening pass (the secrets flow the
  same way, just relocated). Keeping it out keeps this bundle scoped + avoids the
  KV-identity ordering cycle the `main.bicep` TODO documents.
- **Frontend to Azure Static Web Apps** instead of Vercel — out of scope; the
  Vercel split is the existing, intentional choice.

## Out of scope (stays on the launch-checklist, NOT this ticket)

- Production hardening: KV secret references, `AzureWebJobsStorage` identity,
  Postgres Private Endpoint, Blob GRS + 30-day soft-delete, slot-based rollback.
- The actual **live deploy** (operator-led: `az login`, RG creation, setting the
  GitHub deploy secrets, the manual pre-launch RUNs).
- Frontend `NEXT_PUBLIC_*` env vars (set in Vercel project settings —
  launch-checklist item).

## Acceptance criteria

- **Given** the Bicep templates, **when** `az bicep build main.bicep` runs (the
  new CI job), **then** it compiles with no errors and both `.bicepparam` files
  build.
- **Given** a staging deploy, **when** it completes, **then** the `makables`
  database exists, the schema is migrated (idempotent), all 5 blob containers
  exist, the 4 Web hosts + the Functions app boot (no `ValidateOnStart` crash),
  and the Functions app has code with all timer schedules resolved.
- **Given** the repo, **then** no secret value is committed — every secret flows
  via `readEnvironmentVariable` ← GitHub Actions secret, and a missing one aborts
  the deploy.

## Files touched

- `infra/bicep/main.bicep`, `modules/{postgres,blob,app-service,functions}.bicep`,
  `envs/{staging,production}.bicepparam`
- `.github/workflows/{deploy-staging,deploy-production,ci}.yml`
- `docs/deployment/deploy-runbook.md` (new), `docs/launch-checklist.md`,
  `docs/deployment/env-vars.md`

## Manual deployment steps

Set these **GitHub Actions secrets** (per environment) before the first deploy —
the deploy fails loudly without them:
`AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`,
`POSTGRES_ADMIN_USER`, `POSTGRES_ADMIN_PASSWORD`, `JWT_SIGNING_KEY_BASE64`,
`SENDGRID_API_KEY`, `COMGATE_MERCHANT_ID`, `COMGATE_SECRET`, `PACKETA_API_KEY`,
`PACKETA_PUBLIC_WIDGET_KEY`, `MAPBOX_ACCESS_TOKEN`, `VERCEL_TOKEN`.
Full procedure in `docs/deployment/deploy-runbook.md`.

## Status log

- 2026-06-22 `draft → ready` by PM (deploy-readiness bundle, staging-first;
  user-locked: fix the 6 blockers in code, no live deploy, no real secrets).
