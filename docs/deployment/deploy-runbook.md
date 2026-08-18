# Deploy runbook — dev (and production notes)

> Operator-facing. The CI/CD workflows (`.github/workflows/deploy-staging.yml`,
> `deploy-production.yml`) automate the deploy; this runbook covers the
> one-time setup the workflows assume, the order of operations, and how to
> verify a deploy actually produced a working app. Closes T-0138.
>
> **Naming note:** the non-production environment is **dev** — Azure resource
> group `rg-makables-weu-dev`, resources `<type>-makables-weu-dev` (Bicep `envSlug = 'dev'`),
> and the GitHub Actions *environment* is named **`dev`**. The only thing still
> carrying the old "staging" label is the workflow *filename* `deploy-staging.yml`
> (kept for path stability) and the `weu.dev.bicepparam` *filename* — both
> describe the dev env.

## What the pipeline does (per environment)

`bicep` → `migrate` → (`backend` ‖ `functions`) ‖ `frontend`

1. **bicep** — `az deployment group create` against the env's `.bicepparam`:
   Postgres (server + `makables` DB), Blob (5 containers), 4 App Services,
   Functions app, App Insights, Key Vault. Injects every boot-required app
   setting (incl. secrets, from GitHub Actions secrets).
2. **migrate** — generates an idempotent EF SQL script and applies it via
   `psql` so the schema (and the CZ `CountryConfiguration` seed) exists before
   any app boots.
3. **backend** — publishes + deploys the 4 .NET Web hosts.
4. **functions** — publishes + deploys the .NET-isolated Functions app (the
   background jobs).
5. **frontend** — builds the Next.js app (`output: 'standalone'`) and deploys it
   to its own Azure App Service (`makables-<env>-web`, Node, on the shared
   plan). Everything is on Azure — no Vercel.

## One-time operator setup (NOT in the templates)

### 1. Azure resource group + OIDC

```bash
az login
az account set --subscription <subscription-id>
az group create --name rg-makables-weu-dev --location westeurope   # prod: makables-prod
```

Configure an **OIDC federated credential** so GitHub Actions can `azure/login`
without a stored password: register an Entra app, grant it Contributor on the
resource group, and add a federated credential bound to this repo's
`dev` / `production` GitHub *environment* (subject e.g.
`repo:<org>/tisknicz:environment:dev`). (The workflows use `id-token: write` +
`azure/login` with `client-id`/`tenant-id`/`subscription-id`.)

### 2. GitHub Actions secrets (per environment)

The deploy **fails loudly** if any is missing (fail-closed). Set, in the
`dev` and `production` GitHub environments:

| Secret | Used by |
|---|---|
| `AZURE_CLIENT_ID` / `AZURE_TENANT_ID` / `AZURE_SUBSCRIPTION_ID` | `azure/login` (OIDC) |
| `POSTGRES_ADMIN_USER` / `POSTGRES_ADMIN_PASSWORD` | Bicep + migrate |
| `JWT_SIGNING_KEY_BASE64` | app boot (base64, ≥32 bytes) |
| `SENDGRID_API_KEY` | app + Functions boot |
| `COMGATE_MERCHANT_ID` / `COMGATE_SECRET` | app + Functions boot |
| `PACKETA_API_KEY` / `PACKETA_PUBLIC_WIDGET_KEY` | app + Functions boot |
| `MAPBOX_ACCESS_TOKEN` | app + Functions boot |

No secret value is in the repo — the `.bicepparam` files read these via
`readEnvironmentVariable(...)` and the bicep job passes them through `env:`.

(No `VERCEL_TOKEN` — the frontend deploys to Azure App Service, not Vercel.)

### 3. Frontend config (no operator step)

The frontend runs on its own Azure App Service (`makables-<env>-web`, Node). Its
`NEXT_PUBLIC_*` settings (`NEXT_PUBLIC_SITE_URL` + the four
`NEXT_PUBLIC_API_*_BASE_URL`) are injected by the Bicep `web-app` module,
pointing at the deployed API hosts' default hostnames — so there is no manual
env-var step. (To use the custom `dev.makables.cz` domain, map it on the web
App Service + set `NEXT_PUBLIC_SITE_URL` accordingly; until then the
`*.azurewebsites.net` hostname works.)

## Migration connectivity (the one non-obvious step)

The `migrate` job runs `psql` from the GitHub runner, which has a **public IP**
and is **not** an "Azure service", so the staging `allowAllAzureServices` rule
does not admit it. The job therefore opens a **temporary firewall rule for the
runner's IP**, applies the script, and removes the rule on exit (trap).

**Production** has no public firewall opening (Private Endpoint per the
launch-checklist). Options for the prod `migrate` job: (a) run it on a
**self-hosted runner inside the VNet**, or (b) a deliberate break-glass temp
firewall rule for the migration window. The prod workflow's `migrate` job uses
the temp-rule approach and is commented accordingly; switch to a self-hosted
runner once the VNet is wired.

## Deploy

- **Staging:** automatic on push to `master` (or `workflow_dispatch`).
- **Production:** `workflow_dispatch` only, gated by typing `PRODUCTION` into the
  confirm input.

## Verify a deploy actually worked (not just "az succeeded")

1. **Hosts booted:** `GET https://app-makables-customer-weu-dev.azurewebsites.net/`
   returns `Makables Customer API — alive.` (repeat per host). A boot crash =
   a missing app setting (check the App Service log stream for
   `OptionsValidationException`).
2. **DB schema present:** the migrate job is green and `__EFMigrationsHistory`
   has rows; a catalog read returns data, not a 500.
3. **CZ config seeded:** an order/pricing path works (the CZ
   `CountryConfiguration` row exists).
4. **Functions running:** the Functions app shows the 8 functions in the portal;
   `outbox_events/stalled/count` stays low; a placed order produces its emails.
5. **CORS:** the frontend can call the API (no CORS error in the browser
   console) — confirms the `Cors__AllowedOrigins__*` settings bound.

## Known deploy failures

**`AADSTS700024: Client assertion is not within its valid time range`** — the
GitHub OIDC token `azure/login` exchanges is a client assertion that lives
**5 minutes**. The Azure CLI replays that stored assertion whenever it needs a
token for an audience it has not used yet (Microsoft Graph, `vault.azure.net`),
so a Bicep deploy longer than 5 minutes leaves the session working for ARM and
dead for everything else. Fix is structural, already in both workflows: a fresh
`azure/login@v2` immediately before the Key Vault steps. **If you add a step
that touches a new Azure service after a long step, re-login first.**

**`ReadOnlyDisabledSubscription: The subscription is disabled and therefore
marked as read only`** — billing/subscription state, not a pipeline bug. Nothing
in the repo fixes it; re-enable the subscription in the Azure portal and re-run.

## Rollback

App code: redeploy the previous commit (the workflows are idempotent). Infra:
Bicep is declarative — re-run with the previous template. DB: forward-only
migrations; a bad migration needs a new corrective migration (see
`docs/runbooks/backup-restore.md` for point-in-time restore). Slot-based
blue-green rollback is a launch-checklist hardening item (not yet wired).

## Still out of scope (launch-checklist hardening)

Secrets → Key Vault references, `AzureWebJobsStorage` identity-based, Postgres
Private Endpoint, Blob GRS + 30-day soft-delete, App Service deployment slots.
See `docs/launch-checklist.md`.
