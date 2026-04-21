@description('Azure region for all resources.')
param location string

@description('Short prefix used in resource names. Use lowercase letters, numbers, or hyphens.')
@minLength(3)
@maxLength(20)
param resourcePrefix string

@description('Linux admin username used for SSH.')
param adminUsername string

@description('Public half of the SSH key pair. Keep the private key on your machine.')
@secure()
param sshPublicKey string

@description('Source CIDR allowed to connect to SSH, for example 203.0.113.10/32.')
param allowedSshSourceAddressPrefix string

@description('Source CIDR allowed to connect to the books service on port 8080, for example 203.0.113.10/32.')
param allowedBooksServiceSourceAddressPrefix string

@description('Source CIDR allowed to connect to HTTPS on port 443, for example 203.0.113.10/32.')
param allowedHttpsSourceAddressPrefix string

@description('Source CIDR allowed to connect to HTTP on port 80, for example 203.0.113.10/32.')
param allowedHttpSourceAddressPrefix string

@description('VM size. Allowed values are at least 4 vCPU and 8 GiB memory.')
@allowed([
  'Standard_F4s_v2'
  'Standard_D4s_v5'
  'Standard_D4as_v5'
  'Standard_B4ms'
  'Standard_E4s_v5'
])
param vmSize string

@description('Whether to install Docker during first boot using cloud-init.')
param installDocker bool

@description('CIDR for the virtual network.')
param vnetAddressPrefix string

@description('CIDR for the VM subnet.')
param subnetAddressPrefix string

@description('OS disk size in GiB.')
@minValue(30)
param osDiskSizeGB int

@description('Ubuntu image publisher.')
param imagePublisher string

@description('Ubuntu image offer.')
param imageOffer string

@description('Ubuntu image SKU.')
param imageSku string

@description('Ubuntu image version.')
param imageVersion string

@description('Container registry name prefix. ACR names must be globally unique and use only lowercase letters and numbers.')
@minLength(5)
@maxLength(37)
param containerRegistryNamePrefix string

@description('Append a deterministic tenant-scoped suffix to the ACR name prefix.')
param useTenantUniqueContainerRegistryName bool

@description('Container registry SKU.')
@allowed([
  'Basic'
  'Standard'
  'Premium'
])
param containerRegistrySku string

@description('Whether ACR admin user is enabled. Managed identity pull access does not require this.')
param containerRegistryAdminUserEnabled bool

@description('User object ID that receives AcrPush on the deployed container registry.')
param containerRegistryPushUserObjectId string

@description('PostgreSQL Flexible Server name prefix.')
@minLength(3)
@maxLength(50)
param postgresqlServerNamePrefix string

@description('Append a deterministic tenant-scoped suffix to the PostgreSQL server name prefix.')
param useTenantUniquePostgresqlServerName bool

@description('PostgreSQL administrator login.')
param postgresqlAdministratorLogin string

@description('PostgreSQL administrator password.')
@secure()
param postgresqlAdministratorLoginPassword string

@description('PostgreSQL database name.')
param postgresqlDatabaseName string

@description('PostgreSQL version.')
param postgresqlVersion string

@description('PostgreSQL compute SKU name.')
param postgresqlSkuName string

@description('PostgreSQL compute SKU tier.')
param postgresqlSkuTier string

@description('PostgreSQL storage size in GiB.')
param postgresqlStorageSizeGB int

@description('PostgreSQL backup retention in days.')
param postgresqlBackupRetentionDays int

@description('Whether PostgreSQL public network access is enabled.')
param postgresqlPublicNetworkAccess string

@description('Firewall rules for public PostgreSQL access.')
param postgresqlFirewallRules array

@description('Log Analytics workspace retention in days.')
param logAnalyticsRetentionInDays int

@description('Log Analytics daily ingestion cap in GB. Use -1 for unlimited.')
param logAnalyticsDailyQuotaGb int

@description('Storage account name prefix for monitoring export data. Use lowercase letters and numbers.')
@minLength(3)
@maxLength(18)
param monitoringStorageAccountNamePrefix string

@description('Append a deterministic tenant-scoped suffix to the monitoring storage account name prefix.')
param useTenantUniqueMonitoringStorageAccountName bool

@description('Monitoring storage account replication SKU.')
param monitoringStorageAccountSku string

@description('Blob soft-delete retention in days for monitoring storage.')
param monitoringStorageBlobDeleteRetentionDays int

