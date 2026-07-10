# `infra/bicep/` — Azure infrastructure as code

Per ADR 0023, every Makables environment is deployed via the Bicep templates in
this folder. The same `main.bicep` orchestrator is composed twice — once per
environment — with the env-specific `.bicepparam` selecting SKUs and CORS origins.

## Naming (Cleansia/CAF convention)

`<type>-makables[-<audience>]-<region>-<env>`, region token `weu`; globally-unique
alphanumeric-only resources collapse the hyphens:

| Resource | dev | prod |
|---|---|---|
| Resource group | `rg-makables-weu-dev` | `rg-makables-weu-prod` |
| App Service Plan | `plan-makables-weu-dev` | `plan-makables-weu-prod` |
| API hosts | `app-makables-{customer,maker,admin,public}-weu-dev` | `…-weu-prod` |
| Frontend (Next.js) | `web-makables-weu-dev` | `web-makables-weu-prod` |
| Functions | `func-makables-weu-dev` | `func-makables-weu-prod` |
| Postgres | `pg-makables-weu-dev` (lives in `northeurope` — offer restriction) | `pg-makables-weu-prod` |
| Key Vault | `kv-makables-weu-dev` | `kv-makables-weu-prod` |
| Blob storage | `stmakablesweudev` | `stmakablesweuprod` |
| Functions storage | `stmakablesfnweudev` | `stmakablesfnweuprod` |
| App Insights / Log Analytics | `appi-makables-weu-dev` / `log-makables-weu-dev` | `…-weu-prod` |
| Action group (alerts) | `ag-makables-weu-dev` | `ag-makables-weu-prod` |

The region token is the deployment region, never a country — countries are
application data (`CountryConfiguration`), not infrastructure.

## Secrets model (T-0134)

1. Host secret app settings are `@Microsoft.KeyVault(SecretUri=...)` **references**
   resolved at runtime by each host's system-assigned managed identity
   (Key Vault Secrets User, granted in `modules/role-assignments.bicep`).
   No secret value is visible in the resource group.
2. Bicep writes only the **derivable** secrets (`modules/derived-secrets.bicep`):
   `ConnectionStrings--Postgres`, `Storage--ConnectionString`,
   `OutboxQueues--ConnectionString`, `Jwt--Issuer`, `Jwt--Audience`.
3. **External** secrets (`Jwt--SigningKeyBase64`, `SendGrid--ApiKey`,
   `Comgate--MerchantId/Secret`, `Packeta--ApiKey/PublicWidgetKey`,
   `Mapbox--AccessToken`) are pushed from GitHub Environment secrets by the
   deploy workflow's "Push external secrets to Key Vault" step.
4. `AzureWebJobsStorage` on the Functions host is **identity-based**
   (`__accountName` + `__credential=managedidentity`) — no account key at all.

The deploying principal MUST hold **User Access Administrator** (or Owner) on
the resource group — `role-assignments.bicep` creates RBAC role assignments,
which a plain Contributor cannot.

## Layout

```
infra/bicep/
├── main.bicep                    # orchestrator (names, kvRef composition, module wiring)
├── envs/
│   ├── weu.dev.bicepparam        # dev (rg-makables-weu-dev); Burstable Postgres; B2 plan
│   └── weu.prod.bicepparam       # prod (rg-makables-weu-prod); D2s_v3 Postgres; P1v3 plan
└── modules/
    ├── app-insights.bicep        # Log Analytics workspace + AI component
    ├── app-service.bicep         # One API App Service (called 4×; KV-ref settings; container logs ON)
    ├── alerts.bicep              # Action group + Http5xx/latency/exceptions/Postgres metric alerts
    ├── blob.bicep                # Storage account + 5 containers (incl. payouts)
    ├── derived-secrets.bicep     # Writes the Bicep-derivable Key Vault secrets
    ├── functions.bicep           # Functions app + storage (identity-based AzureWebJobsStorage)
    ├── key-vault.bicep           # RBAC-authorized Key Vault (secret NAMES only)
    ├── postgres.bicep            # Postgres Flexible Server + makables DB + SSL + firewall rule
    ├── role-assignments.bicep    # MI grants: KV Secrets User + Blob/Queue Data roles
    └── web-app.bicep             # Next.js SSR App Service (Node; container logs ON)
```

## Deploy locally (operator-led, requires `az` and User Access Administrator)

```bash
# 1. Authenticate.
az login
az account set --subscription <subscription-id>

# 2. Resource group (pre-created by the operator; not in template).
az group create --name rg-makables-weu-dev --location westeurope

# 3. Set the two secrets the .bicepparam reads via readEnvironmentVariable().
export POSTGRES_ADMIN_USER='makablesadmin'
export POSTGRES_ADMIN_PASSWORD='<strong-password>'   # alphanumeric-only (connection-string safe)

# 4. What-if first, then deploy.
az deployment group what-if --resource-group rg-makables-weu-dev \
  --template-file main.bicep --parameters envs/weu.dev.bicepparam
az deployment group create --resource-group rg-makables-weu-dev \
  --template-file main.bicep --parameters envs/weu.dev.bicepparam

# 5. Push the external secrets into Key Vault (the CI step does this same thing).
az keyvault secret set --vault-name kv-makables-weu-dev --name 'Jwt--SigningKeyBase64' --value '<base64 ≥32B>'
# … SendGrid--ApiKey, Comgate--MerchantId, Comgate--Secret, Packeta--ApiKey,
#   Packeta--PublicWidgetKey, Mapbox--AccessToken
```

## CI deploy

`.github/workflows/deploy-staging.yml` provisions + pushes secrets + migrates +
deploys against `rg-makables-weu-dev` on every push to `master`. Production is
gated on a manual, confirmation-guarded dispatch (`deploy-production.yml`).
See `docs/deployment/deploy-runbook.md` and
`docs/deployment/infra-migration-2026-07.md` for the operator setup.

## Out-of-band setup the templates do NOT cover

- Resource-group creation + the OIDC principal's Contributor + **User Access
  Administrator** grants (operator-led, once per env).
- GitHub Environment secrets (see the workflow env blocks for the full list).
- DNS / custom domains + TLS certificates (rebind after any rename).
- Postgres app-user provisioning beyond the admin account.
