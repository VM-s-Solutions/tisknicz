// Managed-identity role grants that wire Makables' hosts to Key Vault + Storage
// (ported from Cleansia deploy/bicep/modules/roleAssignments.bicep, adapted to
// Makables' 4-Web-host + Functions topology — ADR 0005, ADR 0011, ADR 0020).
//
// NEW to Makables. Grants every consuming managed identity:
//   * Key Vault Secrets User          (read secret values, never manage them)
//   * Storage Blob Data Contributor   (the MI path DefaultAzureCredential prefers)
//   * Storage Queue Data Contributor  (the outbox queue -> Functions pipeline)
//
// Least privilege: the hosts get Secrets *User* (read), never Officer. Role
// assignment names are deterministic guids, so a re-provision is idempotent (the
// ARM control plane treats the same guid as the same assignment — no duplicates).
//
// Makables has NO container registry: the Functions host runs DOTNET-ISOLATED|10.0
// directly (see modules/functions.bicep), not a pulled image, so Cleansia's AcrPull
// grant is intentionally dropped. There is no SSR host in the MI set either — the
// Next.js frontend (modules/web-app.bicep) is a pure presentation Node App Service
// that never touches Key Vault or Storage, so it is not granted anything here.
//
// -----------------------------------------------------------------------------
// T-0134 status: WIRED from main.bicep (the `roleAssignments` module block),
// with blob.outputs.storageAccountId + functions.outputs.storageAccountId and
// the four Web host + Functions principal ids. This module now owns ALL host
// RBAC grants (key-vault.bicep's grantReaderRoles stays false/unused).
//
// The Functions MI additionally gets Storage Blob Data OWNER on its own storage
// account: identity-based AzureWebJobsStorage (functions.bicep) requires Owner
// (not just Contributor) for the host's coordination blobs per the Functions
// identity-connection docs; queue triggers ride the Queue Data Contributor
// grant below.
//
// DEPLOY IDENTITY: needs Microsoft.Authorization/roleAssignments/write on the
// resource group (User Access Administrator or Owner) — a plain Contributor
// deploy principal FAILS this module. Validate changes with
// `az deployment group what-if -g <rg> -f infra/bicep/main.bicep -p envs/<env>.bicepparam`.
// -----------------------------------------------------------------------------

@description('Resource id of the Key Vault the host identities read secrets from.')
param keyVaultId string

@description('Resource ids of the Storage Accounts the host identities use for blob + queue. Pass the blob account AND the Functions/outbox-queue account (ADR 0020 — they are separate accounts in Makables).')
param storageAccountIds array

@description('System-assigned managed-identity principal ids of the four Web API hosts (Customer / Maker / Admin / Public) that read Key Vault + Storage.')
param webPrincipalIds array

@description('System-assigned managed-identity principal id of the Azure Functions host (Key Vault + Storage — outbox processor, timers, queue triggers). Empty string skips it.')
param functionsPrincipalId string = ''

@description('Resource id of the Functions host storage account. When set (with functionsPrincipalId), the Functions MI gets Storage Blob Data OWNER on it — required for identity-based AzureWebJobsStorage host coordination. Empty string skips the grant.')
param functionsStorageAccountId string = ''

// Built-in role definition ids (stable, tenant-independent).
var roleIds = {
  keyVaultSecretsUser: '4633458b-17de-408a-b874-0445c86b69e6'
  storageBlobDataContributor: 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
  storageBlobDataOwner: 'b7e6dc6d-f1e8-4753-8033-0f276bb0955b'
  storageQueueDataContributor: '974c5e8b-45b9-4653-ba55-5f855dd0fb88'
}

// Every host MI that consumes Key Vault + Storage: the four Web hosts, plus the
// Functions MI when supplied. The optional Functions id is filtered out when
// empty so no empty principalId ever reaches a role assignment.
var functionsPrincipals = filter([functionsPrincipalId], id => !empty(id))
var consumerPrincipalIds = concat(webPrincipalIds, functionsPrincipals)

resource keyVault 'Microsoft.KeyVault/vaults@2024-12-01-preview' existing = {
  name: last(split(keyVaultId, '/'))
}

resource storageAccounts 'Microsoft.Storage/storageAccounts@2024-01-01' existing = [
  for id in storageAccountIds: {
    name: last(split(id, '/'))
  }
]

// Host + Functions identities -> Key Vault Secrets User (read secret values, not manage them).
resource kvSecretsUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = [
  for principalId in consumerPrincipalIds: {
    name: guid(keyVault.id, principalId, roleIds.keyVaultSecretsUser)
    scope: keyVault
    properties: {
      roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleIds.keyVaultSecretsUser)
      principalId: principalId
      principalType: 'ServicePrincipal'
    }
  }
]

// Host + Functions identities -> Storage Blob Data Contributor, on every storage
// account (blob account + Functions/outbox-queue account). Flattened to one loop
// over the account x principal cross product so each pair gets a deterministic guid.
resource blobDataContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = [
  for pair in flattenAccountPrincipalPairs: {
    name: guid(storageAccounts[pair.accountIndex].id, pair.principalId, roleIds.storageBlobDataContributor)
    scope: storageAccounts[pair.accountIndex]
    properties: {
      roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleIds.storageBlobDataContributor)
      principalId: pair.principalId
      principalType: 'ServicePrincipal'
    }
  }
]

// Host + Functions identities -> Storage Queue Data Contributor (the outbox
// queue -> Functions pipeline), on every storage account.
resource queueDataContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = [
  for pair in flattenAccountPrincipalPairs: {
    name: guid(storageAccounts[pair.accountIndex].id, pair.principalId, roleIds.storageQueueDataContributor)
    scope: storageAccounts[pair.accountIndex]
    properties: {
      roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleIds.storageQueueDataContributor)
      principalId: pair.principalId
      principalType: 'ServicePrincipal'
    }
  }
]

// Cross product of (storage account index) x (consumer principal), so the two
// storage-data-role loops above can each be a single flat for-loop. Bicep does
// not allow nested for-expressions in a variable, so the cross product is built
// with map() lambdas + flatten() instead.
var flattenAccountPrincipalPairs = flatten(map(
  range(0, length(storageAccountIds)),
  accountIndex => map(consumerPrincipalIds, principalId => {
    accountIndex: accountIndex
    principalId: principalId
  })
))

// Functions MI -> Storage Blob Data OWNER on the Functions host storage account.
// Identity-based AzureWebJobsStorage (functions.bicep) needs Owner for the
// host's coordination blobs (timer leases, host locks) — Contributor is not
// sufficient per the Functions identity-connection docs.
resource functionsHostStorage 'Microsoft.Storage/storageAccounts@2024-01-01' existing = if (!empty(functionsStorageAccountId) && !empty(functionsPrincipalId)) {
  name: last(split(functionsStorageAccountId, '/'))
}

resource functionsBlobDataOwner 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(functionsStorageAccountId) && !empty(functionsPrincipalId)) {
  name: guid(functionsStorageAccountId, functionsPrincipalId, roleIds.storageBlobDataOwner)
  scope: functionsHostStorage
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleIds.storageBlobDataOwner)
    principalId: functionsPrincipalId
    principalType: 'ServicePrincipal'
  }
}
