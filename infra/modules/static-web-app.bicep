param environmentName string
param location string
param resourceSuffix string

resource staticWebApp 'Microsoft.Web/staticSites@2023-12-01' = {
  name: 'velo-swa-${environmentName}-${resourceSuffix}'
  location: location
  sku: { name: 'Free', tier: 'Free' }
  properties: {}
}

output staticWebAppUrl string = 'https://${staticWebApp.properties.defaultHostname}'
