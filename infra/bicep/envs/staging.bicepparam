using '../main.bicep'

// Dev environment (resource group rg-makables-dev) — single-region West Europe,
// Burstable SKUs, narrow CORS allowlist pointing at the dev frontend URL.
// (This file is still named staging.bicepparam for path stability; the env it
// describes is the non-production "dev" environment, envSlug = 'dev'.)

param envSlug = 'dev'
param location = 'westeurope'

param postgresSku = 'Standard_B1ms'
param postgresSkuTier = 'Burstable'
param postgresStorageGb = 32

param appServicePlanSku = 'B1'

// Sourced from GitHub Actions secrets at deploy time — never commit real
// values. No fallback default for the password: missing secret aborts the
// deploy rather than silently provisioning with an empty password.
param postgresAdminUser = readEnvironmentVariable('POSTGRES_ADMIN_USER')
// Secureness is declared by @secure() on the param in main.bicep — decorators
// are not valid in a .bicepparam file (BCP130).
param postgresAdminPassword = readEnvironmentVariable('POSTGRES_ADMIN_PASSWORD')

param customerCorsOrigins = [
  'https://dev.makables.cz'
]
param makerCorsOrigins = [
  'https://dev.makables.cz'
]
param adminCorsOrigins = [
  'https://dev-admin.makables.cz'
]
param publicCorsOrigins = [
  'https://dev.makables.cz'
  'https://makables.cz'
]

// Per-env non-secret app config.
param publicWebBaseUrl = 'https://dev.makables.cz'
param jwtIssuer = 'https://dev.makables.cz'
param comgateMerchantId = readEnvironmentVariable('COMGATE_MERCHANT_ID')

// Application secrets — sourced from GitHub Actions secrets at deploy time;
// never committed. A missing secret aborts the deploy (fail-closed), exactly
// like the Postgres password above.
param jwtSigningKeyBase64 = readEnvironmentVariable('JWT_SIGNING_KEY_BASE64')
param sendGridApiKey = readEnvironmentVariable('SENDGRID_API_KEY')
param comgateSecret = readEnvironmentVariable('COMGATE_SECRET')
param packetaApiKey = readEnvironmentVariable('PACKETA_API_KEY')
param packetaPublicWidgetKey = readEnvironmentVariable('PACKETA_PUBLIC_WIDGET_KEY')
param mapboxAccessToken = readEnvironmentVariable('MAPBOX_ACCESS_TOKEN')
