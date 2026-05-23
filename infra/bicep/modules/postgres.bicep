// Postgres Flexible Server module.
//
// Per ADR 0023 §3 (availability) we run a single-region West Europe deployment
// at MVP scale. SKU defaults match the year-1 scale assumptions (up to 5 GB
// database) — staging stays on B1ms, production on B2s. Both expose the
// admin user; the password is supplied by the parent template from Key Vault.

@description('Logical name of the Postgres server, e.g. makables-stg-pg.')
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

// Allow Azure services (App Services, Functions) to reach the server.
// Production should replace this with private endpoints; tracked as a
// follow-up in the runbook (T-0134).
resource allowAzureServices 'Microsoft.DBforPostgreSQL/flexibleServers/firewallRules@2024-08-01' = {
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

output serverFqdn string = server.properties.fullyQualifiedDomainName
output serverName string = server.name
