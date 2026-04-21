@description('Log Analytics workspace name.')
param workspaceName string

@description('Data export rule name.')
param name string

@description('Storage account resource ID receiving exported workspace data.')
param storageAccountId string

@description('Whether the data export rule is enabled.')
param enabled bool

@description('Workspace tables to export to the storage account.')
param tableNames array

resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' existing = {
  name: workspaceName
}

resource dataExport 'Microsoft.OperationalInsights/workspaces/dataExports@2023-09-01' = {
  parent: workspace
  name: name
  properties: {
    destination: {
      resourceId: storageAccountId
    }
    enable: enabled
    tableNames: tableNames
  }
}

output dataExportId string = dataExport.id
output name string = dataExport.name
