# `infra/bicep/` — Azure infrastructure as code

Per ADR 0023, every Makables environment is deployed via the Bicep
templates in this folder. The same `main.bicep` orchestrator is composed
twice — once per environment — with the env-specific parameters file
selecting SKUs and CORS origins.

## Layout

```
infra/bicep/
├── main.bicep                  # orchestrator (composes the modules)
├── envs/
│   ├── staging.bicepparam      # the DEV env (envSlug 'dev', rg-makables-dev); B1ms Postgres; B1 ASP
│   └── production.bicepparam   # West Europe; D2s_v3 Postgres; P1v3 ASP
└── modules/
    ├── app-insights.bicep      # Log Analytics workspace + AI component
    ├── app-service.bicep       # One App Service (called 4× per env, one per audience)
    ├── blob.bicep              # Storage account + 5 containers (incl. payouts)
    ├── functions.bicep         # Functions app + AzureWebJobsStorage account
    ├── key-vault.bicep         # RBAC-authorized Key Vault
    └── postgres.bicep          # Postgres Flexible Server + makables DB + SSL + firewall rule
```

> **Environment names:** the non-production environment is **`dev`** (resource
> group `rg-makables-dev`, resources `makables-dev-*`). The param file is still
> named `staging.bicepparam` for path stability, but it sets `envSlug = 'dev'`.

## Deploy locally (operator-led, requires `az` and elevated permissions)

```bash
# 1. Authenticate.
az login
az account set --subscription <subscription-id>

# 2. Resource group (pre-created by the operator; not in template).
az group create --name rg-makables-dev --location westeurope

# 3. Set the secrets the .bicepparam reads via readEnvironmentVariable() (each
#    aborts the deploy if missing). Full list: docs/deployment/deploy-runbook.md.
export POSTGRES_ADMIN_USER='makablesadmin'
export POSTGRES_ADMIN_PASSWORD='<strong-password>'
export JWT_SIGNING_KEY_BASE64='<base64, >=32 bytes>'
export SENDGRID_API_KEY='<...>'; export COMGATE_MERCHANT_ID='<...>'
export COMGATE_SECRET='<...>'; export PACKETA_API_KEY='<...>'
export PACKETA_PUBLIC_WIDGET_KEY='<...>'; export MAPBOX_ACCESS_TOKEN='<...>'

# 4. Deploy.
az deployment group create \
  --resource-group rg-makables-dev \
  --template-file main.bicep \
  --parameters envs/staging.bicepparam
```

## CI deploy

`.github/workflows/deploy-staging.yml` runs the same `az deployment` against
the `rg-makables-dev` resource group on every push to `master` (the dev env).
Production deploys are gated on a manual workflow dispatch
(`deploy-production.yml`). See `docs/deployment/deploy-runbook.md` for the full
operator setup.

## Out-of-band setup the templates do NOT cover

- DNS / custom domains (operator-led).
- TLS certificates (App Service managed certs after CNAMEs are in).
- Comgate / Resend / Packeta API credentials uploaded to Key Vault.
- Postgres user provisioning for the application (a follow-up `psql` step
  in the deploy pipeline once T-0020 has migrations to apply).
