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

@description('Replication SKU for the BLOB storage account (product images, invoices, maker documents). Dev keeps Standard_LRS; production sets Standard_GZRS per ADR 0023 §7 — see the param doc in modules/blob.bicep for why GZRS rather than the GRS the ADR originally named. This does NOT apply to the Functions host storage account, which stays LRS deliberately (see modules/functions.bicep).')
@allowed([
  'Standard_LRS'
  'Standard_ZRS'
  'Standard_GRS'
  'Standard_GZRS'
])
param blobStorageSku string = 'Standard_LRS'

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

@description('Give the App Services a PRIVATE path to Postgres: a VNet, a private endpoint on the server, and the privatelink DNS zone. Production only. Dev keeps the "allow all Azure services" firewall rule instead — its plan is Basic and, more importantly, dev is live and there is no reason to re-plumb a working environment. This never touches the server\'s own network block: Flexible Server networking mode is fixed at creation, and a private endpoint attaches alongside public access rather than replacing it.')
param enablePrivateNetworking bool = false

@description('Comgate API base URL (Comgate:BaseUrl). Empty keeps the code default, which is the LIVE gateway (https://payments.comgate.cz) — so a non-production environment that will actually transact against Comgate must set this to the sandbox host. Dev normally never reaches Comgate at all: envSlug dev enables the DevPaymentProvider bypass below, which mints a synthetic session and never calls the gateway.')
param comgateBaseUrl string = ''

@description('Source IPs / CIDR ranges Comgate sends webhook callbacks from (Comgate:WebhookAllowedIps). The allowlist is FAIL-CLOSED: empty rejects every callback with 401, which is the current state of every deployed environment. Values are operator-supplied from Comgate\'s published ranges — they are deliberately not hardcoded here, because a guessed range silently breaks the only route an order has to Paid.')
param comgateWebhookAllowedIps string[] = []

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
var vnetName = 'vnet-makables-${suffix}'
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
    // Dev gets the "any Azure service" firewall rule for convenience.
    // Production gets NO firewall rule at all and reaches Postgres over the
    // private endpoint in modules/network.bicep — wired by this template, not
    // out of band. With zero rules the server has no public path in.
    allowAllAzureServices: envSlug == 'dev'
  }
}

// Private path to Postgres. Depends on the server existing (for its id), so it
// sits after the postgres module. The apps consume network.outputs only through
// a ternary — referencing a conditional module's output unconditionally is a
// compile error even when the condition is false.
module network 'modules/network.bicep' = if (enablePrivateNetworking) {
  name: 'network'
  params: {
    vnetName: vnetName
    // Co-regional with the App Services, NOT with Postgres. Regional VNet
    // integration requires the former; Private Link is global-reach, so the
    // northeurope database is reached cross-region.
    location: location
    postgresServerId: postgres.outputs.serverId
  }
}

var appSubnetId = enablePrivateNetworking ? network.outputs.integrationSubnetId : ''

