using '../main.bicep'

// Staging environment — single-region West Europe, Burstable SKUs, narrow CORS
// allowlist pointing at the staging frontend URL.

param envSlug = 'stg'
param location = 'westeurope'

param postgresSku = 'Standard_B1ms'
param postgresSkuTier = 'Burstable'
param postgresStorageGb = 32

param appServicePlanSku = 'B1'

// Sourced from GitHub Actions secrets at deploy time — never commit real
// values. No fallback default for the password: missing secret aborts the
// deploy rather than silently provisioning with an empty password.
param postgresAdminUser = readEnvironmentVariable('POSTGRES_ADMIN_USER')
@secure()
param postgresAdminPassword = readEnvironmentVariable('POSTGRES_ADMIN_PASSWORD')

param customerCorsOrigins = [
  'https://stg.makables.cz'
]
param makerCorsOrigins = [
  'https://stg.makables.cz'
]
param adminCorsOrigins = [
  'https://stg-admin.makables.cz'
]
param publicCorsOrigins = [
  'https://stg.makables.cz'
  'https://makables.cz'
]

// Per-env non-secret app config.
param publicWebBaseUrl = 'https://stg.makables.cz'
param jwtIssuer = 'https://stg.makables.cz'
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
