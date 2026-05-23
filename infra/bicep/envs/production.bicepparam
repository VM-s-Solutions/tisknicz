using '../main.bicep'

// Production environment — same template as staging but with higher SKUs
// and the production domain as the only CORS origin.

param envSlug = 'prod'
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
@secure()
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
