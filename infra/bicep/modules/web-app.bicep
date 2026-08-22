// Next.js frontend App Service (Linux / Node). Runs the SSR app via
// `next start` on the SAME App Service Plan as the four API hosts — everything
// stays in Azure (no Vercel). The frontend is a pure presentation layer; its
// only runtime config is the API base URLs (non-secret).
//
// T-0153 same-origin proxy: the NEXT_PUBLIC_* bases are the browser-facing
// values (relative `/api-proxy/<host>` paths on deployed envs — inlined at
// BUILD time; the copies here are documentation-of-record). The
// API_*_INTERNAL_BASE_URL settings are the ones the standalone server
// actually reads at RUNTIME for SSR fetches (lib/runtime/api-fetch.ts).

@description('Logical app name, e.g. web-makables-weu-dev.')
param appName string

@description('Resource ID of the App Service Plan to host on (shared with the API hosts).')
param appServicePlanId string

@description('Region — inherits from resource group.')
param location string = resourceGroup().location

@description('Public site origin (NEXT_PUBLIC_SITE_URL), e.g. https://dev.makables.cz.')
param siteUrl string

@description('Customer API base URL (NEXT_PUBLIC_API_CUSTOMER_BASE_URL).')
param customerApiBaseUrl string

@description('Maker API base URL (NEXT_PUBLIC_API_MAKER_BASE_URL).')
param makerApiBaseUrl string

@description('Admin API base URL (NEXT_PUBLIC_API_ADMIN_BASE_URL).')
param adminApiBaseUrl string

@description('Public API base URL (NEXT_PUBLIC_API_PUBLIC_BASE_URL).')
param publicApiBaseUrl string

@description('Customer API absolute origin for SSR fetches (API_CUSTOMER_INTERNAL_BASE_URL).')
param customerApiInternalBaseUrl string

@description('Maker API absolute origin for SSR fetches (API_MAKER_INTERNAL_BASE_URL).')
param makerApiInternalBaseUrl string

@description('Admin API absolute origin for SSR fetches (API_ADMIN_INTERNAL_BASE_URL).')
param adminApiInternalBaseUrl string

@description('Public API absolute origin for SSR fetches (API_PUBLIC_INTERNAL_BASE_URL).')
param publicApiInternalBaseUrl string

resource webApp 'Microsoft.Web/sites@2024-04-01' = {
  name: appName
  location: location
  properties: {
    serverFarmId: appServicePlanId
    httpsOnly: true
    siteConfig: {
      // Node 20 left support in April 2026 — the portal flags it as a
      // deprecated runtime module (no further bug/security fixes). 22-lts
      // is the current App Service LTS image and satisfies Next 16's
      // engines constraint (>=20.9.0). Keep this and the CI
      // `node-version` in the deploy workflows on the SAME major — the
      // standalone bundle is built against whatever CI used.
      linuxFxVersion: 'NODE|22-lts'
      alwaysOn: true
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      use32BitWorkerProcess: false
      // HTTP/2 at the App Service front end — multiplexes the CSS/JS/font
      // fetches the storefront's first paint depends on; App Service
      // defaults to HTTP/1.1 unless opted in.
      http20Enabled: true
      // The Next.js `output: 'standalone'` build emits server.js; run it
      // directly (no `next start` / npm install needed at runtime).
      appCommandLine: 'node server.js'
      appSettings: [
        {
          // App Service builds the app on deploy when this is true (Oryx). We
          // deploy a prebuilt artifact instead, so disable the build step.
          name: 'SCM_DO_BUILD_DURING_DEPLOYMENT'
          value: 'false'
        }
        {
          name: 'WEBSITE_NODE_DEFAULT_VERSION'
          value: '~22'
        }
        {
          // The platform's startup probe defaults to 230s. Six always-on
          // apps share ONE Linux plan (4 API hosts + this + Functions), so a
          // deploy bounces all of them onto the same 2 cores at once and the
          // single-threaded Next server can miss that window — the site then
          // dies with "did not respond to startup probe on port 8080 ...
          // No listening ports were detected" even though nothing is wrong
          // with the build. Give the cold start real headroom (max 1800).
          name: 'WEBSITES_CONTAINER_START_TIME_LIMIT'
          value: '600'
        }
        {
          name: 'NODE_ENV'
          value: 'production'
        }
        {
          name: 'NEXT_PUBLIC_SITE_URL'
          value: siteUrl
        }
        {
          name: 'NEXT_PUBLIC_API_CUSTOMER_BASE_URL'
          value: customerApiBaseUrl
        }
        {
          name: 'NEXT_PUBLIC_API_MAKER_BASE_URL'
          value: makerApiBaseUrl
        }
        {
          name: 'NEXT_PUBLIC_API_ADMIN_BASE_URL'
          value: adminApiBaseUrl
        }
        {
          name: 'NEXT_PUBLIC_API_PUBLIC_BASE_URL'
          value: publicApiBaseUrl
        }
        {
          name: 'API_CUSTOMER_INTERNAL_BASE_URL'
          value: customerApiInternalBaseUrl
        }
        {
          name: 'API_MAKER_INTERNAL_BASE_URL'
          value: makerApiInternalBaseUrl
        }
        {
          name: 'API_ADMIN_INTERNAL_BASE_URL'
          value: adminApiInternalBaseUrl
        }
        {
          name: 'API_PUBLIC_INTERNAL_BASE_URL'
          value: publicApiInternalBaseUrl
        }
      ]
    }
  }
  tags: {
    role: 'frontend'
  }
}

// Container/console logs to the filesystem so Log stream shows Next.js stdout
// (startup errors, SSR crashes). See app-service.bicep for rationale.
resource siteLogs 'Microsoft.Web/sites/config@2024-04-01' = {
  parent: webApp
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

output appName string = webApp.name
output defaultHostName string = webApp.properties.defaultHostName
