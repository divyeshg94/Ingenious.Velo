param environmentName string
param location string
param resourceSuffix string
param sqlConnectionString string

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: 'velostorage${resourceSuffix}'
  location: location
  sku: { name: 'Standard_LRS' }
  kind: 'StorageV2'
}

resource hostingPlan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: 'velo-functions-plan-${environmentName}-${resourceSuffix}'
  location: location
  sku: { name: 'Y1', tier: 'Dynamic' }
  properties: {}
}

resource functionApp 'Microsoft.Web/sites@2023-12-01' = {
  name: 'velo-functions-${environmentName}-${resourceSuffix}'
  location: location
  kind: 'functionapp'
  identity: { type: 'SystemAssigned' }
  properties: {
    serverFarmId: hostingPlan.id
    siteConfig: {
      appSettings: [
        { name: 'AzureWebJobsStorage', value: 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};...' }
        { name: 'FUNCTIONS_EXTENSION_VERSION', value: '~4' }
        { name: 'FUNCTIONS_WORKER_RUNTIME', value: 'dotnet-isolated' }
        { name: 'VeloDb', value: sqlConnectionString }
        { name: 'METRICS_COMPUTE_SCHEDULE', value: '0 0 * * * *' }  // hourly
      ]
    }
  }
}

output functionAppName string = functionApp.name
output principalId string = functionApp.identity.principalId
