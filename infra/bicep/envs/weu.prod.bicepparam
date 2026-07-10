using '../main.bicep'

// Production environment (resource group rg-makables-weu-prod) — same template
// as dev but with higher SKUs and the production domain as the only CORS origin.
// Naming follows the Cleansia/CAF convention: <type>-makables-<region>-<env>.
//
// SECRETS: only the Postgres admin pair is a Bicep parameter now. The
// application secrets are pushed into Key Vault by the deploy workflow's
// "Push external secrets" step and consumed as Key Vault references (T-0134).

param envSlug = 'prod'
param region = 'weu'
param location = 'westeurope'

// Per ADR 0023 §7: production runs General Purpose D2s_v3 Postgres and
// P1v3 App Service Plan. Burstable / P0v3 were a draft-time mistake that
// the T-0016 reviewer caught — both contradict the availability and
// CPU-alert assumptions in ADR 0023 §4.
param postgresSku = 'Standard_D2s_v3'
param postgresSkuTier = 'GeneralPurpose'
param postgresStorageGb = 64

param appServicePlanSku = 'P1v3'

// POSTGRES_ADMIN_USER / POSTGRES_ADMIN_PASSWORD come from GitHub Actions
// secrets at deploy time. There is intentionally NO fallback default for
// the password — readEnvironmentVariable without a default fails the
// deployment loudly if the secret is missing, which is what we want.
param postgresAdminUser = readEnvironmentVariable('POSTGRES_ADMIN_USER')
// Secureness is declared by @secure() on the param in main.bicep — decorators
// are not valid in a .bicepparam file (BCP130).
param postgresAdminPassword = readEnvironmentVariable('POSTGRES_ADMIN_PASSWORD')

param customerCorsOrigins = [
  'https://makables.cz'
]
param makerCorsOrigins = [
  'https://makables.cz'
]
param adminCorsOrigins = [
  'https://admin.makables.cz'
]
param publicCorsOrigins = [
  'https://makables.cz'
]

// Per-env non-secret app config.
param publicWebBaseUrl = 'https://makables.cz'
param jwtIssuer = 'https://makables.cz'

// Ops alert email — production should set the ALERT_EMAIL GitHub secret so
// Http5xx / latency / exceptions / Postgres alerts actually notify someone.
param alertEmail = readEnvironmentVariable('ALERT_EMAIL', '')
