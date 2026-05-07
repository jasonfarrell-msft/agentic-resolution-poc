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

@description('Entra admin login (display name or UPN)')
param entraAdminLogin string

@description('Entra admin object ID (GUID)')
param entraAdminObjectId string

@description('Entra admin tenant ID (GUID)')
param entraAdminTenantId string

// Tags applied to all resources
var tags = {
  'azd-env-name': environmentName
}

// Create resource group with environment-based naming
resource rg 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: 'rg-${environmentName}'
  location: location
  tags: tags
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
    entraAdminLogin: entraAdminLogin
    entraAdminObjectId: entraAdminObjectId
    entraAdminTenantId: entraAdminTenantId
  }
}

// Outputs from the deployment
output AZURE_LOCATION string = location
output AZURE_TENANT_ID string = tenant().tenantId
output WEB_APP_NAME string = resources.outputs.webAppName
output WEB_APP_HOSTNAME string = resources.outputs.webAppHostname
output AZURE_KEY_VAULT_NAME string = resources.outputs.keyVaultName
output AZURE_KEY_VAULT_URI string = resources.outputs.keyVaultUri
output SQL_SERVER_NAME string = resources.outputs.sqlServerName
output SQL_SERVER_FQDN string = resources.outputs.sqlServerFqdn
output SQL_DATABASE_NAME string = resources.outputs.sqlDatabaseName
