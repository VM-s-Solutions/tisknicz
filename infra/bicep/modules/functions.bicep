// Azure Functions module — hosts the outbox processor, timer jobs, and
// queue-triggered work per ADR 0020. Single Functions app for the MVP.

@description('Functions app name, e.g. makables-dev-functions.')
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

@description('Blob storage account connection string (AzureBlobStorage:ConnectionString). NOT ServiceUri — App Service/Functions blocks app settings ending in the reserved __ServiceUri suffix.')
@secure()
param blobConnectionString string

// --- Provider secrets the Functions host validates via ValidateOnStart ---
// Makables.Functions/Program.cs calls AddMakablesClients + AddMakablesBlobStorage,
// so it shares the Web hosts' provider requirements (it does NOT call
// AddMakablesAuth, so no Jwt key here). Sourced from GitHub Actions secrets.

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

// Timer NCRONTAB schedules (6-field). These are %key% binding expressions on
// the [TimerTrigger] attributes with NO in-code fallback — a missing key fails
// function INDEXING at host startup. Defaults match the canonical per-ticket
// values in Makables.Functions/local.settings.json + docs/deployment/env-vars.md.
@description('ProcessOutbox timer (every 30s). T-0029.')
param processOutboxSchedule string = '*/30 * * * * *'

@description('AutoDeliverOrders timer (daily 08:00 UTC). T-0077.')
param autoDeliverOrdersSchedule string = '0 0 8 * * *'

@description('SyncShipmentStatuses timer (every 6h). T-0078.')
param syncShipmentStatusesSchedule string = '0 0 0,6,12,18 * * *'

@description('CancelExpiredPendingPaymentOrders timer (daily 02:00 UTC). T-0083.')
param cancelExpiredOrdersSchedule string = '0 0 2 * * *'

@description('RunWeeklyPayoutBatch timer (Monday 02:00 UTC). T-0104.')
param runWeeklyPayoutBatchSchedule string = '0 0 2 * * 1'

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
        {
          name: 'BlobStorage__ConnectionString'
          value: blobConnectionString
        }
        // Outbox queues: this Functions storage account doubles as the queue
        // store (ADR 0020 — Functions + publisher share one account). The
        // three queue names are %key% bindings on the queue-triggered
        // functions; a missing one fails indexing.
        {
          name: 'OutboxQueues__ConnectionString'
          value: storageConnectionString
        }
        {
          name: 'OutboxQueues__SendEmailQueueName'
          value: 'send-email'
        }
        {
          name: 'OutboxQueues__GenerateInvoiceQueueName'
          value: 'generate-invoice'
        }
        {
          name: 'OutboxQueues__GenerateLabelQueueName'
          value: 'generate-label'
        }
        // Timer schedules (%key% bindings — missing key fails indexing).
        {
          name: 'ProcessOutbox__Schedule'
          value: processOutboxSchedule
        }
        {
          name: 'AutoDeliverOrders__Schedule'
          value: autoDeliverOrdersSchedule
        }
        {
          name: 'SyncShipmentStatuses__Schedule'
          value: syncShipmentStatusesSchedule
        }
        {
          name: 'CancelExpiredPendingPaymentOrders__Schedule'
          value: cancelExpiredOrdersSchedule
        }
        {
          name: 'RunWeeklyPayoutBatch__Schedule'
          value: runWeeklyPayoutBatchSchedule
        }
        // Provider secrets (ValidateOnStart on the Functions host).
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
    }
  }
}

output principalId string = functionsApp.identity.principalId
output storageAccountName string = storage.name

// Secure: the same storage account is the outbox queue store (ADR 0020 —
// Functions + the Web-host publisher share one account). The Web hosts need
// this connection string for OutboxQueues:ConnectionString to enqueue.
@secure()
output queuesConnectionString string = storageConnectionString
