param environmentName string
param location string
param resourceSuffix string

resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: 'velo-sql-${environmentName}-${resourceSuffix}'
  location: location
  identity: { type: 'SystemAssigned' }
  properties: {
    administrators: {
      administratorType: 'ActiveDirectory'
      azureADOnlyAuthentication: true
    }
  }
}

resource sqlDatabase 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: 'VeloDb'
  location: location
  sku: {
    name: 'GP_S_Gen5'
    tier: 'GeneralPurpose'
    family: 'Gen5'
    capacity: 1
  }
  properties: {
    // Serverless: auto-pause after 60 minutes of inactivity (~$2/month idle)
    autoPauseDelay: 60
    minCapacity: '0.5'
    collation: 'SQL_Latin1_General_CP1_CI_AS'
    requestedBackupStorageRedundancy: 'Zone'
  }
}

output connectionString string = 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Database=VeloDb;Authentication=Active Directory Managed Identity;'
output serverName string = sqlServer.name
