// Derived Key Vault secrets module — writes the Key Vault secrets Bicep can
// DERIVE from resources it already creates or from deterministic config, with
// NO external secret value in source. New to Makables (ported/adapted from
// Cleansia's derivedSecrets.bicep). Per ADR 0023 §4 (Key Vault) and the
// no-secrets-in-source rule.
//
// Secrets written here (all use the `--` Key Vault delimiter that maps to the
// .NET `:` config separator):
//   - Storage--ConnectionString        : built from the blob account's access key
//                                         (listKeys — read at deploy time, never
//                                         persisted to source or an output).
//   - OutboxQueues--ConnectionString   : built from the FUNCTIONS storage account
//                                         key (that account doubles as the outbox
//                                         queue store, ADR 0020).
//   - ConnectionStrings--Postgres      : built from the Postgres FQDN + admin
//                                         login + the @secure() admin password.
//   - Jwt--Issuer                      : deterministic per-environment config.
//   - Jwt--Audience                    : constant config value.
//
// The remaining Makables secrets are EXTERNAL — Bicep cannot know them — so
// they are NOT written here; the deploy workflow's "Push external secrets"
// step pushes them from GitHub Environment secrets:
//   Jwt--SigningKeyBase64, SendGrid--ApiKey, Comgate--MerchantId,
//   Comgate--Secret, Packeta--ApiKey, Packeta--PublicWidgetKey,
//   Mapbox--AccessToken.
//
// T-0134 status: WIRED from main.bicep (the `derivedSecrets` module block),
// ordered after key-vault/blob/functions/postgres via output references. The
// module references the vault + storage accounts as `existing`. Validate any
// change with `az deployment group what-if` before a prod apply.

@description('Name of the Key Vault these secrets are written into.')
param keyVaultName string

@description('Blob storage account name whose access key builds the Storage connection string.')
param storageAccountName string

@description('Functions/outbox-queue storage account name whose access key builds the OutboxQueues connection string (ADR 0020 — the Functions runtime account doubles as the outbox queue store; separate from the blob account).')
param functionsStorageAccountName string

@description('PostgreSQL fully-qualified domain name (the DB connection-string host).')
param postgresFqdn string

@description('PostgreSQL admin login (non-secret).')
param postgresAdministratorLogin string

@description('PostgreSQL admin password (@secure() — supplied at deploy time, never committed).')
@secure()
param postgresAdministratorPassword string

@description('The application database name. Matches postgres.bicep (Database=makables).')
param databaseName string = 'makables'

@description('JWT issuer (Jwt:Issuer) — deterministic per environment.')
param jwtIssuer string

@description('JWT audience (Jwt:Audience) — a constant config value.')
param jwtAudience string = 'makables'

resource keyVault 'Microsoft.KeyVault/vaults@2024-12-01-preview' existing = {
  name: keyVaultName
}

resource storageAccount 'Microsoft.Storage/storageAccounts@2024-01-01' existing = {
  name: storageAccountName
}

resource functionsStorageAccount 'Microsoft.Storage/storageAccounts@2024-01-01' existing = {
  name: functionsStorageAccountName
}

// Storage connection string from the blob account's primary key (read at deploy
// time via listKeys; never persisted to source or an output). Same shape the
// blob.bicep module emits as its secure `connectionString` output.
var storageConnectionString = 'DefaultEndpointsProtocol=https;AccountName=${storageAccountName};EndpointSuffix=${environment().suffixes.storage};AccountKey=${storageAccount.listKeys().keys[0].value}'

// Npgsql connection string — same shape main.bicep / app-service.bicep already
// use (Host/Database/Username/Password + SslMode=Require). The password is
// alphanumeric-only by runbook, so no escaping is required.
var dbConnectionString = 'Host=${postgresFqdn};Database=${databaseName};Username=${postgresAdministratorLogin};Password=${postgresAdministratorPassword};SslMode=Require'

// Outbox-queue connection string from the FUNCTIONS storage account key — the
// Web hosts' StorageQueueOutboxPublisher and the Functions queue consumers use
// it via OutboxQueues:ConnectionString (OutboxQueueOptions supports only the
// connection-string shape, unlike BlobStorage which could use ServiceUri).
var outboxQueuesConnectionString = 'DefaultEndpointsProtocol=https;AccountName=${functionsStorageAccountName};EndpointSuffix=${environment().suffixes.storage};AccountKey=${functionsStorageAccount.listKeys().keys[0].value}'

resource storageConnSecret 'Microsoft.KeyVault/vaults/secrets@2024-12-01-preview' = {
  parent: keyVault
  name: 'Storage--ConnectionString'
  properties: {
    value: storageConnectionString
  }
}

resource outboxQueuesConnSecret 'Microsoft.KeyVault/vaults/secrets@2024-12-01-preview' = {
  parent: keyVault
  name: 'OutboxQueues--ConnectionString'
  properties: {
    value: outboxQueuesConnectionString
  }
}

resource dbConnSecret 'Microsoft.KeyVault/vaults/secrets@2024-12-01-preview' = {
  parent: keyVault
  name: 'ConnectionStrings--Postgres'
  properties: {
    value: dbConnectionString
  }
}

resource jwtIssuerSecret 'Microsoft.KeyVault/vaults/secrets@2024-12-01-preview' = {
  parent: keyVault
  name: 'Jwt--Issuer'
  properties: {
    value: jwtIssuer
  }
}

resource jwtAudienceSecret 'Microsoft.KeyVault/vaults/secrets@2024-12-01-preview' = {
  parent: keyVault
  name: 'Jwt--Audience'
  properties: {
    value: jwtAudience
  }
}
