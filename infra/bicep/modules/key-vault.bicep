// Key Vault module — holds Comgate, SendGrid, JWT signing keys, Packeta API
// keys, Mapbox token, Postgres admin connection string, and the Storage /
// outbox-queue connection strings. Per ADR 0023 §4.
//
// RBAC-mode vault holding the platform's secret NAMES only. NO real secret
// value is ever committed: the owner (or a Secrets-Officer CI step) populates
// values out of band via the portal / `az keyvault secret set`. The four Web
// hosts (Customer / Maker / Admin / Public) + the Functions app read via their
// system-assigned managed identity (Key Vault Secrets User, granted below and
// gated on `grantReaderRoles`); access policies are disabled in favour of
// `enableRbacAuthorization`.
//
// ─────────────────────────────────────────────────────────────────────────────
// WIRING CONTRACT (for main.bicep):
//   params IN:
//     keyVaultName        string   globally-unique, 3–24 chars (e.g. kv-makables-weu-dev)
//     readerPrincipalIds  array    default [] — unused since T-0134 (grants live
//                                  in role-assignments.bicep)
//     grantReaderRoles    bool     default false — see param doc + T-0138
//     createEmptySecretPlaceholders bool  default false — FIRST-provision only
//     location            string   default resourceGroup().location
//   outputs OUT:
//     keyVaultUri   string  → main.bicep composes @Microsoft.KeyVault(SecretUri=...)
//     keyVaultId    string
//     keyVaultName  string
//     secretNames   array   the Makables secret inventory, for main.bicep to
//                           build per-secret Key Vault reference URIs
//
//   T-0134 status: WIRED. main.bicep invokes this module with keyVaultName only
//   (no principals — reader grants moved to role-assignments.bicep, which runs
//   after the apps exist). Host secret app settings ARE Key Vault references
//   composed in main.bicep via kvRef(); derived secrets are written by
//   derived-secrets.bicep and external secrets pushed by the deploy workflow's
//   "Push external secrets" step. The deploy identity must hold
//   roleAssignments/write (User Access Administrator / Owner) on the RG.
//   Validate any change with `az deployment group what-if` before a prod apply.
// ─────────────────────────────────────────────────────────────────────────────

@description('Key Vault name (globally unique, 3–24 chars).')
param keyVaultName string

@description('Object IDs (App Services + Functions managed identities) that need read access. Default empty: since T-0134 the reader grants live in role-assignments.bicep (which runs AFTER the apps exist), so main.bicep no longer passes principals here — that also breaks the old keyVault<->apps parameter cycle.')
param readerPrincipalIds array = []

@description('When true, grant the reader principals the Key Vault Secrets User role. Requires the DEPLOYER to have Microsoft.Authorization/roleAssignments/write (User Access Administrator / Owner) on the scope. Default false: the hosts inject secrets as direct app settings today (T-0138), so KV is empty and no read role is needed. Flip true when secrets move to Key Vault references AND the deploy identity has role-assignment rights.')
param grantReaderRoles bool = false

@description('''
Pre-create each secret as an EMPTY placeholder so App Service Key Vault references
resolve before the owner populates values. OFF by default: a re-run would clobber
owner-set values back to empty, so the idempotent path is owner-creates-the-secret
and Bicep only emits the vault + RBAC + the name list. Only the FIRST provision
should ever run with this true.
''')
param createEmptySecretPlaceholders bool = false

param location string = resourceGroup().location

// Secret NAMES the four Web hosts + Functions read. Values are owner-populated
// post-deploy — NO value is ever committed. Names use the '--' delimiter that
// Azure App Service / Key Vault references map to the .NET config ':' separator
// (e.g. 'Jwt--SigningKeyBase64' → Jwt:SigningKeyBase64). This inventory mirrors
// the @secure() params threaded through app-service.bicep / functions.bicep and
// the Postgres/Storage connection strings composed in main.bicep.
var secretNames = [
  'Jwt--SigningKeyBase64'
  'Jwt--Issuer'
  'Jwt--Audience'
  'SendGrid--ApiKey'
  'Comgate--MerchantId'
  'Comgate--Secret'
  'Packeta--ApiKey'
  'Packeta--PublicWidgetKey'
  'Mapbox--AccessToken'
  'ConnectionStrings--Postgres'
  'Storage--ConnectionString'
  'OutboxQueues--ConnectionString'
]

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
    // Purge protection is irreversible once on, so keep it OFF outside prod so
    // throwaway staging vaults can be fully purged and recreated. Prod (name
    // suffix '-prod') switches it on so an accidental delete is recoverable.
    enablePurgeProtection: endsWith(keyVaultName, '-prod') ? true : null
    publicNetworkAccess: 'Enabled'
    networkAcls: {
      bypass: 'AzureServices'
      defaultAction: 'Allow'
    }
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

// Optional empty placeholders (default OFF). When enabled, only the FIRST
// provision should run with this true — a later re-run with it true overwrites
// owner-populated values with empty. No real secret material is ever in source:
// the value is the empty string; the owner sets the real value out of band.
resource secrets 'Microsoft.KeyVault/vaults/secrets@2024-12-01-preview' = [
  for name in (createEmptySecretPlaceholders ? secretNames : []): {
    parent: keyVault
    name: name
    properties: {
      value: ''
      attributes: {
        enabled: true
      }
    }
  }
]

output keyVaultUri string = keyVault.properties.vaultUri
output keyVaultId string = keyVault.id
output keyVaultName string = keyVault.name

@description('Secret names declared in the vault — for main.bicep to compose Key Vault reference URIs.')
output secretNames array = secretNames
