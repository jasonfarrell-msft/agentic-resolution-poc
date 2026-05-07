param name string
param location string
param tags object = {}
param administratorLogin string
@secure()
param administratorLoginPassword string
param allowAzureServices bool = true

resource sqlServer 'Microsoft.Sql/servers@2023-05-01-preview' = {
  name: name
  location: location
  tags: tags
  properties: {
    administratorLogin: administratorLogin
    administratorLoginPassword: administratorLoginPassword
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
  }
}

// Allow Azure services to access this server
resource allowAzure 'Microsoft.Sql/servers/firewallRules@2023-05-01-preview' = if (allowAzureServices) {
  name: 'AllowAllWindowsAzureIps'
  parent: sqlServer
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource database 'Microsoft.Sql/servers/databases@2023-05-01-preview' = {
  name: 'agenticresolution'
  parent: sqlServer
  location: location
  tags: tags
  sku: {
    name: 'Basic'
    tier: 'Basic'
  }
  properties: {
    collation: 'SQL_Latin1_General_CP1_CI_AS'
    maxSizeBytes: 2147483648 // 2GB
  }
}

output serverName string = sqlServer.name
output serverFqdn string = sqlServer.properties.fullyQualifiedDomainName
output databaseName string = database.name
output serverId string = sqlServer.id
