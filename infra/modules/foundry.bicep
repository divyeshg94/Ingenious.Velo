param environmentName string
param location string
param resourceSuffix string

// Microsoft Foundry (Azure AI Foundry) resource provisioning
// Requires: Microsoft.CognitiveServices and Microsoft.MachineLearningServices providers

resource aiHub 'Microsoft.MachineLearningServices/workspaces@2024-07-01-preview' = {
  name: 'velo-foundry-${environmentName}-${resourceSuffix}'
  location: location
  kind: 'Hub'
  identity: { type: 'SystemAssigned' }
  properties: {
    description: 'Velo Pipeline Intelligence Agent - Foundry Hub'
    friendlyName: 'Velo Foundry Hub'
  }
}

resource aiProject 'Microsoft.MachineLearningServices/workspaces@2024-07-01-preview' = {
  name: 'velo-foundry-project-${environmentName}'
  location: location
  kind: 'Project'
  identity: { type: 'SystemAssigned' }
  properties: {
    hubResourceId: aiHub.id
    friendlyName: 'Velo Agent Project'
  }
}

output foundryEndpoint string = aiProject.properties.workspaceHubConfig.?additionalWorkspaceStorageAccounts ?? ''
output projectName string = aiProject.name
