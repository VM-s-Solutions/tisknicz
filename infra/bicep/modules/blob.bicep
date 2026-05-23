// Blob storage module — holds product images, order attachments, invoices,
// and labels per ADR 0011. Containers have per-purpose access policy.

@description('Storage account name (must be globally unique, 3–24 chars, lowercase letters and digits).')
param storageAccountName string

param location string = resourceGroup().location

resource storage 'Microsoft.Storage/storageAccounts@2024-01-01' = {
  name: storageAccountName
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    allowBlobPublicAccess: true
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2024-01-01' = {
  parent: storage
  name: 'default'
}

// Container map per ADR 0011: product-images public read, the rest private.
var containers = [
  { name: 'product-images', publicAccess: 'Blob' }
  { name: 'order-attachments', publicAccess: 'None' }
  { name: 'invoices', publicAccess: 'None' }
  { name: 'labels', publicAccess: 'None' }
]

resource containerResources 'Microsoft.Storage/storageAccounts/blobServices/containers@2024-01-01' = [for c in containers: {
  parent: blobService
  name: c.name
  properties: {
    publicAccess: c.publicAccess
  }
}]

output storageAccountName string = storage.name