@description('Application Insights retention in days.')
param applicationInsightsRetentionInDays int

@description('Application Insights telemetry sampling percentage.')
param applicationInsightsSamplingPercentage int

@description('Whether Log Analytics data export to storage is enabled.')
param logAnalyticsDataExportEnabled bool

@description('Log Analytics workspace tables exported to the monitoring storage account.')
param logAnalyticsDataExportTableNames array

var sanitizedPrefix = toLower(replace(resourcePrefix, '_', '-'))
var acrPrefix = toLower(replace(replace(containerRegistryNamePrefix, '-', ''), '_', ''))
var containerRegistryName = useTenantUniqueContainerRegistryName ? take('${acrPrefix}${uniqueString(tenant().tenantId)}', 50) : acrPrefix
var postgresqlPrefix = toLower(replace(postgresqlServerNamePrefix, '_', '-'))
var postgresqlServerName = useTenantUniquePostgresqlServerName ? take('${postgresqlPrefix}-${uniqueString(tenant().tenantId)}', 63) : postgresqlPrefix
var monitoringStoragePrefix = toLower(replace(replace(monitoringStorageAccountNamePrefix, '-', ''), '_', ''))
var monitoringStorageAccountName = useTenantUniqueMonitoringStorageAccountName ? take('${monitoringStoragePrefix}${uniqueString(tenant().tenantId)}', 24) : monitoringStoragePrefix
var managedIdentityName = '${sanitizedPrefix}-mi'
var managedIdentityNameSeed = resourceId('Microsoft.ManagedIdentity/userAssignedIdentities', managedIdentityName)
var cloudInit = installDocker ? replace(loadTextContent('./scripts/cloud-init-docker.yaml'), '__ADMIN_USERNAME__', adminUsername) : '#cloud-config\n'

module acr 'modules/containerRegistry.bicep' = {
  name: 'containerRegistry'
  params: {
    adminUserEnabled: containerRegistryAdminUserEnabled
    location: location
    name: containerRegistryName
    skuName: containerRegistrySku
  }
}

module managedIdentity 'modules/managedIdentity.bicep' = {
  name: 'managedIdentity'
  params: {
    location: location
    name: managedIdentityName
  }
}

module vnet 'modules/vnet.bicep' = {
  name: 'vnet'
  params: {
    allowedBooksServiceSourceAddressPrefix: allowedBooksServiceSourceAddressPrefix
    allowedHttpSourceAddressPrefix: allowedHttpSourceAddressPrefix
    allowedHttpsSourceAddressPrefix: allowedHttpsSourceAddressPrefix
    allowedSshSourceAddressPrefix: allowedSshSourceAddressPrefix
    location: location
    nsgName: '${sanitizedPrefix}-nsg'
    subnetAddressPrefix: subnetAddressPrefix
    subnetName: '${sanitizedPrefix}-subnet'
    vnetAddressPrefix: vnetAddressPrefix
    vnetName: '${sanitizedPrefix}-vnet'
  }
}

module publicIp 'modules/publicIp.bicep' = {
  name: 'publicIp'
  params: {
    location: location
    name: '${sanitizedPrefix}-pip'
  }
}

module nic 'modules/networkInterface.bicep' = {
  name: 'networkInterface'
  params: {
    location: location
    name: '${sanitizedPrefix}-nic'
    publicIpId: publicIp.outputs.publicIpId
    subnetId: vnet.outputs.subnetId
  }
}

module vm 'modules/virtualMachine.bicep' = {
  name: 'virtualMachine'
  params: {
    adminUsername: adminUsername
    customData: cloudInit
    imageOffer: imageOffer
    imagePublisher: imagePublisher
    imageSku: imageSku
    imageVersion: imageVersion
    location: location
    managedIdentityId: managedIdentity.outputs.identityId
    name: '${sanitizedPrefix}-vm'
    networkInterfaceId: nic.outputs.networkInterfaceId
    osDiskSizeGB: osDiskSizeGB
    sshPublicKey: sshPublicKey
    vmSize: vmSize
  }
}

