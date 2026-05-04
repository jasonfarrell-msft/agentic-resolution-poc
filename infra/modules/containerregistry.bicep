param name string
param location string
param tags object = {}

// Principal IDs that need AcrPull (Container App managed identities)
param webhookPrincipalId string
param apiPrincipalId string
param mcpPrincipalId string

// AcrPull built-in role
var acrPullRoleId = '7f951dda-4ed3-4680-a7ca-43fe172d538d'

resource acr 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: name
  location: location
  tags: tags
  sku: {
    name: 'Basic'
  }
  properties: {
    adminUserEnabled: false
  }
}

resource acrPullWebhook 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(acr.id, webhookPrincipalId, 'acr-pull-webhook')
  scope: acr
  properties: {
    principalId: webhookPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', acrPullRoleId)
  }
}

resource acrPullApi 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(acr.id, apiPrincipalId, 'acr-pull-api')
  scope: acr
  properties: {
    principalId: apiPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', acrPullRoleId)
  }
}

resource acrPullMcp 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(acr.id, mcpPrincipalId, 'acr-pull-mcp')
  scope: acr
  properties: {
    principalId: mcpPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', acrPullRoleId)
  }
}

output registryName string = acr.name
output loginServer string = acr.properties.loginServer
output registryId string = acr.id
