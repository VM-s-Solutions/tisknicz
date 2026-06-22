// App Service module — used four times (Customer / Maker / Admin / Public).
//
// Each audience gets its own App Service so traffic, scaling, and access
// logs are isolated per ADR 0005. All four share a single App Service Plan
// to keep the cost ceiling realistic for the MVP.

@description('Logical app name, e.g. makables-stg-customer.')
param appName string

@description('Resource ID of the App Service Plan to host on.')
param appServicePlanId string

@description('Region — inherits from resource group.')
param location string = resourceGroup().location

@description('Audience tag for diagnostics + Authorization claim binding.')
@allowed([
  'customer'
  'maker'
  'admin'
  'public'
])
param audience string

@description('App Insights connection string (forwarded as APPLICATIONINSIGHTS_CONNECTION_STRING).')
@secure()
param appInsightsConnectionString string

@description('Postgres connection string (forwarded as ConnectionStrings__Postgres).')
@secure()
param postgresConnectionString string

@description('Allowed CORS origins for this audience. Injected as the Cors:AllowedOrigins:<audience> config ARRAY the .NET host reads — NOT platform CORS (which AddMakablesCors ignores). An empty array crashes the host outside Development (fail-closed).')
param corsOrigins array = []

@description('Public site origin for links in emails etc. (PublicAppUrls:WebBaseUrl). Per-env so staging emails do not link to prod.')
param publicWebBaseUrl string

@description('Blob storage account service URI (AzureBlobStorage:ServiceUri) for the managed-identity blob path.')
param blobServiceUri string

@description('Storage queue connection string for the outbox publisher (OutboxQueues:ConnectionString).')
@secure()
param outboxQueuesConnectionString string

// --- Required-to-boot settings the .NET hosts validate via ValidateOnStart ---
// A missing one crashes the host at startup. Non-secrets are plain params;
// secrets are @secure() and sourced from GitHub Actions secrets at deploy time
// (no secret value lives in the repo). See docs/deployment/env-vars.md.

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

// CORS origins injected as the Cors__AllowedOrigins__<audience>__N indexed
// app settings that bind to the string[] the host reads. (Bicep app settings
// are flat key/value, so the array is expanded to indexed keys here.)
var corsAppSettings = [for (origin, i) in corsOrigins: {
  name: 'Cors__AllowedOrigins__${audience}__${i}'
  value: origin
}]

var baseAppSettings = [
  {
    name: 'ASPNETCORE_ENVIRONMENT'
    value: 'Production'
  }
  {
    name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
    value: appInsightsConnectionString
  }
  {
    name: 'Makables__Audience'
    value: audience
  }
  {
    name: 'ConnectionStrings__Postgres'
    value: postgresConnectionString
  }
  {
    name: 'WEBSITES_PORT'
    value: '8080'
  }
  {
    name: 'PublicAppUrls__WebBaseUrl'
    value: publicWebBaseUrl
  }
  {
    name: 'AzureBlobStorage__ServiceUri'
    value: blobServiceUri
  }
  {
    name: 'OutboxQueues__ConnectionString'
    value: outboxQueuesConnectionString
  }
  {
    name: 'Jwt__Issuer'
    value: jwtIssuer
  }
  {
    name: 'Jwt__SigningKeyBase64'
    value: jwtSigningKeyBase64
  }
  {
    name: 'SendGrid__ApiKey'
    value: sendGridApiKey
  }
  {
    name: 'Comgate__MerchantId'
    value: comgateMerchantId
  }
  {
    name: 'Comgate__Secret'
    value: comgateSecret
  }
  {
    name: 'Packeta__ApiKey'
    value: packetaApiKey
  }
  {
    name: 'Packeta__PublicWidgetKey'
    value: packetaPublicWidgetKey
  }
  {
    name: 'Mapbox__AccessToken'
    value: mapboxAccessToken
  }
]

resource app 'Microsoft.Web/sites@2024-04-01' = {
  name: appName
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: appServicePlanId
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: true
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      use32BitWorkerProcess: false
      appSettings: concat(baseAppSettings, corsAppSettings)
    }
  }
  tags: {
    audience: audience
  }
}

output principalId string = app.identity.principalId
output defaultHostName string = app.properties.defaultHostName
output appName string = app.name
