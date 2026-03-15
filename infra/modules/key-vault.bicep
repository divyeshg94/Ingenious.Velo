param environmentName string
param location string
param resourceSuffix string

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: 'velo-kv-${environmentName}-${resourceSuffix}'
  location: location
  properties: {
    sku: { family: 'A', name: 'standard' }
    tenantId: subscription().tenantId
    enableRbacAuthorization: true  // Use RBAC, not access policies
    enableSoftDelete: true
    softDeleteRetentionInDays: 30
  }
}

output keyVaultUri string = keyVault.properties.vaultUri
output keyVaultName string = keyVault.name
