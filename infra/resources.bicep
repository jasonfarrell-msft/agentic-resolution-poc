param environmentName string
param location string
param principalId string
param tags object

@description('Deploy backend resources (Container Apps, ACR, etc.) - defaults to false')
param deployBackend bool = false

@description('External URL for the .NET tickets CRUD API used by the Blazor frontend.')
param ticketsApiBaseUrl string = 'https://ca-api-tocqjp4pnegfo.graybush-af9ee262.eastus2.azurecontainerapps.io'

@description('External URL for the Python Resolution API used by the Blazor frontend.')
param resolutionApiBaseUrl string = 'https://ca-resolution-tocqjp4pnegfo.graybush-af9ee262.eastus2.azurecontainerapps.io'

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
      ]
    }
    httpsOnly: true
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
