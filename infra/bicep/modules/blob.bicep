// Blob storage module — holds product images, order attachments, invoices,
// and labels per ADR 0011. Containers have per-purpose access policy.

@description('Storage account name (must be globally unique, 3–24 chars, lowercase letters and digits).')
param storageAccountName string

param location string = resourceGroup().location

@description('''
Replication SKU. Dev keeps Standard_LRS (cheapest; the data is disposable).
Production uses Standard_GZRS per ADR 0023 §7.

WHY GZRS AND NOT GRS, which is what §7 originally said: under GRS the copy in
the PRIMARY region is LRS — "Geo-redundant storage (GRS) copies your data
synchronously within one or more Azure availability zones in the primary region
by using LRS." West Europe has availability zones, so losing a single datacenter
takes a GRS account offline, and the only recovery is a customer-initiated
UNPLANNED failover, which Microsoft says "usually involves some amount of data
loss", converts the account to LRS and deletes the original primary. GZRS
survives the same event with no failover and no data loss. The ADR was amended
rather than silently deviated from.

The geo axis (LRS<->GRS) is a live sku update, but the ZONE axis is not: going
LRS -> GZRS later is a two-step migration (LRS -> GRS, then a zone conversion)
with a 72-hour wait between steps and no completion SLA. Production has never
been deployed, so this is the one moment the choice is free.
''')
@allowed([
  'Standard_LRS'
  'Standard_ZRS'
  'Standard_GRS'
  'Standard_GZRS'
])
param skuName string = 'Standard_LRS'

@description('Blob + container soft-delete retention in days. ADR 0023 §7 requires 30 in every environment. Azure allows 1–365.')
@minValue(1)
@maxValue(365)
param softDeleteRetentionDays int = 30

resource storage 'Microsoft.Storage/storageAccounts@2024-01-01' = {
  name: storageAccountName
  location: location
  sku: {
    name: skuName
  }
  kind: 'StorageV2'
  properties: {
    // Closed. Nothing in the codebase has ever emitted a direct
    // *.blob.core.windows.net URL — every image reaches the browser as
    // {publicApiBase}/api/v1/files/{folder}/{path}, streamed by the two
    // public-host image controllers through the credentialed SDK. The
    // anonymous container ACL was therefore a second door onto the same
    // room, used by nobody: unmetered, unlogged, and reachable from any IP
    // on an account whose name is fully derivable (stmakables<region><env>).
    //
    // It also contradicted CLAUDE.md PART 6 ("All file access proxied by the
    // backend — no direct browser → blob URLs") and ADR 0011, whose own title
    // is "all access through the backend; no direct browser links" and which
    // never sanctioned a public ACL for profile-images at all.
    //
    // The sibling Functions storage account already ships
    // allowBlobPublicAccess: false (modules/functions.bicep), deployed by the
    // same job — so the two accounts in one resource group were configured
    // inconsistently, and the false path is already proven in this pipeline.
    //
    // Setting this false is what actually closes the door: with an
    // Authorization header present, "anonymous access on the storage account
    // is ignored, and the request is authorized based on the provided
    // credentials" — so every backend read is unaffected. The per-container
    // 'None' below is Microsoft's documented companion ("Make all containers
    // private to mitigate this issue"), because an account-level block alone
    // makes a still-public container answer 403 rather than 404.
    allowBlobPublicAccess: false
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
  }
}

// Data protection, per ADR 0023 §7. Both policies live on the blobServices
// 'default' child that already had to exist as the containers' parent, so this
// adds no new resource and no new ordering edge.
//
// TWO POLICIES, NOT ONE. Blob soft delete alone does NOT cover a dropped
// container: Microsoft's own operation table says of Delete Container, "No
// change. You can't recover blobs in the deleted container." Deleting a
// container is the single worst accident available here (product-images holds
// every maker's catalogue), so containerDeleteRetentionPolicy is the half that
// actually covers the catastrophic case and it costs nothing — containers are
// created once per environment and never deleted in normal operation.
//
// DELIBERATELY NOT ENABLED: isVersioningEnabled. It reads like strictly more
// protection and is the opposite for this account. With versioning ON, deleting
// a blob produces NO soft-deleted object — "the current version becomes a
// previous version, and the current version is deleted... No new version is
// created and no soft-deleted snapshots are created" — so `az storage blob
// list --include d` returns nothing and `az storage blob undelete` silently
// restores nothing. Recovery becomes "promote a previous version by copying
// it", a different procedure from the one backup-restore.md documents. ADR 0023
// §7 asks for soft delete, not versioning, and the app's dominant loss vector
// is exactly Delete Blob (replacing a profile image, removing a product image).
// Enabling versioning here would have closed a BLOCKING launch item with a
// setting that breaks the recovery command that item exists to deliver.
//
// ALSO DELIBERATELY ABSENT: a managementPolicies lifecycle rule. It is only
// needed to bound version growth, which does not exist without versioning, and
// Microsoft warns "Don't apply lifecycle management policies to your Blob
// Storage account used by your function app" — a trap worth not building
// toward, even though the Functions host uses its own separate account.
resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2024-01-01' = {
  parent: storage
  name: 'default'
  properties: {
    deleteRetentionPolicy: {
      enabled: true
      days: softDeleteRetentionDays
    }
    containerDeleteRetentionPolicy: {
      enabled: true
      days: softDeleteRetentionDays
    }
  }
}

// Container map per ADR 0011: product-images + profile-images public read,
// the rest private.
// MUST stay in sync with BlobContainer.All in
// backend/src/Makables.Core.Domain/Storage/BlobContainer.cs — the client does
// NOT auto-create containers, so a name in code but not here is a runtime 404
// on the first blob op. (payouts added: the weekly payout CSV upload, T-0102b.
// profile-images added: maker logos + user avatars.)
// Every container is private. product-images and profile-images were 'Blob'
// (anonymous read); see the allowBlobPublicAccess note above for why that was
// closed. Their CONTENT is still served anonymously — by the backend's
// [AllowAnonymous] image controllers — so nothing user-visible changes. What
// changes is that there is now exactly one way in, which can be rate-limited,
// cached, logged and revoked. Revocation is currently only POSSIBLE, not
// implemented: neither image controller checks whether the product is still
// visible or its maker still verified (see docs/questions/open.md Q-0040).
var containers = [
  { name: 'product-images', publicAccess: 'None' }
  { name: 'order-attachments', publicAccess: 'None' }
  { name: 'invoices', publicAccess: 'None' }
  { name: 'maker-documents', publicAccess: 'None' }
  { name: 'payouts', publicAccess: 'None' }
  { name: 'profile-images', publicAccess: 'None' }
]

resource containerResources 'Microsoft.Storage/storageAccounts/blobServices/containers@2024-01-01' = [for c in containers: {
  parent: blobService
  name: c.name
  properties: {
    publicAccess: c.publicAccess
  }
}]

output storageAccountName string = storage.name
output storageAccountId string = storage.id
output blobServiceUri string = storage.properties.primaryEndpoints.blob

// Secure: blob account connection string. The hosts inject this as
// AzureBlobStorage:ConnectionString (the host prefers it over the
// managed-identity ServiceUri path). We use the connection string rather than
// ServiceUri because App Service blocks any app-setting ending in the reserved
// '__ServiceUri' suffix, and it avoids needing an RBAC role assignment on the
// blob account for the hosts' managed identities (simpler for dev).
@secure()
output connectionString string = 'DefaultEndpointsProtocol=https;AccountName=${storage.name};EndpointSuffix=${environment().suffixes.storage};AccountKey=${storage.listKeys().keys[0].value}'
