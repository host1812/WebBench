using './perftest-main.bicep'

param location = 'westus3'
param projectName = readEnvironmentVariable('PROJECT_NAME')
param resourceGroupName = readEnvironmentVariable('PERFTEST_RESOURCE_GROUP_NAME')
param adminUsername = 'azureuser'
param sshPublicKey = readEnvironmentVariable('SSH_PUBLIC_KEY')

// Replace this with your public client IP in CIDR form, for example 203.0.113.10/32.
param allowedSshSourceAddressPrefix = '184.16.75.186/32'

// Standard_F4s_v2 is 4 vCPU and 8 GiB memory.
param vmSize = 'Standard_F4s_v2'
param vnetAddressPrefix = '10.20.0.0/16'
param subnetAddressPrefix = '10.20.1.0/24'
param osDiskSizeGB = 64

param imagePublisher = 'Canonical'
param imageOffer = '0001-com-ubuntu-server-jammy'
param imageSku = '22_04-lts-gen2'
param imageVersion = 'latest'
