// Azure Functions module — hosts the outbox processor, timer jobs, and
// queue-triggered work per ADR 0020. Single Functions app for the MVP.

@description('Functions app name, e.g. makables-stg-functions.')
param functionsAppName string

@description('Storage account used by the Functions runtime + outbox queues.')
param storageAccountName string

@description('App Service Plan ID (shared with the Web hosts to keep MVP costs low).')
param appServicePlanId string

@description('App Insights connection string for the Functions host.')
@secure()
param appInsightsConnectionString string

@description('Postgres connection string for the Functions host.')
@secure()
param postgresConnectionString string

param location string = resourceGroup().location

resource storage 'Microsoft.Storage/storageAccounts@2024-01-01' = {
  name: storageAccountName
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    allowBlobPublicAccess: false
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
  }
}

// TODO(T-0134): migrate AzureWebJobsStorage to identity-based connection
// (AzureWebJobsStorage__accountName + managed-identity role assignment)
// so the account key is no longer embedded in app settings. Tracked in
// the pre-launch ops runbook.
var storageConnectionString = 'DefaultEndpointsProtocol=https;AccountName=${storage.name};EndpointSuffix=${environment().suffixes.storage};AccountKey=${storage.listKeys().keys[0].value}'

resource functionsApp 'Microsoft.Web/sites@2024-04-01' = {
  name: functionsAppName
  location: location
  kind: 'functionapp,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: appServicePlanId
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNET-ISOLATED|10.0'
      alwaysOn: true
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      appSettings: [
        {
          name: 'AzureWebJobsStorage'
          value: storageConnectionString
        }
        {
          name: 'FUNCTIONS_EXTENSION_VERSION'
          value: '~4'
        }
        {
          name: 'FUNCTIONS_WORKER_RUNTIME'
          value: 'dotnet-isolated'
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsightsConnectionString
        }
        {
          name: 'ConnectionStrings__Postgres'
          value: postgresConnectionString
        }
      ]
    }
  }
}

output principalId string = functionsApp.identity.principalId
output storageAccountName string = storage.name
