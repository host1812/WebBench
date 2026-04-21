@description('User-assigned managed identity name.')
param name string

@description('Azure region for the managed identity.')
param location string

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: name
  location: location
}

output identityId string = identity.id
output name string = identity.name
output principalId string = identity.properties.principalId
output clientId string = identity.properties.clientId
output tenantId string = identity.properties.tenantId
