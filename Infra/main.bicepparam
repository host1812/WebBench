using './main.bicep'

param location = 'northcentralus'
param resourcePrefix = 'linuxvm'
param adminUsername = 'azureuser'

param sshPublicKey = readEnvironmentVariable('SSH_PUBLIC_KEY')

// Replace this with your public client IP in CIDR form, for example 203.0.113.10/32.
param allowedSshSourceAddressPrefix = '184.16.75.186/32'
param allowedBooksServiceSourceAddressPrefix = '184.16.75.186/32'
param allowedHttpsSourceAddressPrefix = '184.16.75.186/32'
param allowedPerfTestHttpsSourceAddressPrefix = ''
param allowedHttpSourceAddressPrefix = '184.16.75.186/32'

// Standard_F4s_v2 is 4 vCPU and 8 GiB memory.
param vmSize = 'Standard_F4s_v2'
param installDocker = true

param vnetAddressPrefix = '10.10.0.0/16'
param subnetAddressPrefix = '10.10.1.0/24'
param osDiskSizeGB = 64

param imagePublisher = 'Canonical'
param imageOffer = '0001-com-ubuntu-server-jammy'
param imageSku = '22_04-lts-gen2'
param imageVersion = 'latest'

param containerRegistryNamePrefix = 'acrwebbench'
param useTenantUniqueContainerRegistryName = true
param containerRegistrySku = 'Basic'
param containerRegistryAdminUserEnabled = false
param containerRegistryPushUserObjectId = '1a8f2341-8fb4-4eb2-a259-b0c12f9955a9'

param postgresqlServerNamePrefix = 'pg-webbench'
param useTenantUniquePostgresqlServerName = true
param postgresqlAdministratorLogin = 'pgadminuser'
param postgresqlAdministratorLoginPassword = readEnvironmentVariable('POSTGRESQL_ADMIN_PASSWORD')
param postgresqlDatabaseName = 'webbench'
param postgresqlVersion = '16'
param postgresqlSkuName = 'Standard_B1ms'
param postgresqlSkuTier = 'Burstable'
param postgresqlStorageSizeGB = 32
param postgresqlBackupRetentionDays = 7
param postgresqlPublicNetworkAccess = 'Enabled'
param postgresqlFirewallRules = [
  {
    name: 'AllowAzureServices'
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
]

param logAnalyticsRetentionInDays = 30
param logAnalyticsDailyQuotaGb = -1

param monitoringStorageAccountNamePrefix = 'stwebbenchlogs'
param useTenantUniqueMonitoringStorageAccountName = true
param monitoringStorageAccountSku = 'Standard_LRS'
param monitoringStorageBlobDeleteRetentionDays = 30

param applicationInsightsRetentionInDays = 90
param applicationInsightsSamplingPercentage = 100

param logAnalyticsDataExportEnabled = true
param logAnalyticsDataExportTableNames = [
  'AppAvailabilityResults'
  'AppBrowserTimings'
  'AppDependencies'
  'AppEvents'
  'AppExceptions'
  'AppMetrics'
  'AppPageViews'
  'AppPerformanceCounters'
  'AppRequests'
  'AppSystemEvents'
  'AppTraces'
]
