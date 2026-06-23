using '../main.bicep'

// Dev environment (resource group rg-makables-dev) — single-region West Europe,
// Burstable SKUs, narrow CORS allowlist pointing at the dev frontend URL.
// (This file is still named staging.bicepparam for path stability; the env it
// describes is the non-production "dev" environment, envSlug = 'dev'.)

param envSlug = 'dev'
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
