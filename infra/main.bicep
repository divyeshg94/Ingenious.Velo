targetScope = 'resourceGroup'

@description('Environment name: dev, staging, prod')
param environmentName string

@description('Azure region for all resources')
param location string = resourceGroup().location

@description('Unique suffix to avoid naming collisions')
param resourceSuffix string = uniqueString(resourceGroup().id)

module sql 'modules/sql.bicep' = {
  name: 'sql'
  params: {
    environmentName: environmentName
    location: location
    resourceSuffix: resourceSuffix
  }
}

module functions 'modules/functions.bicep' = {
  name: 'functions'
  params: {
    environmentName: environmentName
    location: location
    resourceSuffix: resourceSuffix
    sqlConnectionString: sql.outputs.connectionString
  }
}

module containerApps 'modules/container-apps.bicep' = {
  name: 'containerApps'
  params: {
    environmentName: environmentName
    location: location
    resourceSuffix: resourceSuffix
    sqlConnectionString: sql.outputs.connectionString
  }
}

module staticWebApp 'modules/static-web-app.bicep' = {
  name: 'staticWebApp'
  params: {
    environmentName: environmentName
    location: location
    resourceSuffix: resourceSuffix
  }
}

module keyVault 'modules/key-vault.bicep' = {
  name: 'keyVault'
  params: {
    environmentName: environmentName
    location: location
    resourceSuffix: resourceSuffix
  }
}

module monitor 'modules/monitor.bicep' = {
  name: 'monitor'
  params: {
    environmentName: environmentName
    location: location
    resourceSuffix: resourceSuffix
  }
}

module foundry 'modules/foundry.bicep' = {
  name: 'foundry'
  params: {
    environmentName: environmentName
    location: location
    resourceSuffix: resourceSuffix
  }
}
