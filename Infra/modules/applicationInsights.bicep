@description('Application Insights resource name.')
param name string

@description('Azure region for Application Insights.')
param location string

@description('Log Analytics workspace resource ID for workspace-based Application Insights.')
param workspaceResourceId string

@description('Application Insights retention in days.')
param retentionInDays int

@description('Telemetry sampling percentage.')
@minValue(0)
@maxValue(100)
param samplingPercentage int

resource component 'Microsoft.Insights/components@2020-02-02' = {
  name: name
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    DisableIpMasking: false
    DisableLocalAuth: false
    Flow_Type: 'Bluefield'
    IngestionMode: 'LogAnalytics'
    Request_Source: 'rest'
    RetentionInDays: retentionInDays
    SamplingPercentage: samplingPercentage
    WorkspaceResourceId: workspaceResourceId
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
  }
}

output applicationInsightsId string = component.id
output name string = component.name
output applicationId string = component.properties.AppId
output connectionString string = component.properties.ConnectionString
output instrumentationKey string = component.properties.InstrumentationKey
