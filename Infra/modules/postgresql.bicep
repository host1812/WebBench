@description('PostgreSQL Flexible Server name.')
param serverName string

@description('Azure region for PostgreSQL.')
param location string

@description('PostgreSQL administrator login.')
param administratorLogin string

@description('PostgreSQL administrator password.')
@secure()
param administratorLoginPassword string

@description('PostgreSQL database name.')
param databaseName string

@description('PostgreSQL version.')
@allowed([
  '13'
  '14'
  '15'
  '16'
])
param version string

@description('PostgreSQL compute SKU name.')
param skuName string

@description('PostgreSQL compute SKU tier.')
@allowed([
  'Burstable'
  'GeneralPurpose'
  'MemoryOptimized'
])
param skuTier string

@description('PostgreSQL storage size in GiB.')
@minValue(32)
param storageSizeGB int

@description('Backup retention in days.')
@minValue(7)
@maxValue(35)
param backupRetentionDays int

@description('Whether public network access is enabled.')
@allowed([
  'Enabled'
  'Disabled'
])
param publicNetworkAccess string

@description('Firewall rules for public PostgreSQL access.')
param firewallRules array

resource server 'Microsoft.DBforPostgreSQL/flexibleServers@2023-06-01-preview' = {
  name: serverName
  location: location
  sku: {
    name: skuName
    tier: skuTier
  }
  properties: {
    administratorLogin: administratorLogin
    administratorLoginPassword: administratorLoginPassword
    version: version
    storage: {
      storageSizeGB: storageSizeGB
    }
    backup: {
      backupRetentionDays: backupRetentionDays
      geoRedundantBackup: 'Disabled'
    }
    highAvailability: {
      mode: 'Disabled'
    }
    network: {
      publicNetworkAccess: publicNetworkAccess
    }
  }
}

resource database 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2023-06-01-preview' = {
  parent: server
  name: databaseName
  properties: {
    charset: 'UTF8'
    collation: 'en_US.utf8'
  }
}

resource serverFirewallRules 'Microsoft.DBforPostgreSQL/flexibleServers/firewallRules@2023-06-01-preview' = [for rule in firewallRules: if (publicNetworkAccess == 'Enabled') {
  parent: server
  name: rule.name
  properties: {
    startIpAddress: rule.startIpAddress
    endIpAddress: rule.endIpAddress
  }
}]

output serverName string = server.name
output databaseName string = database.name
output fullyQualifiedDomainName string = server.properties.fullyQualifiedDomainName

@secure()
output connectionString string = 'Host=${server.properties.fullyQualifiedDomainName};Port=5432;Database=${database.name};Username=${administratorLogin};Password=${administratorLoginPassword};Ssl Mode=Require;Trust Server Certificate=true'
