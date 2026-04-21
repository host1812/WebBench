@description('Network interface name.')
param name string

@description('Azure region for the network interface.')
param location string

@description('Subnet resource ID.')
param subnetId string

@description('Public IP resource ID.')
param publicIpId string

resource nic 'Microsoft.Network/networkInterfaces@2024-05-01' = {
  name: name
  location: location
  properties: {
    ipConfigurations: [
      {
        name: 'ipconfig1'
        properties: {
          privateIPAllocationMethod: 'Dynamic'
          subnet: {
            id: subnetId
          }
          publicIPAddress: {
            id: publicIpId
          }
        }
      }
    ]
  }
}

output networkInterfaceId string = nic.id
output name string = nic.name
