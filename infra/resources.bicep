param environmentName string
param location string
param principalId string
param tags object

@description('Deploy backend resources (Container Apps, ACR, etc.) - defaults to false')
param deployBackend bool = false

@description('URL for the .NET tickets CRUD API used by the Blazor frontend. Setup-Solution.ps1 configures this after creating the API Container App.')
param ticketsApiBaseUrl string = ''

@description('URL for the Python Resolution API used by the Blazor frontend. Setup-Solution.ps1 configures this after creating the Resolution Container App.')
param resolutionApiBaseUrl string = ''

@description('SQL Server administrator login')
param sqlAdminLogin string = 'sqladmin'

@secure()
@description('SQL Server administrator password - must be passed securely')
param sqlAdminPassword string

// ========================================
// CORE INFRASTRUCTURE: Key Vault + SQL
// ========================================

var keyVaultName = 'kv-${replace(environmentName, '-', '')}'
var sqlServerName = 'sql-${environmentName}'

module keyVault './modules/keyvault.bicep' = {
  name: 'keyvault'
  params: {
    name: keyVaultName
    location: location
    tags: tags
    principalId: principalId
  }
}

module sqlServer './modules/sqlserver.bicep' = {
  name: 'sqlserver'
  params: {
    name: sqlServerName
    location: location
    tags: tags
    administratorLogin: sqlAdminLogin
    administratorLoginPassword: sqlAdminPassword
    allowAzureServices: true
  }
}

// Reference to Key Vault for role assignments and child resources
resource kv 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

// Store SQL connection string in Key Vault
resource sqlConnectionStringSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  name: 'sql-connection-string'
  parent: kv
  properties: {
    value: 'Server=tcp:${sqlServer.outputs.serverFqdn},1433;Initial Catalog=${sqlServer.outputs.databaseName};Persist Security Info=False;User ID=${sqlAdminLogin};Password=${sqlAdminPassword};MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;'
  }
  dependsOn: [keyVault]
}

// ========================================
// FRONTEND: App Service for Blazor Web
// ========================================

resource appServicePlan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: 'plan-${environmentName}'
  location: location
  tags: tags
  sku: {
    name: 'B1'
    tier: 'Basic'
    capacity: 1
  }
  kind: 'linux'
  properties: {
    reserved: true // required for Linux
  }
}

resource webApp 'Microsoft.Web/sites@2024-04-01' = {
  name: 'app-${environmentName}-web'
  location: location
  tags: union(tags, {
    'azd-service-name': 'web'
  })
  kind: 'app,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: appServicePlan.id
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: true
      ftpsState: 'FtpsOnly'
      minTlsVersion: '1.2'
      appSettings: [
        {
          name: 'ApiClient__BaseUrl'
          value: ticketsApiBaseUrl
        }
        {
          name: 'ApiBaseUrl'
          value: ticketsApiBaseUrl
        }
        {
          name: 'ResolutionApi__BaseUrl'
          value: resolutionApiBaseUrl
        }
        {
          name: 'ASPNETCORE_ENVIRONMENT'
          value: 'Production'
        }
        {
          name: 'KeyVault__Uri'
          value: keyVault.outputs.keyVaultUri
        }
      ]
    }
    httpsOnly: true
  }
}

// Grant Web App managed identity access to Key Vault secrets
resource webAppKvRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(kv.id, webApp.id, 'kv-secrets-user')
  scope: kv
  properties: {
    principalId: webApp.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions',
      '4633458b-17de-408a-b874-0445c86b69e6'
    )
  }
}

// ========================================
// BACKEND: Container App resources (GATED)
// ========================================

// Container App Environment stub - only deployed when deployBackend=true
module containerEnv './modules/containerappenvironment.bicep' = if (deployBackend) {
  name: 'container-env'
  params: {
    name: 'cae-${environmentName}'
    location: location
    tags: tags
  }
}

// Container Registry - only deployed when deployBackend=true
// Note: The existing containerregistry.bicep requires principal IDs for role assignments
// Those will be provided when backend deployment is enabled
// module containerRegistry './modules/containerregistry.bicep' = if (deployBackend) {
//   name: 'container-registry'
//   params: {
//     name: 'cr${replace(environmentName, '-', '')}'
//     location: location
//     tags: tags
//     webhookPrincipalId: ''  // will be filled when deploying backend
//     apiPrincipalId: ''       // will be filled when deploying backend
//     mcpPrincipalId: ''       // will be filled when deploying backend
//   }
// }

// ========================================
// OUTPUTS
// ========================================

output webAppName string = webApp.name
output webAppHostname string = webApp.properties.defaultHostName
output appServicePlanId string = appServicePlan.id
output keyVaultName string = keyVaultName
output keyVaultUri string = keyVault.outputs.keyVaultUri
output sqlServerName string = sqlServerName
output sqlServerFqdn string = sqlServer.outputs.serverFqdn
output sqlDatabaseName string = sqlServer.outputs.databaseName
