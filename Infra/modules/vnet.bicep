@description('Virtual network name.')
param vnetName string

@description('Network security group name.')
param nsgName string

@description('Subnet name.')
param subnetName string

@description('Azure region for network resources.')
param location string

@description('CIDR for the virtual network.')
param vnetAddressPrefix string

@description('CIDR for the VM subnet.')
param subnetAddressPrefix string

@description('Source CIDR allowed to connect to SSH.')
param allowedSshSourceAddressPrefix string

@description('Source CIDR allowed to connect to the books service on port 8080.')
param allowedBooksServiceSourceAddressPrefix string

@description('Source CIDR allowed to connect to HTTPS on port 443.')
param allowedHttpsSourceAddressPrefix string

@description('Optional perf VM source CIDR allowed to connect to HTTPS on port 443.')
param allowedPerfVmHttpsSourceAddressPrefix string

@description('Source CIDR allowed to connect to HTTP on port 80.')
param allowedHttpSourceAddressPrefix string

var perfVmHttpsRules = empty(allowedPerfVmHttpsSourceAddressPrefix) ? [] : [
  {
    name: 'Allow-Https-443-From-Perf'
    properties: {
      priority: 1025
      access: 'Allow'
      direction: 'Inbound'
      protocol: 'Tcp'
      sourcePortRange: '*'
      destinationPortRange: '443'
      sourceAddressPrefix: allowedPerfVmHttpsSourceAddressPrefix
      destinationAddressPrefix: '*'
    }
  }
]

resource nsg 'Microsoft.Network/networkSecurityGroups@2024-05-01' = {
  name: nsgName
  location: location
  properties: {
    securityRules: concat([
      {
        name: 'AllowSshFromConfiguredSource'
        properties: {
          priority: 1000
          access: 'Allow'
          direction: 'Inbound'
          protocol: 'Tcp'
          sourcePortRange: '*'
          destinationPortRange: '22'
          sourceAddressPrefix: allowedSshSourceAddressPrefix
          destinationAddressPrefix: '*'
        }
      }
      {
        name: 'Allow-Books-Service-8080'
        properties: {
          priority: 1010
          access: 'Allow'
          direction: 'Inbound'
          protocol: 'Tcp'
          sourcePortRange: '*'
          destinationPortRange: '8080'
          sourceAddressPrefix: allowedBooksServiceSourceAddressPrefix
          destinationAddressPrefix: '*'
        }
      }
      {
        name: 'Allow-Https-443'
        properties: {
          priority: 1020
          access: 'Allow'
          direction: 'Inbound'
          protocol: 'Tcp'
          sourcePortRange: '*'
          destinationPortRange: '443'
          sourceAddressPrefix: allowedHttpsSourceAddressPrefix
          destinationAddressPrefix: '*'
        }
      }
      {
        name: 'Allow-Http-80'
        properties: {
          priority: 1030
          access: 'Allow'
          direction: 'Inbound'
          protocol: 'Tcp'
          sourcePortRange: '*'
          destinationPortRange: '80'
          sourceAddressPrefix: allowedHttpSourceAddressPrefix
          destinationAddressPrefix: '*'
        }
      }
    ], perfVmHttpsRules)
  }
}

resource vnet 'Microsoft.Network/virtualNetworks@2024-05-01' = {
  name: vnetName
  location: location
  properties: {
    addressSpace: {
      addressPrefixes: [
        vnetAddressPrefix
      ]
    }
    subnets: [
      {
        name: subnetName
        properties: {
          addressPrefix: subnetAddressPrefix
          networkSecurityGroup: {
            id: nsg.id
          }
        }
      }
    ]
  }
}

output vnetId string = vnet.id
output subnetId string = resourceId('Microsoft.Network/virtualNetworks/subnets', vnet.name, subnetName)
output networkSecurityGroupId string = nsg.id
