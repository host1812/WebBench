@description('Virtual machine name.')
param name string

@description('Azure region for the virtual machine.')
param location string

@description('Linux admin username used for SSH.')
param adminUsername string

@description('Public half of the SSH key pair.')
@secure()
param sshPublicKey string

@description('VM size.')
param vmSize string

@description('Network interface resource ID.')
param networkInterfaceId string

@description('User-assigned managed identity resource ID.')
param managedIdentityId string

@description('Cloud-init custom data.')
param customData string

@description('OS disk size in GiB.')
param osDiskSizeGB int

@description('Ubuntu image publisher.')
param imagePublisher string

@description('Ubuntu image offer.')
param imageOffer string

@description('Ubuntu image SKU.')
param imageSku string

@description('Ubuntu image version.')
param imageVersion string

resource vm 'Microsoft.Compute/virtualMachines@2024-07-01' = {
  name: name
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${managedIdentityId}': {}
    }
  }
  properties: {
    hardwareProfile: {
      vmSize: vmSize
    }
    osProfile: {
      computerName: name
      adminUsername: adminUsername
      customData: base64(customData)
      linuxConfiguration: {
        disablePasswordAuthentication: true
        ssh: {
          publicKeys: [
            {
              path: '/home/${adminUsername}/.ssh/authorized_keys'
              keyData: sshPublicKey
            }
          ]
        }
      }
    }
    storageProfile: {
      imageReference: {
        publisher: imagePublisher
        offer: imageOffer
        sku: imageSku
        version: imageVersion
      }
      osDisk: {
        createOption: 'FromImage'
        diskSizeGB: osDiskSizeGB
        managedDisk: {
          storageAccountType: 'Premium_LRS'
        }
      }
    }
    networkProfile: {
      networkInterfaces: [
        {
          id: networkInterfaceId
        }
      ]
    }
  }
}

output id string = vm.id
output name string = vm.name
