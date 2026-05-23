using '../main.bicep'

// Staging environment — single-region West Europe, Burstable SKUs, narrow CORS
// allowlist pointing at the staging frontend URL.

param envSlug = 'stg'
param location = 'westeurope'

param postgresSku = 'Standard_B1ms'
param postgresSkuTier = 'Burstable'
param postgresStorageGb = 32

param appServicePlanSku = 'B1'

// Sourced from GitHub Actions secrets at deploy time — never commit real values.
param postgresAdminUser = readEnvironmentVariable('POSTGRES_ADMIN_USER', 'makables_admin')
@secure()
param postgresAdminPassword = readEnvironmentVariable('POSTGRES_ADMIN_PASSWORD', '')

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
