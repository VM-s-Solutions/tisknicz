// Postgres Flexible Server module.
//
// Per ADR 0023 §3 (availability) we run a single-region West Europe deployment
// at MVP scale. SKU defaults match the year-1 scale assumptions (up to 5 GB
// database) — staging stays on B1ms, production on B2s. Both expose the
// admin user; the password is supplied by the parent template from Key Vault.

@description('Logical name of the Postgres server, e.g. pg-makables-weu-dev.')
param serverName string

@description('Azure region — defaults to West Europe per ADR 0023.')
param location string = resourceGroup().location

@description('SKU name — Standard_B1ms for staging, Standard_B2s for production.')
param skuName string = 'Standard_B1ms'

@description('SKU tier — Burstable / GeneralPurpose.')
param skuTier string = 'Burstable'

@description('Allocated storage in GB. Year-1 scale assumes 5 GB but the SKU minimum is 32.')
param storageGb int = 32

@description('Postgres admin user.')
param administratorLogin string

@description('Postgres admin password — supplied by the parent template from Key Vault.')
@secure()
param administratorLoginPassword string

@description('Azure AD object IDs that get pg admin via Microsoft Entra. Empty in staging by default.')
param entraAdminObjectIds array = []

@description('When true (staging), opens the server to any Azure tenant. Production must run with false and route via private endpoint or VNet rules.')
param allowAllAzureServices bool = false

resource server 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' = {
  name: serverName
  location: location
  sku: {
    name: skuName
    tier: skuTier
  }
  properties: {
    version: '16'
    administratorLogin: administratorLogin
    administratorLoginPassword: administratorLoginPassword
    storage: {
      storageSizeGB: storageGb
      autoGrow: 'Enabled'
    }
    backup: {
      backupRetentionDays: 14
      geoRedundantBackup: 'Disabled'
    }
    highAvailability: {
      mode: 'Disabled'
    }
    network: {
      publicNetworkAccess: 'Enabled'
    }
    authConfig: {
      activeDirectoryAuth: 'Enabled'
      passwordAuth: 'Enabled'
      tenantId: subscription().tenantId
    }
  }
}

// The application database. The connection string (in main.bicep) targets
// Database=makables; a fresh Flexible Server only provisions the default
// `postgres` DB, so without this resource the first app connection fails
// with 3D000 ("database makables does not exist"). Created empty here; the
// EF Core migrations are applied by the deploy pipeline's migrate step.
resource database 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2024-08-01' = {
  parent: server
  name: 'makables'
  properties: {
    charset: 'UTF8'
    collation: 'en_US.utf8'
  }
}

// Enforce TLS at the SERVER, not just the client connection string. The app
// connects with SslMode=Require, but require_secure_transport=on makes the
// server reject any plaintext connection regardless of client config.
resource requireSsl 'Microsoft.DBforPostgreSQL/flexibleServers/configurations@2024-08-01' = {
  parent: server
  name: 'require_secure_transport'
  properties: {
    value: 'on'
    source: 'user-override'
  }
}

// Dev-only: open to any Azure tenant — which means any Azure resource in any
// customer's subscription, so it is emphatically not a production posture.
// Production runs WITHOUT this rule and reaches the server over the private
// endpoint created by modules/network.bicep. That endpoint attaches alongside
// public access (the two coexist by design); this module's network block is
// deliberately never touched, because Flexible Server's networking mode is
// fixed at creation.
resource allowAzureServices 'Microsoft.DBforPostgreSQL/flexibleServers/firewallRules@2024-08-01' = if (allowAllAzureServices) {
  parent: server
  name: 'AllowAllAzureServicesAndResourcesWithinAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource entraAdmins 'Microsoft.DBforPostgreSQL/flexibleServers/administrators@2024-08-01' = [for (id, i) in entraAdminObjectIds: {
  parent: server
  name: id
  properties: {
    principalType: 'User'
    principalName: 'admin-${i}'
    tenantId: subscription().tenantId
  }
}]

@description('Resource id — consumed by modules/network.bicep to attach a private endpoint. Exposing it changes nothing about the server itself; the network block above is deliberately never touched, because Flexible Server networking mode is fixed at creation.')
output serverId string = server.id

output serverFqdn string = server.properties.fullyQualifiedDomainName
output serverName string = server.name
output databaseName string = database.name
