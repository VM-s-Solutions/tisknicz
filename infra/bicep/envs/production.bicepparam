using '../main.bicep'

// Production environment — same template as staging but with higher SKUs
// and the production domain as the only CORS origin.

param envSlug = 'prod'
param location = 'westeurope'

param postgresSku = 'Standard_B2s'
param postgresSkuTier = 'Burstable'
param postgresStorageGb = 64

param appServicePlanSku = 'P0v3'

param postgresAdminUser = readEnvironmentVariable('POSTGRES_ADMIN_USER', 'makables_admin')
@secure()
param postgresAdminPassword = readEnvironmentVariable('POSTGRES_ADMIN_PASSWORD', '')

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
