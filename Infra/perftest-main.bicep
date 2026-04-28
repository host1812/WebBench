targetScope = 'subscription'

@description('Azure region for the PerfTest load-test environment.')
param location string

@description('Resource group name for the PerfTest load-test environment.')
param resourceGroupName string

@description('Project name used in PerfTest resource names. Use lowercase letters, numbers, or hyphens.')
@minLength(3)
@maxLength(40)
param projectName string

@description('Linux admin username used for SSH.')
param adminUsername string

@description('Public half of the SSH key pair. Keep the private key on your machine.')
@secure()
param sshPublicKey string

@description('Source CIDR allowed to connect to SSH, for example 203.0.113.10/32.')
param allowedSshSourceAddressPrefix string

@description('VM size for the PerfTest load-test VM.')
param vmSize string

@description('CIDR for the PerfTest virtual network.')
param vnetAddressPrefix string

@description('CIDR for the PerfTest VM subnet.')
param subnetAddressPrefix string

@description('OS disk size in GiB.')
@minValue(30)
param osDiskSizeGB int

@description('Ubuntu image publisher.')
param imagePublisher string

@description('Ubuntu image offer.')
param imageOffer string

@description('Ubuntu image SKU.')
param imageSku string

@description('Ubuntu image version.')
param imageVersion string

var sanitizedProjectName = toLower(replace(projectName, '_', '-'))
var cloudInit = replace(loadTextContent('./scripts/cloud-init-loadtest.yaml'), '__ADMIN_USERNAME__', adminUsername)

resource rg 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: resourceGroupName
  location: location
}

module loadTestEnvironment 'modules/loadTestEnvironment.bicep' = {
  name: 'loadTestEnvironment'
  scope: rg
  params: {
    adminUsername: adminUsername
    allowedSshSourceAddressPrefix: allowedSshSourceAddressPrefix
    customData: cloudInit
    imageOffer: imageOffer
    imagePublisher: imagePublisher
    imageSku: imageSku
    imageVersion: imageVersion
    location: location
    networkInterfaceName: 'nic-${sanitizedProjectName}-perf'
    nsgName: 'nsg-${sanitizedProjectName}-perf'
    osDiskSizeGB: osDiskSizeGB
    publicIpName: 'pip-${sanitizedProjectName}-perf'
    sshPublicKey: sshPublicKey
    subnetAddressPrefix: subnetAddressPrefix
    subnetName: 'subnet-${sanitizedProjectName}-perf'
    vmName: 'vm-${sanitizedProjectName}-perf'
    vmSize: vmSize
    vnetAddressPrefix: vnetAddressPrefix
    vnetName: 'vnet-${sanitizedProjectName}-perf'
  }
}

output resourceGroupName string = rg.name
output vmName string = loadTestEnvironment.outputs.vmName
output adminUsername string = adminUsername
output publicIpAddress string = loadTestEnvironment.outputs.publicIpAddress
output sshCommand string = 'ssh -i <path-to-private-key> ${adminUsername}@${loadTestEnvironment.outputs.publicIpAddress}'
