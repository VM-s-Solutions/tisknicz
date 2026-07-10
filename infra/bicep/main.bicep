// Orchestrator template for Makables infrastructure (single region — West Europe).
//
// Composes: Postgres Flexible Server, four App Services (Customer / Maker / Admin / Public),
// Azure Functions, Blob storage, Key Vault, App Insights + Log Analytics, RBAC role
// assignments, derived Key Vault secrets, and (optional) metric alerts.
// Per ADR 0023 §3 (availability), §4 (observability), and ADR 0005 (per-audience hosts).
//
// Deployed twice — once for dev (`envs/weu.dev.bicepparam`) and once for production
// (`envs/weu.prod.bicepparam`). Same template, different SKUs, names, CORS origins.
//
// SECRETS MODEL (T-0134, ported from the Cleansia deployment pattern):
//   1. App runtime secrets are App Service settings that are
//      `@Microsoft.KeyVault(SecretUri=...)` REFERENCES, resolved at runtime by each
//      host's system-assigned managed identity (Key Vault Secrets User, granted in
//      modules/role-assignments.bicep). No secret value is visible in the RG.
//   2. Bicep writes only the secrets it can DERIVE (modules/derived-secrets.bicep):
//      Postgres/Storage/outbox connection strings + Jwt issuer/audience.
//   3. EXTERNAL secrets (JWT signing key, SendGrid/Comgate/Packeta/Mapbox) are pushed
//      from GitHub Environment secrets into Key Vault by the deploy workflow's
//      "Push external secrets" step — they are NOT Bicep parameters anymore.
//
// NAMING (Cleansia/CAF pattern): <type>-makables[-<audience>]-<region>-<env>, with
// globally-unique alphanumeric-only resources collapsing the hyphens (stmakablesweudev).
// The region token is the deployment region (weu), never a country — countries are
// application data (CountryConfiguration), not infrastructure.
//
// DEPLOY IDENTITY REQUIREMENT: modules/role-assignments.bicep creates RBAC role
// assignments, so the deploying principal (the GitHub OIDC service principal or the
// operator) MUST hold Microsoft.Authorization/roleAssignments/write on the resource
// group — i.e. User Access Administrator or Owner, not just Contributor.

targetScope = 'resourceGroup'

@description('Environment slug — appears in every resource name. e.g. "dev" or "prod".')
@allowed([
  'dev'
  'prod'
])
param envSlug string

@description('Short region token for resource names (Cleansia convention), e.g. "weu".')
param region string = 'weu'

@description('Region for every resource. Defaults to West Europe per ADR 0023.')
param location string = 'westeurope'

@description('Region for Postgres specifically. Defaults to the main location, but can differ when a subscription is offer-restricted for Postgres Flexible Server in the main region (e.g. a new/trial sub blocks westeurope — use northeurope/francecentral). The DB connection string uses the server FQDN, so a cross-region Postgres works; expect a few ms extra latency.')
param postgresLocation string = location

@description('Postgres SKU name. Burstable B1ms/B2s in dev, D2s_v3 in production.')
param postgresSku string = 'Standard_B1ms'

@description('Postgres SKU tier.')
param postgresSkuTier string = 'Burstable'

@description('Postgres storage in GB.')
param postgresStorageGb int = 32

@description('App Service Plan SKU. B1/B2 for dev; P1v3 for production per ADR 0023 §7.')
@allowed([
  'B1'
  'B2'
  'P1v3'
  'P2v3'
])
param appServicePlanSku string = 'B1'

@description('Postgres admin username (GitHub Actions secret at deploy time).')
param postgresAdminUser string

@secure()
@description('Postgres admin password (GitHub Actions secret at deploy time). Also flows into the derived ConnectionStrings--Postgres Key Vault secret.')
param postgresAdminPassword string

@description('Allowlist of frontend origins for CORS, per audience.')
param customerCorsOrigins array
param makerCorsOrigins array
param adminCorsOrigins array
param publicCorsOrigins array

