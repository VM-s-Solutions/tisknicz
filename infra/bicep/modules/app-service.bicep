// App Service module — used four times (Customer / Maker / Admin / Public).
//
// Each audience gets its own App Service so traffic, scaling, and access
// logs are isolated per ADR 0005. All four share a single App Service Plan
// to keep the cost ceiling realistic for the MVP.
//
// SECRETS: this module receives secret-bearing settings as `secretAppSettings`
// — an array of { name, value } pairs whose values are
// `@Microsoft.KeyVault(SecretUri=...)` REFERENCE strings composed in
// main.bicep (T-0134). No secret material flows through this module; the
// host's system-assigned managed identity resolves the references at runtime
// (Key Vault Secrets User, granted in role-assignments.bicep).

@description('Logical app name, e.g. app-makables-customer-weu-dev.')
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

@description('Allowed CORS origins for this audience. Injected as the Cors:AllowedOrigins:<audience> config ARRAY the .NET host reads — NOT platform CORS (which AddMakablesCors ignores). An empty array crashes the host outside Development (fail-closed).')
param corsOrigins array = []

@description('Public site origin for links in emails etc. (PublicAppUrls:WebBaseUrl). Per-env so dev emails do not link to prod.')
param publicWebBaseUrl string

@description('JWT issuer (Jwt:Issuer) — non-secret, per environment.')
param jwtIssuer string

@description('Secret-bearing app settings as { name, value } pairs where every value is a Key Vault REFERENCE string (no secret material). Composed in main.bicep from the vault URI + secret names.')
param secretAppSettings array = []

@description('App Service health-check path (Cleansia pattern). The platform pings it per instance; repeated non-2xx marks the instance unhealthy (and with >1 instance pulls it from rotation / restarts it). The API hosts expose a dependency-free liveness endpoint at /health (see each Program.cs). Empty disables the health check.')
param healthCheckPath string = ''

@description('Non-secret per-environment app settings as { name, value } pairs, appended after the base/secret/CORS sets. Used for switches that exist only in some environments — e.g. the dev payment bypass (Payments__Dev__*), which main.bicep passes ONLY when envSlug is dev.')
param extraAppSettings array = []

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
    name: 'WEBSITES_PORT'
    value: '8080'
  }
  {
    // Startup probe headroom (default 230s). All six apps on the shared
    // plan restart together on a deploy, so a cold .NET host can miss the
    // default window and be killed as "no listening ports detected" —
    // indistinguishable in the portal from a real ValidateOnStart crash.
    name: 'WEBSITES_CONTAINER_START_TIME_LIMIT'
    value: '600'
  }
  {
    name: 'PublicAppUrls__WebBaseUrl'
    value: publicWebBaseUrl
  }
  {
    name: 'Jwt__Issuer'
    value: jwtIssuer
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
      // HTTP/2 at the App Service front end (parity with the web app).
      http20Enabled: true
      healthCheckPath: healthCheckPath
      appSettings: concat(baseAppSettings, secretAppSettings, corsAppSettings, extraAppSettings)
    }
  }
  tags: {
    audience: audience
  }
}

// App Service Logs — container stdout/stderr to the filesystem so the portal
// Log stream / `az webapp log tail` / Kudu show the app's console output.
// OFF by default on Linux App Service, which is why a crashing container shows
// nothing in Log stream. Filesystem logging auto-disables after 12h of quota
// pressure but the retention below keeps it bounded regardless.
resource siteLogs 'Microsoft.Web/sites/config@2024-04-01' = {
  parent: app
  name: 'logs'
  properties: {
    applicationLogs: {
      fileSystem: {
        level: 'Information'
      }
    }
    httpLogs: {
      fileSystem: {
        enabled: true
        retentionInMb: 100
        retentionInDays: 3
      }
    }
    detailedErrorMessages: {
      enabled: true
    }
    failedRequestsTracing: {
      enabled: false
    }
  }
}

output principalId string = app.identity.principalId
output defaultHostName string = app.properties.defaultHostName
output appName string = app.name
