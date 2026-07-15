using '../main.bicep'

// Dev environment (resource group rg-makables-weu-dev) — single-region West
// Europe, Burstable SKUs, narrow CORS allowlist pointing at the dev frontend.
// Naming follows the Cleansia/CAF convention: <type>-makables-<region>-<env>.
//
// SECRETS: only the Postgres admin pair is a Bicep parameter now. The
// application secrets (JWT signing key, SendGrid/Comgate/Packeta/Mapbox) are
// pushed straight into Key Vault by the deploy workflow's "Push external
// secrets" step and consumed by the hosts as Key Vault references (T-0134).

param envSlug = 'dev'
param region = 'weu'
param location = 'westeurope'

// Postgres goes in northeurope: this subscription is offer-restricted for
// Postgres Flexible Server in westeurope (LocationIsOfferRestricted). Everything
// else stays in westeurope. northeurope + francecentral are confirmed open;
// westeurope + germanywestcentral are blocked on this sub.
param postgresLocation = 'northeurope'

// Dev "modest bump" (2026-06-23): one shared plan hosts 4 web apps + Functions,
// so B1 (1 core / 1.75GB) was tight -> B2 (2 core / 3.5GB). Postgres B1ms -> B2s
// (still Burstable; B2s confirmed available in northeurope). Storage 32 -> 64GB.
// Stays single-instance / no HA — this is dev, not prod.
param postgresSku = 'Standard_B2s'
param postgresSkuTier = 'Burstable'
param postgresStorageGb = 64

param appServicePlanSku = 'B2'

// Sourced from GitHub Actions secrets at deploy time — never commit real
// values. No fallback default for the password: missing secret aborts the
// deploy rather than silently provisioning with an empty password.
param postgresAdminUser = readEnvironmentVariable('POSTGRES_ADMIN_USER')
// Secureness is declared by @secure() on the param in main.bicep — decorators
// are not valid in a .bicepparam file (BCP130).
param postgresAdminPassword = readEnvironmentVariable('POSTGRES_ADMIN_PASSWORD')

// Dev allowlists include the App Service DEFAULT hostname alongside the custom
// domain (Cleansia D3 pattern): until DNS is rebound, the frontend serves at
// web-makables-weu-dev.azurewebsites.net, and a browser Origin not in this list
// fails the CORS preflight even against a healthy backend.
param customerCorsOrigins = [
  'https://dev.makables.cz'
  'https://web-makables-weu-dev.azurewebsites.net'
]
param makerCorsOrigins = [
  'https://dev.makables.cz'
  'https://web-makables-weu-dev.azurewebsites.net'
]
param adminCorsOrigins = [
  'https://dev-admin.makables.cz'
  'https://web-makables-weu-dev.azurewebsites.net'
]
param publicCorsOrigins = [
  'https://dev.makables.cz'
  'https://makables.cz'
  'https://web-makables-weu-dev.azurewebsites.net'
]

// Per-env non-secret app config.
param publicWebBaseUrl = 'https://dev.makables.cz'
param jwtIssuer = 'https://dev.makables.cz'

// Ops alert email — optional (empty skips the alerts module). Set the
// ALERT_EMAIL GitHub secret to enable metric alerts in dev.
param alertEmail = readEnvironmentVariable('ALERT_EMAIL', '')