@description('Public site origin (https://...) for PublicAppUrls:WebBaseUrl — per env, so dev emails do not link to prod.')
param publicWebBaseUrl string

@description('JWT issuer (Jwt:Issuer) — non-secret, per environment.')
param jwtIssuer string

@description('Ops email for metric alerts. Empty skips the alerts module entirely.')
param alertEmail string = ''

// ---------------------------------------------------------------------------
// Names (Cleansia/CAF convention).
// ---------------------------------------------------------------------------
var suffix = '${region}-${envSlug}'
var planName = 'plan-makables-${suffix}'
var customerAppName = 'app-makables-customer-${suffix}'
var makerAppName = 'app-makables-maker-${suffix}'
var adminAppName = 'app-makables-admin-${suffix}'
var publicAppName = 'app-makables-public-${suffix}'
var webAppName = 'web-makables-${suffix}'
var functionsAppName = 'func-makables-${suffix}'
var postgresServerName = 'pg-makables-${suffix}'
var keyVaultName = 'kv-makables-${suffix}'
var blobStorageName = 'stmakables${region}${envSlug}'
var functionsStorageName = 'stmakablesfn${region}${envSlug}'
var appInsightsName = 'appi-makables-${suffix}'
var workspaceName = 'log-makables-${suffix}'

// ---------------------------------------------------------------------------
// Key Vault reference helper. keyVaultUri (the vault's vaultUri property) ends
// with a trailing '/', so the path segment appends WITHOUT a leading slash.
// The '--' in secret names maps to the .NET ':' config separator on resolve.
// ---------------------------------------------------------------------------
func kvRef(vaultUri string, secretName string) string =>
  '@Microsoft.KeyVault(SecretUri=${vaultUri}secrets/${secretName})'

resource appServicePlan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: planName
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
  name: 'app-insights'
  params: {
    appInsightsName: appInsightsName
    workspaceName: workspaceName
    location: location
  }
}

