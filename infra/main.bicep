targetScope = 'subscription'

@minLength(1)
@maxLength(64)
@description('Name of the environment used for resource naming')
param environmentName string

@minLength(1)
@description('Primary location for all resources')
param location string = 'eastus2'

@description('Id of the user or app to assign application roles')
param principalId string = ''

// Tags applied to all resources
var tags = {
  'azd-env-name': environmentName
}

// Use existing resource group
resource rg 'Microsoft.Resources/resourceGroups@2024-03-01' existing = {
  name: 'rg-agentic-res-src-dev'
}

// Deploy resources into the resource group
module resources './resources.bicep' = {
  name: 'resources'
  scope: rg
  params: {
    environmentName: environmentName
    location: location
    principalId: principalId
    tags: tags
  }
}

// Outputs from the deployment
output AZURE_LOCATION string = location
output AZURE_TENANT_ID string = tenant().tenantId
output WEB_APP_NAME string = resources.outputs.webAppName
output WEB_APP_HOSTNAME string = resources.outputs.webAppHostname