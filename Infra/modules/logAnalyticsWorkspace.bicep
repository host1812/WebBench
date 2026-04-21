@description('Log Analytics workspace name.')
param name string

@description('Azure region for the workspace.')
param location string

@description('Workspace retention in days.')
@minValue(30)
@maxValue(730)
param retentionInDays int

@description('Daily ingestion cap in GB. Use -1 for unlimited.')
param dailyQuotaGb int

resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: name
  location: location
  properties: {
    features: {
      enableDataExport: true
      enableLogAccessUsingOnlyResourcePermissions: true
    }
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
    retentionInDays: retentionInDays
    sku: {
      name: 'PerGB2018'
    }
    workspaceCapping: {
      dailyQuotaGb: dailyQuotaGb
    }
  }
}

output workspaceId string = workspace.id
output name string = workspace.name
output customerId string = workspace.properties.customerId