module postgres 'modules/postgres.bicep' = {
  name: 'postgres'
  params: {
    serverName: postgresServerName
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

module blob 'modules/blob.bicep' = {
  name: 'blob'
  params: {
    storageAccountName: blobStorageName
    location: location
  }
}

// Vault first (no principal inputs — role grants live in role-assignments.bicep,
// AFTER the apps exist; this breaks the old keyVault<->apps parameter cycle).
module keyVault 'modules/key-vault.bicep' = {
  name: 'key-vault'
  params: {
    keyVaultName: keyVaultName
    location: location
  }
}

// ---------------------------------------------------------------------------
// Secret app settings — Key Vault REFERENCE strings (no secret material here).
// Composed once, passed to all four API hosts; the Functions host gets the
// same set minus Jwt (it never issues/validates tokens).
// ---------------------------------------------------------------------------
var kvUri = keyVault.outputs.keyVaultUri

var apiSecretSettings = [
  {
    name: 'ConnectionStrings__Postgres'
    value: kvRef(kvUri, 'ConnectionStrings--Postgres')
  }
  {
    name: 'BlobStorage__ConnectionString'
    value: kvRef(kvUri, 'Storage--ConnectionString')
  }
  {
    name: 'OutboxQueues__ConnectionString'
    value: kvRef(kvUri, 'OutboxQueues--ConnectionString')
  }
  {
    name: 'Jwt__SigningKeyBase64'
    value: kvRef(kvUri, 'Jwt--SigningKeyBase64')
  }
  {
    name: 'SendGrid__ApiKey'
    value: kvRef(kvUri, 'SendGrid--ApiKey')
  }
  {
    name: 'Comgate__MerchantId'
    value: kvRef(kvUri, 'Comgate--MerchantId')
  }
  {
    name: 'Comgate__Secret'
    value: kvRef(kvUri, 'Comgate--Secret')
  }
  {
    name: 'Packeta__ApiKey'
    value: kvRef(kvUri, 'Packeta--ApiKey')
  }
  {
    name: 'Packeta__PublicWidgetKey'
    value: kvRef(kvUri, 'Packeta--PublicWidgetKey')
  }
  {
    name: 'Mapbox__AccessToken'
    value: kvRef(kvUri, 'Mapbox--AccessToken')
  }
]

var functionsSecretSettings = [
  {
    name: 'ConnectionStrings__Postgres'
    value: kvRef(kvUri, 'ConnectionStrings--Postgres')
  }
  {
    name: 'BlobStorage__ConnectionString'
    value: kvRef(kvUri, 'Storage--ConnectionString')
  }
  {
    name: 'OutboxQueues__ConnectionString'
    value: kvRef(kvUri, 'OutboxQueues--ConnectionString')
  }
  {
    name: 'SendGrid__ApiKey'
    value: kvRef(kvUri, 'SendGrid--ApiKey')
  }
  {
    name: 'Comgate__MerchantId'
    value: kvRef(kvUri, 'Comgate--MerchantId')
  }
  {
    name: 'Comgate__Secret'
    value: kvRef(kvUri, 'Comgate--Secret')
  }
  {
    name: 'Packeta__ApiKey'
    value: kvRef(kvUri, 'Packeta--ApiKey')
  }
  {
    name: 'Packeta__PublicWidgetKey'
    value: kvRef(kvUri, 'Packeta--PublicWidgetKey')
  }
  {
    name: 'Mapbox__AccessToken'
    value: kvRef(kvUri, 'Mapbox--AccessToken')
  }
]

module customerApp 'modules/app-service.bicep' = {
  name: 'customer-app'
  params: {
    appName: customerAppName
    appServicePlanId: appServicePlan.id
    location: location
    audience: 'customer'
    appInsightsConnectionString: appInsights.outputs.connectionString
    corsOrigins: customerCorsOrigins
    publicWebBaseUrl: publicWebBaseUrl
    jwtIssuer: jwtIssuer
    secretAppSettings: apiSecretSettings
    healthCheckPath: '/health'
  }
}

module makerApp 'modules/app-service.bicep' = {
  name: 'maker-app'
  params: {
    appName: makerAppName
    appServicePlanId: appServicePlan.id
    location: location
    audience: 'maker'
    appInsightsConnectionString: appInsights.outputs.connectionString
    corsOrigins: makerCorsOrigins
    publicWebBaseUrl: publicWebBaseUrl
    jwtIssuer: jwtIssuer
    secretAppSettings: apiSecretSettings
    healthCheckPath: '/health'
  }
}

module adminApp 'modules/app-service.bicep' = {
  name: 'admin-app'
  params: {
    appName: adminAppName
    appServicePlanId: appServicePlan.id
    location: location
    audience: 'admin'
    appInsightsConnectionString: appInsights.outputs.connectionString
    corsOrigins: adminCorsOrigins
    publicWebBaseUrl: publicWebBaseUrl
    jwtIssuer: jwtIssuer
    secretAppSettings: apiSecretSettings
    healthCheckPath: '/health'
  }
}

module publicApp 'modules/app-service.bicep' = {
  name: 'public-app'
  params: {
    appName: publicAppName
    appServicePlanId: appServicePlan.id
    location: location
    audience: 'public'
    appInsightsConnectionString: appInsights.outputs.connectionString
    corsOrigins: publicCorsOrigins
    publicWebBaseUrl: publicWebBaseUrl
    jwtIssuer: jwtIssuer
    secretAppSettings: apiSecretSettings
    healthCheckPath: '/health'
  }
}

module functions 'modules/functions.bicep' = {
  name: 'functions'
  params: {
    functionsAppName: functionsAppName
    storageAccountName: functionsStorageName
    appServicePlanId: appServicePlan.id
    appInsightsConnectionString: appInsights.outputs.connectionString
    secretAppSettings: functionsSecretSettings
    location: location
  }
}

// RBAC grants: each host MI gets Key Vault Secrets User + Storage Blob/Queue
// Data Contributor; the Functions MI additionally gets Blob Data Owner on its
// own storage account (required for identity-based AzureWebJobsStorage).
// Requires the deploy identity to hold roleAssignments/write (see header).
module roleAssignments 'modules/role-assignments.bicep' = {
  name: 'role-assignments'
  params: {
    keyVaultId: keyVault.outputs.keyVaultId
    storageAccountIds: [
      blob.outputs.storageAccountId
      functions.outputs.storageAccountId
    ]
    webPrincipalIds: [
      customerApp.outputs.principalId
      makerApp.outputs.principalId
      adminApp.outputs.principalId
      publicApp.outputs.principalId
    ]
    functionsPrincipalId: functions.outputs.principalId
    functionsStorageAccountId: functions.outputs.storageAccountId
  }
}

// Key Vault secrets Bicep can DERIVE (connection strings, Jwt issuer/audience).
// The EXTERNAL secrets are pushed by the deploy workflow's push-secrets step.
module derivedSecrets 'modules/derived-secrets.bicep' = {
  name: 'derived-secrets'
  params: {
    keyVaultName: keyVault.outputs.keyVaultName
    storageAccountName: blob.outputs.storageAccountName
    functionsStorageAccountName: functions.outputs.storageAccountName
    postgresFqdn: postgres.outputs.serverFqdn
    postgresAdministratorLogin: postgresAdminUser
    postgresAdministratorPassword: postgresAdminPassword
    jwtIssuer: jwtIssuer
  }
}

// Next.js frontend on its own Linux App Service (Node), on the shared plan.
// Everything stays in Azure (no Vercel). The NEXT_PUBLIC_* API base URLs point
// at the deployed API hosts' default hostnames.
module webApp 'modules/web-app.bicep' = {
  name: 'web-app'
  params: {
    appName: webAppName
    appServicePlanId: appServicePlan.id
    location: location
    siteUrl: publicWebBaseUrl
    customerApiBaseUrl: 'https://${customerApp.outputs.defaultHostName}'
    makerApiBaseUrl: 'https://${makerApp.outputs.defaultHostName}'
    adminApiBaseUrl: 'https://${adminApp.outputs.defaultHostName}'
    publicApiBaseUrl: 'https://${publicApp.outputs.defaultHostName}'
  }
}

// Metric alerts over the hosts + Postgres + App Insights exceptions. Deployed
// only when alertEmail is set. Scopes are deploy-time NAMES (BCP182), so the
// module block declares explicit dependsOn to order after the resources exist.
module alerts 'modules/alerts.bicep' = if (!empty(alertEmail)) {
  name: 'alerts'
  params: {
    envSlug: envSlug
    alertEmail: alertEmail
    actionGroupName: 'ag-makables-${suffix}'
    siteNames: [
      customerAppName
      makerAppName
      adminAppName
      publicAppName
      functionsAppName
      webAppName
    ]
    postgresServerName: postgresServerName
    appInsightsName: appInsightsName
  }
  dependsOn: [
    customerApp
    makerApp
    adminApp
    publicApp
    functions
    webApp
    appInsights
    postgres
  ]
}

output customerAppName string = customerApp.outputs.appName
output makerAppName string = makerApp.outputs.appName
output adminAppName string = adminApp.outputs.appName
output publicAppName string = publicApp.outputs.appName
output functionsAppName string = functionsAppName
output postgresFqdn string = postgres.outputs.serverFqdn
output appInsightsConnectionString string = appInsights.outputs.connectionString
output keyVaultName string = keyVault.outputs.keyVaultName
output keyVaultUri string = keyVault.outputs.keyVaultUri
output webAppName string = webApp.outputs.appName
output webAppHostName string = webApp.outputs.defaultHostName
