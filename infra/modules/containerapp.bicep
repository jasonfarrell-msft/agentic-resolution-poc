param name string
param location string
param tags object = {}
param environmentId string
param managedIdentityId string
param managedIdentityClientId string
param managedIdentityPrincipalId string
param keyVaultUri string
param keyVaultId string
param appInsightsConnectionString string
param openAiEndpoint string
param acrLoginServer string

// Key Vault Secrets User — same role used in keyvault.bicep and functionapp.bicep
var kvSecretsUserRoleId = '4633458b-17de-408a-b874-0445c86b69e6'

// Extract vault name from URI: https://{name}.vault.azure.net/
var keyVaultName = split(split(keyVaultUri, 'https://')[1], '.')[0]

resource kvResource 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

resource kvRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVaultId, managedIdentityPrincipalId, 'ca-kv-secrets-user')
  scope: kvResource
  properties: {
    principalId: managedIdentityPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', kvSecretsUserRoleId)
  }
}

resource app 'Microsoft.App/containerApps@2024-03-01' = {
  name: name
  location: location
  tags: union(tags, { 'azd-service-name': 'webhook' })
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${managedIdentityId}': {}
    }
  }
  properties: {
    environmentId: environmentId
    configuration: {
      registries: [
        {
          server: acrLoginServer
          identity: managedIdentityId
        }
      ]
      ingress: {
        external: true
        targetPort: 8080
        transport: 'http'
      }
      // No secrets declared at deploy time — webhook-hmac-secret does not exist in KV yet.
      // Container Apps fetches KV secrets eagerly at deployment, so referencing a missing
      // secret blocks the deployment. Operators populate the secret post-deploy and then
      // update the Container App revision to add the secretRef. See containerapp.bicep notes.
    }
    template: {
      containers: [
        {
          name: 'webhook'
          // Placeholder image — replaced when application code is built and pushed to a registry
          image: 'mcr.microsoft.com/dotnet/samples:aspnetapp'
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          env: [
            { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsightsConnectionString }
            { name: 'AZURE_CLIENT_ID', value: managedIdentityClientId }
            { name: 'Foundry__Endpoint', value: '' }
            { name: 'OpenAI__Endpoint', value: openAiEndpoint }
            // Webhook__HmacSecret is empty until operator populates KV secret 'webhook-hmac-secret'
            // and updates the Container App revision to use secretRef instead.
            { name: 'Webhook__HmacSecret', value: '' }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 3
      }
    }
  }
  dependsOn: [kvRole]
}

output containerAppName string = app.name
output defaultHostName string = app.properties.configuration.ingress.fqdn
