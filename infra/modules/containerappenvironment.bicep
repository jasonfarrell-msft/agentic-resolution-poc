param name string
param location string
param tags object = {}

resource env 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: name
  location: location
  tags: tags
  properties: {}
}

output environmentId string = env.id
output environmentName string = env.name