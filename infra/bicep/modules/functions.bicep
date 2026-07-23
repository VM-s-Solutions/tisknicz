// Azure Functions module — hosts the outbox processor, timer jobs, and
// queue-triggered work per ADR 0020. Single Functions app for the MVP.
//
// SECRETS (T-0134, Cleansia pattern):
//   - AzureWebJobsStorage is IDENTITY-BASED (`__accountName` + `__credential` =
//     managedidentity) — no account key in app settings. The Functions MI needs
//     Storage Blob Data OWNER (host coordination blobs) + Queue Data Contributor
//     (queue triggers) on this storage account; both are granted in
//     role-assignments.bicep. On a FIRST provision the host may crash-loop for a
//     few minutes until RBAC propagates — it self-heals; restart to hurry it.
//   - App-level secrets (Postgres/Blob/outbox connection strings, provider keys)
//     arrive via `secretAppSettings` as Key Vault REFERENCE strings composed in
//     main.bicep. The app-level OutboxQueues:ConnectionString stays a connection
//     string (via Key Vault) because OutboxQueueOptions only supports that shape.

@description('Functions app name, e.g. func-makables-weu-dev.')
param functionsAppName string

@description('Storage account used by the Functions runtime + outbox queues.')
param storageAccountName string

@description('App Service Plan ID (shared with the Web hosts to keep MVP costs low).')
param appServicePlanId string

@description('App Insights connection string for the Functions host.')
@secure()
param appInsightsConnectionString string

@description('Secret-bearing app settings as { name, value } pairs where every value is a Key Vault REFERENCE string (no secret material). Composed in main.bicep.')
param secretAppSettings array = []

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

@description('DisputeAutoEscalation timer (daily 09:00 UTC). T-0145.')
param disputeAutoEscalationSchedule string = '0 0 9 * * *'

@description('EvictExpiredRegistryCache timer (daily 02:30 UTC, offset from CancelExpired). T-0113.')
param evictExpiredRegistryCacheSchedule string = '0 30 2 * * *'

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

var baseAppSettings = [
  // Identity-based host storage: no account key in app settings. Requires the
  // Functions MI to hold Blob Data Owner + Queue Data Contributor on this
  // account (role-assignments.bicep).
  {
    name: 'AzureWebJobsStorage__accountName'
    value: storage.name
  }
  {
    name: 'AzureWebJobsStorage__credential'
    value: 'managedidentity'
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
  // Outbox queue names: %key% bindings on the queue-triggered functions; a
  // missing one fails indexing. The queue CONNECTION string arrives via
  // secretAppSettings (Key Vault reference — derived-secrets.bicep writes it).
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
  {
    name: 'DisputeAutoEscalation__Schedule'
    value: disputeAutoEscalationSchedule
  }
  {
    name: 'EvictExpiredRegistryCache__Schedule'
    value: evictExpiredRegistryCacheSchedule
  }
]

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
      appSettings: concat(baseAppSettings, secretAppSettings)
    }
  }
}

// Container/console logs to the filesystem so Log stream shows the worker's
// stdout (startup crashes, host errors). See app-service.bicep for rationale.
resource siteLogs 'Microsoft.Web/sites/config@2024-04-01' = {
  parent: functionsApp
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

output principalId string = functionsApp.identity.principalId
output storageAccountName string = storage.name
output storageAccountId string = storage.id
