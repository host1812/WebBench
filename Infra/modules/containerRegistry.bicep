@description('Container registry name.')
param name string

@description('Azure region for the container registry.')
param location string

@description('Container registry SKU.')
@allowed([
  'Basic'
  'Standard'
  'Premium'
])
param skuName string

@description('Whether ACR admin user is enabled.')
param adminUserEnabled bool

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: name
  location: location
  sku: {
    name: skuName
  }
  properties: {
    adminUserEnabled: adminUserEnabled
  }
}

output id string = registry.id
output name string = registry.name
output loginServer string = registry.properties.loginServer