module blob 'modules/blob.bicep' = {
  name: 'blob'
  params: {
    storageAccountName: blobStorageName
    location: location
    skuName: blobStorageSku
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
    name: 'Resend__ApiKey'
    value: kvRef(kvUri, 'Resend--ApiKey')
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
  // OAuth sign-in providers (Auth:Google T-0026, Auth:Apple ADR 0026).
  // Deliberately NOT ValidateOnStart in the hosts — a boot-stub value keeps
  // the KV reference resolvable and the feature fails closed at the provider
  // until real credentials land (dev); prod requires real values.
  {
    name: 'Auth__Google__ClientId'
    value: kvRef(kvUri, 'Auth--Google--ClientId')
  }
  {
    name: 'Auth__Google__ClientSecret'
    value: kvRef(kvUri, 'Auth--Google--ClientSecret')
  }
  {
    name: 'Auth__Apple__ClientId'
    value: kvRef(kvUri, 'Auth--Apple--ClientId')
  }
  {
    name: 'Auth__Apple__TeamId'
    value: kvRef(kvUri, 'Auth--Apple--TeamId')
  }
  {
    name: 'Auth__Apple__KeyId'
    value: kvRef(kvUri, 'Auth--Apple--KeyId')
  }
  {
    name: 'Auth__Apple__PrivateKeyPem'
    value: kvRef(kvUri, 'Auth--Apple--PrivateKeyPem')
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
    name: 'Resend__ApiKey'
    value: kvRef(kvUri, 'Resend--ApiKey')
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

// ---------------------------------------------------------------------------
// Dev payment bypass. Replaces the Comgate redirect with a one-click confirm
// hop back into our own Customer host so the checkout flow is walkable on dev
// without real cards or a public webhook endpoint. Gated on envSlug so the
// setting is structurally absent from production — not merely set to false.
//
// ConfirmBaseUrl stays ORIGIN-RELATIVE on purpose: the browser resolves it
// against whichever hostname the tester actually browsed (custom domain or
// the default *.azurewebsites.net name), which keeps the confirm navigation
// same-origin. The session cookies are SameSite=Strict, so a cross-site hop
// would arrive with no cookie and 401. '/api-proxy/customer' is the T-0153
// rewrite in frontend/next.config.ts.
// ---------------------------------------------------------------------------
var devPaymentAppSettings = envSlug == 'dev' ? [
  {
    name: 'Payments__Dev__Enabled'
    value: 'true'
  }
  {
    name: 'Payments__Dev__ConfirmBaseUrl'
    value: '/api-proxy/customer'
  }
] : []

// ---------------------------------------------------------------------------
// Comgate — non-secret settings. The merchant id and secret are Key Vault
// references in apiSecretSettings; these two are the switches that decide
// WHICH gateway is called and WHOSE callbacks are accepted.
//
// Both are omitted when unset rather than emitted empty: an empty
// Comgate__BaseUrl would fail the host's absolute-https validation at boot,
// and an absent allowlist leaves the filter's fail-closed default intact.
// ---------------------------------------------------------------------------
var comgateBaseUrlSetting = empty(comgateBaseUrl) ? [] : [
  {
    name: 'Comgate__BaseUrl'
    value: comgateBaseUrl
  }
]

// Flat key/value app settings cannot hold arrays, so the list expands to the
// indexed keys the options binder reads — same shape as Cors__AllowedOrigins.
var comgateAllowlistSettings = [for (ip, i) in comgateWebhookAllowedIps: {
  name: 'Comgate__WebhookAllowedIps__${i}'
  value: ip
}]


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
    extraAppSettings: concat(devPaymentAppSettings, comgateBaseUrlSetting, comgateAllowlistSettings)
    virtualNetworkSubnetId: appSubnetId
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
    extraAppSettings: concat(devPaymentAppSettings, comgateBaseUrlSetting, comgateAllowlistSettings)
    virtualNetworkSubnetId: appSubnetId
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
    extraAppSettings: concat(devPaymentAppSettings, comgateBaseUrlSetting, comgateAllowlistSettings)
    virtualNetworkSubnetId: appSubnetId
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
    extraAppSettings: concat(devPaymentAppSettings, comgateBaseUrlSetting, comgateAllowlistSettings)
    virtualNetworkSubnetId: appSubnetId
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
    // The Functions host reaches Postgres for the outbox, so it needs the same
    // private path as the API hosts. The frontend web app deliberately does NOT
    // integrate: it holds no database setting and only proxies to the API hosts
    // over the public internet, so putting it in the VNet would add a hop and
    // consume subnet addresses for nothing.
    virtualNetworkSubnetId: appSubnetId
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
// Everything stays in Azure (no Vercel). T-0153 same-origin proxy: the
// browser-facing NEXT_PUBLIC_* bases are relative `/api-proxy/<host>` paths
// (session cookies must land first-party — sibling *.azurewebsites.net hosts
// share no cookie-visible parent); the API_*_INTERNAL_BASE_URL settings carry
// the real host origins for SSR fetches and the Next rewrite table.
module webApp 'modules/web-app.bicep' = {
  name: 'web-app'
  params: {
    appName: webAppName
    appServicePlanId: appServicePlan.id
    location: location
    siteUrl: publicWebBaseUrl
    customerApiBaseUrl: '/api-proxy/customer'
    makerApiBaseUrl: '/api-proxy/maker'
    adminApiBaseUrl: '/api-proxy/admin'
    publicApiBaseUrl: '/api-proxy/public'
    customerApiInternalBaseUrl: 'https://${customerApp.outputs.defaultHostName}'
    makerApiInternalBaseUrl: 'https://${makerApp.outputs.defaultHostName}'
    adminApiInternalBaseUrl: 'https://${adminApp.outputs.defaultHostName}'
    publicApiInternalBaseUrl: 'https://${publicApp.outputs.defaultHostName}'
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
