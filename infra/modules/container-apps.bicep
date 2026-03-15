param environmentName string
param location string
param resourceSuffix string
param sqlConnectionString string

resource containerAppsEnvironment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: 'velo-cae-${environmentName}-${resourceSuffix}'
  location: location
  properties: {}
}

resource apiApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'velo-api-${environmentName}-${resourceSuffix}'
  location: location
  identity: { type: 'SystemAssigned' }
  properties: {
    managedEnvironmentId: containerAppsEnvironment.id
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
        transport: 'http'
        corsPolicy: {
          allowedOrigins: ['https://dev.azure.com', 'https://*.visualstudio.com']
        }
      }
    }
    template: {
      scale: {
        minReplicas: 0  // Scale to zero between workloads
        maxReplicas: 10
        rules: [{ name: 'http-rule', http: { metadata: { concurrentRequests: '50' } } }]
      }
      containers: [
        {
          name: 'velo-api'
          image: 'mcr.microsoft.com/dotnet/aspnet:9.0'  // Replace with ACR image
          env: [
            { name: 'ConnectionStrings__VeloDb', value: sqlConnectionString }
            { name: 'ASPNETCORE_ENVIRONMENT', value: environmentName }
          ]
          resources: { cpu: '0.5', memory: '1Gi' }
        }
      ]
    }
  }
}

output apiUrl string = 'https://${apiApp.properties.configuration.ingress.fqdn}'
output principalId string = apiApp.identity.principalId
