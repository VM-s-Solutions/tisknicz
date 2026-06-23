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

@description('Environment slug — appears in every resource name. e.g. "dev" or "prod".')
@allowed([
  'dev'
  'prod'
])
param envSlug string

@description('Region for every resource. Defaults to West Europe per ADR 0023.')
param location string = 'westeurope'

@description('Region for Postgres specifically. Defaults to the main location, but can differ when a subscription is offer-restricted for Postgres Flexible Server in the main region (e.g. a new/trial sub blocks westeurope — use northeurope/francecentral). The DB connection string uses the server FQDN, so a cross-region Postgres works; expect a few ms extra latency.')
param postgresLocation string = location

@description('Postgres SKU name. Burstable B1ms in staging, B2s in production.')
param postgresSku string = 'Standard_B1ms'

@description('Postgres SKU tier.')
param postgresSkuTier string = 'Burstable'

@description('Postgres storage in GB.')
param postgresStorageGb int = 32

@description('App Service Plan SKU. B1/B2 for staging; P1v3 for production per ADR 0023 §7.')
@allowed([
  'B1'
  'B2'
  'P1v3'
  'P2v3'
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

@description('Public site origin (https://...) for PublicAppUrls:WebBaseUrl — per env, so staging emails do not link to prod.')
param publicWebBaseUrl string

// --- Application secrets (sourced from GitHub Actions secrets at deploy time;
// NO secret value lives in the repo). Each host validates these at boot via
// ValidateOnStart — a missing one crashes the host. See docs/deployment/env-vars.md.

@description('JWT issuer (Jwt:Issuer) — non-secret, per environment.')
param jwtIssuer string

@secure()
@description('JWT signing key, base64, >=32 bytes (Jwt:SigningKeyBase64).')
param jwtSigningKeyBase64 string

@secure()
@description('SendGrid API key (SendGrid:ApiKey).')
param sendGridApiKey string

@description('Comgate merchant id (Comgate:MerchantId) — non-secret.')
param comgateMerchantId string

@secure()
@description('Comgate webhook secret (Comgate:Secret).')
param comgateSecret string

@secure()
@description('Packeta API key (Packeta:ApiKey).')
param packetaApiKey string

@secure()
@description('Packeta public widget key (Packeta:PublicWidgetKey).')
param packetaPublicWidgetKey string

@secure()
@description('Mapbox access token (Mapbox:AccessToken).')
param mapboxAccessToken string

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
    location: postgresLocation
    skuName: postgresSku
    skuTier: postgresSkuTier
    storageGb: postgresStorageGb
    administratorLogin: postgresAdminUser
    administratorLoginPassword: postgresAdminPassword
    // Dev gets the "any Azure service" firewall rule for convenience;
    // production goes through a Private Endpoint that the operator wires
    // out-of-band per T-0134's runbook.
    allowAllAzureServices: envSlug == 'dev'
  }
}

// TODO(T-0134): move the Postgres connection string to a Key Vault secret
// and reference it from each App Service via
// `@Microsoft.KeyVault(SecretUri=...)` so the password is not visible in
// the resource group as a plain App Setting. Requires the application
// principals to be granted Key Vault Secrets User BEFORE first deploy,
// which the current ordering does not guarantee (KV depends on principals
// that come from App Service deploys). Cycle-breaking lives in the
// pre-launch runbook.
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
    publicWebBaseUrl: publicWebBaseUrl
    blobServiceUri: blob.outputs.blobServiceUri
    outboxQueuesConnectionString: functions.outputs.queuesConnectionString
    jwtIssuer: jwtIssuer
    jwtSigningKeyBase64: jwtSigningKeyBase64
    sendGridApiKey: sendGridApiKey
    comgateMerchantId: comgateMerchantId
    comgateSecret: comgateSecret
    packetaApiKey: packetaApiKey
    packetaPublicWidgetKey: packetaPublicWidgetKey
    mapboxAccessToken: mapboxAccessToken
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
    publicWebBaseUrl: publicWebBaseUrl
    blobServiceUri: blob.outputs.blobServiceUri
    outboxQueuesConnectionString: functions.outputs.queuesConnectionString
    jwtIssuer: jwtIssuer
    jwtSigningKeyBase64: jwtSigningKeyBase64
    sendGridApiKey: sendGridApiKey
    comgateMerchantId: comgateMerchantId
    comgateSecret: comgateSecret
    packetaApiKey: packetaApiKey
    packetaPublicWidgetKey: packetaPublicWidgetKey
    mapboxAccessToken: mapboxAccessToken
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
    publicWebBaseUrl: publicWebBaseUrl
    blobServiceUri: blob.outputs.blobServiceUri
    outboxQueuesConnectionString: functions.outputs.queuesConnectionString
    jwtIssuer: jwtIssuer
    jwtSigningKeyBase64: jwtSigningKeyBase64
    sendGridApiKey: sendGridApiKey
    comgateMerchantId: comgateMerchantId
    comgateSecret: comgateSecret
    packetaApiKey: packetaApiKey
    packetaPublicWidgetKey: packetaPublicWidgetKey
    mapboxAccessToken: mapboxAccessToken
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
    publicWebBaseUrl: publicWebBaseUrl
    blobServiceUri: blob.outputs.blobServiceUri
    outboxQueuesConnectionString: functions.outputs.queuesConnectionString
    jwtIssuer: jwtIssuer
    jwtSigningKeyBase64: jwtSigningKeyBase64
    sendGridApiKey: sendGridApiKey
    comgateMerchantId: comgateMerchantId
    comgateSecret: comgateSecret
    packetaApiKey: packetaApiKey
    packetaPublicWidgetKey: packetaPublicWidgetKey
    mapboxAccessToken: mapboxAccessToken
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
    blobServiceUri: blob.outputs.blobServiceUri
    sendGridApiKey: sendGridApiKey
    comgateMerchantId: comgateMerchantId
    comgateSecret: comgateSecret
    packetaApiKey: packetaApiKey
    packetaPublicWidgetKey: packetaPublicWidgetKey
    mapboxAccessToken: mapboxAccessToken
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
