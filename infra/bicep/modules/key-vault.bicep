// Key Vault module — holds Comgate, Resend, JWT signing keys, Packeta API
// keys, Postgres admin password, etc. Per ADR 0023 §4.

@description('Key Vault name (globally unique, 3–24 chars).')
param keyVaultName string

@description('Object IDs (App Services + Functions managed identities) that need read access.')
param readerPrincipalIds array

@description('When true, grant the reader principals the Key Vault Secrets User role. Requires the DEPLOYER to have Microsoft.Authorization/roleAssignments/write (User Access Administrator / Owner) on the scope. Default false: the hosts inject secrets as direct app settings today (T-0138), so KV is empty and no read role is needed. Flip true when secrets move to Key Vault references AND the deploy identity has role-assignment rights.')
param grantReaderRoles bool = false

param location string = resourceGroup().location

resource keyVault 'Microsoft.KeyVault/vaults@2024-12-01-preview' = {
  name: keyVaultName
  location: location
  properties: {
    tenantId: subscription().tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 90
    publicNetworkAccess: 'Enabled'
  }
}

// Key Vault Secrets User role — lets App Services and Functions read secrets.
// Role definition ID is well-known: 4633458b-17de-408a-b874-0445c86b69e6.
@description('Role assignments for the reader principals (gated on grantReaderRoles — see param).')
resource readerRoleAssignments 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for (pid, idx) in readerPrincipalIds: if (grantReaderRoles && !empty(pid)) {
  name: guid(keyVault.id, pid, 'kv-secrets-user')
  scope: keyVault
  properties: {
    principalId: pid
    roleDefinitionId: resourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')
    principalType: 'ServicePrincipal'
  }
}]

output keyVaultUri string = keyVault.properties.vaultUri
output keyVaultId string = keyVault.id
