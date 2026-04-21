@description('Public IP address resource name.')
param name string

@description('Azure region for the public IP address.')
param location string

resource publicIp 'Microsoft.Network/publicIPAddresses@2024-05-01' = {
  name: name
  location: location
  sku: {
    name: 'Standard'
  }
  properties: {
    publicIPAllocationMethod: 'Static'
    publicIPAddressVersion: 'IPv4'
  }
}

output publicIpId string = publicIp.id
output publicIpAddress string = publicIp.properties.ipAddress