module containerRegistryRoleAssignment 'modules/containerRegistryRoleAssignment.bicep' = {
  name: 'containerRegistryRoleAssignment'
  params: {
    containerRegistryName: acr.outputs.name
    roleAssignments: [
      {
        nameSeed: managedIdentityNameSeed
        principalId: managedIdentity.outputs.principalId
        principalType: 'ServicePrincipal'
        roleName: 'AcrPull'
      }
      {
        nameSeed: containerRegistryPushUserObjectId
        principalId: containerRegistryPushUserObjectId
        principalType: 'User'
        roleName: 'AcrPush'
      }
    ]
  }
}

module logAnalyticsWorkspace 'modules/logAnalyticsWorkspace.bicep' = {
  name: 'logAnalyticsWorkspace'
  params: {
    dailyQuotaGb: logAnalyticsDailyQuotaGb
    location: location
    name: '${sanitizedPrefix}-law'
    retentionInDays: logAnalyticsRetentionInDays
  }
}

module monitoringStorage 'modules/storageAccount.bicep' = {
  name: 'monitoringStorage'
  params: {
    blobDeleteRetentionDays: monitoringStorageBlobDeleteRetentionDays
    location: location
    name: monitoringStorageAccountName
    skuName: monitoringStorageAccountSku
  }
}

module applicationInsights 'modules/applicationInsights.bicep' = {
  name: 'applicationInsights'
  params: {
    location: location
    name: '${sanitizedPrefix}-appi'
    retentionInDays: applicationInsightsRetentionInDays
    samplingPercentage: applicationInsightsSamplingPercentage
    workspaceResourceId: logAnalyticsWorkspace.outputs.workspaceId
  }
}

module logAnalyticsDataExport 'modules/logAnalyticsDataExport.bicep' = {
  name: 'logAnalyticsDataExport'
  params: {
    enabled: logAnalyticsDataExportEnabled
    name: '${sanitizedPrefix}-law-storage-export'
    storageAccountId: monitoringStorage.outputs.storageAccountId
    tableNames: logAnalyticsDataExportTableNames
    workspaceName: logAnalyticsWorkspace.outputs.name
  }
}

module postgresql 'modules/postgresql.bicep' = {
  name: 'postgresql'
  params: {
    administratorLogin: postgresqlAdministratorLogin
    administratorLoginPassword: postgresqlAdministratorLoginPassword
    backupRetentionDays: postgresqlBackupRetentionDays
    databaseName: postgresqlDatabaseName
    firewallRules: postgresqlFirewallRules
    location: location
    publicNetworkAccess: postgresqlPublicNetworkAccess
    serverName: postgresqlServerName
    skuName: postgresqlSkuName
    skuTier: postgresqlSkuTier
    storageSizeGB: postgresqlStorageSizeGB
    version: postgresqlVersion
  }
}

output vmName string = vm.outputs.name
output adminUsername string = adminUsername
output publicIpAddress string = publicIp.outputs.publicIpAddress
output managedIdentityName string = managedIdentity.outputs.name
output managedIdentityPrincipalId string = managedIdentity.outputs.principalId
output managedIdentityClientId string = managedIdentity.outputs.clientId
output managedIdentityTenantId string = managedIdentity.outputs.tenantId
output containerRegistryName string = acr.outputs.name
output containerRegistryLoginServer string = acr.outputs.loginServer
output containerRegistryRoleAssignmentIds array = containerRegistryRoleAssignment.outputs.roleAssignmentIds
output logAnalyticsWorkspaceName string = logAnalyticsWorkspace.outputs.name
output logAnalyticsWorkspaceId string = logAnalyticsWorkspace.outputs.workspaceId
output monitoringStorageAccountName string = monitoringStorage.outputs.name
output monitoringStorageBlobEndpoint string = monitoringStorage.outputs.primaryBlobEndpoint
output applicationInsightsName string = applicationInsights.outputs.name
output applicationInsightsConnectionString string = applicationInsights.outputs.connectionString
output applicationInsightsInstrumentationKey string = applicationInsights.outputs.instrumentationKey
output logAnalyticsDataExportRuleName string = logAnalyticsDataExport.outputs.name
output postgresqlServerName string = postgresql.outputs.serverName
output postgresqlDatabaseName string = postgresql.outputs.databaseName
output postgresqlFullyQualifiedDomainName string = postgresql.outputs.fullyQualifiedDomainName

@secure()
output postgresqlConnectionString string = postgresql.outputs.connectionString

output sshCommand string = 'ssh -i <path-to-private-key> ${adminUsername}@${publicIp.outputs.publicIpAddress}'
