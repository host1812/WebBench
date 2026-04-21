@description('Container registry name where roles should be assigned.')
param containerRegistryName string

@description('Role assignments to create on the container registry.')
param roleAssignments array

var roleDefinitionIds = {
  AcrPull: '7f951dda-4ed3-4680-a7ca-43fe172d538d'
  AcrPush: '8311e382-0749-4cb8-b61a-304f252e45ec'
}

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' existing = {
  name: containerRegistryName
}

resource containerRegistryRoleAssignments 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for assignment in roleAssignments: {
  name: guid(tenant().tenantId, registry.id, assignment.nameSeed, assignment.roleName)
  scope: registry
  properties: {
    principalId: assignment.principalId
    principalType: assignment.principalType
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleDefinitionIds[assignment.roleName])
  }
}]

output roleAssignmentIds array = [for (assignment, i) in roleAssignments: {
  nameSeed: assignment.nameSeed
  principalId: assignment.principalId
  principalType: assignment.principalType
  roleName: assignment.roleName
  roleAssignmentId: containerRegistryRoleAssignments[i].id
}]
