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

@description('Allowed CORS origins for this audience.')
param corsOrigins array = []

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
      cors: {
        allowedOrigins: corsOrigins
        supportCredentials: true
      }
      appSettings: [
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
      ]
    }
  }
  tags: {
    audience: audience
  }
}

output principalId string = app.identity.principalId
output defaultHostName string = app.properties.defaultHostName
output appName string = app.name
