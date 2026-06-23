// Next.js frontend App Service (Linux / Node). Runs the SSR app via
// `next start` on the SAME App Service Plan as the four API hosts — everything
// stays in Azure (no Vercel). The frontend is a pure presentation layer; its
// only runtime config is the NEXT_PUBLIC_* API base URLs (non-secret, baked
// into the build but also read at runtime for SSR fetches).

@description('Logical app name, e.g. makables-dev-web.')
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

resource webApp 'Microsoft.Web/sites@2024-04-01' = {
  name: appName
  location: location
  properties: {
    serverFarmId: appServicePlanId
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'NODE|20-lts'
      alwaysOn: true
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      use32BitWorkerProcess: false
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
          value: '~20'
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
      ]
    }
  }
  tags: {
    role: 'frontend'
  }
}

output appName string = webApp.name
output defaultHostName string = webApp.properties.defaultHostName
