// Orchestrator template for Makables MVP infrastructure (single region — West Europe).
//
// Composes: Postgres Flexible Server, four App Services (Customer / Maker / Admin / Public),
// Azure Functions, Blob storage, Key Vault, App Insights + Log Analytics workspace.
// Per ADR 0023 §3 (availability), §4 (observability), and ADR 0005 (per-audience hosts).
//
// Deployed twice — once for staging (`envs/staging.bicepparam`) and once for
// production (`envs/production.bicepparam`). The two environments use the
// same template but different SKUs, names, and CORS origins.

targetScope = 'resourceGroup'

@description('Environment slug — appears in every resource name. e.g. "stg" or "prod".')
@allowed([
  'stg'
  'prod'
])
param envSlug string

@description('Region for every resource. Defaults to West Europe per ADR 0023.')
param location string = 'westeurope'

@description('Postgres SKU name. Burstable B1ms in staging, B2s in production.')
param postgresSku string = 'Standard_B1ms'

@description('Postgres SKU tier.')
param postgresSkuTier string = 'Burstable'

@description('Postgres storage in GB.')
param postgresStorageGb int = 32

@description('App Service Plan SKU.')
@allowed([
  'B1'
  'B2'
  'P0v3'
  'P1v3'
])
param appServicePlanSku string = 'B1'

@description('Postgres admin username (sourced from GitHub secrets / KeyVault).')
param postgresAdminUser string

@secure()
@description('Postgres admin password (sourced from GitHub secrets / KeyVault).')
param postgresAdminPassword string

@description('Comma-separated allowlist of frontend origins for CORS.')
param customerCorsOrigins array
param makerCorsOrigins array
param adminCorsOrigins array
param publicCorsOrigins array

var prefix = 'makables-${envSlug}'
var storageBaseName = 'makables${envSlug}'

resource appServicePlan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: '${prefix}-asp'
  location: location
  sku: {
    name: appServicePlanSku
  }
  kind: 'linux'
  properties: {
    reserved: true
  }
}

module appInsights 'modules/app-insights.bicep' = {
  name: '${prefix}-appinsights'
  params: {
    appInsightsName: '${prefix}-ai'
    workspaceName: '${prefix}-law'
    location: location
  }
}

module postgres 'modules/postgres.bicep' = {
  name: '${prefix}-pg'
  params: {
    serverName: '${prefix}-pg'
    location: location
    skuName: postgresSku
    skuTier: postgresSkuTier
    storageGb: postgresStorageGb
    administratorLogin: postgresAdminUser
    administratorLoginPassword: postgresAdminPassword
  }
}

var postgresConnectionString = 'Host=${postgres.outputs.serverFqdn};Database=makables;Username=${postgresAdminUser};Password=${postgresAdminPassword};SslMode=Require'

module blob 'modules/blob.bicep' = {
  name: '${prefix}-blob'
  params: {
    storageAccountName: '${storageBaseName}blob'
    location: location
  }
}

module customerApp 'modules/app-service.bicep' = {
  name: '${prefix}-customer'
  params: {
    appName: '${prefix}-customer'
    appServicePlanId: appServicePlan.id
    location: location
    audience: 'customer'
    appInsightsConnectionString: appInsights.outputs.connectionString
    postgresConnectionString: postgresConnectionString
    corsOrigins: customerCorsOrigins
  }
}

module makerApp 'modules/app-service.bicep' = {
  name: '${prefix}-maker'
  params: {
    appName: '${prefix}-maker'
    appServicePlanId: appServicePlan.id
    location: location
    audience: 'maker'
    appInsightsConnectionString: appInsights.outputs.connectionString
    postgresConnectionString: postgresConnectionString
    corsOrigins: makerCorsOrigins
  }
}

module adminApp 'modules/app-service.bicep' = {
  name: '${prefix}-admin'
  params: {
    appName: '${prefix}-admin'
    appServicePlanId: appServicePlan.id
    location: location
    audience: 'admin'
    appInsightsConnectionString: appInsights.outputs.connectionString
    postgresConnectionString: postgresConnectionString
    corsOrigins: adminCorsOrigins
  }
}

module publicApp 'modules/app-service.bicep' = {
  name: '${prefix}-public'
  params: {
    appName: '${prefix}-public'
    appServicePlanId: appServicePlan.id
    location: location
    audience: 'public'
    appInsightsConnectionString: appInsights.outputs.connectionString
    postgresConnectionString: postgresConnectionString
    corsOrigins: publicCorsOrigins
  }
}

module functions 'modules/functions.bicep' = {
  name: '${prefix}-functions'
  params: {
    functionsAppName: '${prefix}-functions'
    storageAccountName: '${storageBaseName}fn'
    appServicePlanId: appServicePlan.id
    appInsightsConnectionString: appInsights.outputs.connectionString
    postgresConnectionString: postgresConnectionString
    location: location
  }
}

module keyVault 'modules/key-vault.bicep' = {
  name: '${prefix}-kv'
  params: {
    keyVaultName: '${prefix}-kv'
    location: location
    readerPrincipalIds: [
      customerApp.outputs.principalId
      makerApp.outputs.principalId
      adminApp.outputs.principalId
      publicApp.outputs.principalId
      functions.outputs.principalId
    ]
  }
}

output customerAppName string = customerApp.outputs.appName
output makerAppName string = makerApp.outputs.appName
output adminAppName string = adminApp.outputs.appName
output publicAppName string = publicApp.outputs.appName
output postgresFqdn string = postgres.outputs.serverFqdn
output appInsightsConnectionString string = appInsights.outputs.connectionString
output keyVaultUri string = keyVault.outputs.keyVaultUri
