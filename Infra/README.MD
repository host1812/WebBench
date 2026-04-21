# Azure Linux VM Bicep Project

This project deploys a Linux VM with a public IP address, SSH key authentication, optional Docker installation through cloud-init, an Azure Container Registry, registry role assignments, Azure Database for PostgreSQL Flexible Server, and monitoring resources.

The VM uses a user-assigned managed identity. The deployment grants that identity the built-in `AcrPull` role on the deployed registry, and grants `AcrPush` to the configured user object ID.

## Files

- `main.bicep` - orchestration template that wires the modules together.
- `main.bicepparam` - all deployment inputs.
- `.env.example` - template for local secret values.
- `deploy.ps1` - simple PowerShell deployment wrapper around the Azure CLI commands.
- `modules/containerRegistry.bicep` - Azure Container Registry.
- `modules/managedIdentity.bicep` - user-assigned managed identity.
- `modules/vnet.bicep` - VNet, subnet, NSG, and SSH rule.
- `modules/publicIp.bicep` - static public IP address.
- `modules/networkInterface.bicep` - VM NIC.
- `modules/virtualMachine.bicep` - Linux VM.
- `modules/logAnalyticsWorkspace.bicep` - Log Analytics workspace for monitoring data.
- `modules/storageAccount.bicep` - storage account for exported monitoring data.
- `modules/applicationInsights.bicep` - workspace-based Application Insights.
- `modules/logAnalyticsDataExport.bicep` - Log Analytics data export to storage.
- `modules/postgresql.bicep` - PostgreSQL Flexible Server, database, and firewall rules.
- `modules/containerRegistryRoleAssignment.bicep` - role assignments on the deployed Azure Container Registry.
- `scripts/cloud-init-docker.yaml` - first-boot Docker bootstrap script.

## Prerequisites

- Azure CLI installed.
- Bicep support in Azure CLI.
- An Azure subscription with quota for the selected VM size.
- An SSH key pair on your machine.

## Configure Parameters

Create a local `.env` file from `.env.example`. The `.env` file is gitignored.

```powershell
Copy-Item .env.example .env
```

Set these values in `.env`:

```dotenv
SSH_PUBLIC_KEY=ssh-ed25519 REPLACE_WITH_YOUR_PUBLIC_KEY_COMMENT
POSTGRESQL_ADMIN_PASSWORD=REPLACE_WITH_STRONG_PASSWORD
```

Edit `main.bicepparam` for non-secret deployment settings.

The default deployment location is `northcentralus`. This region currently supports the selected VM size and PostgreSQL Flexible Server SKU for this subscription.

Set `SSH_PUBLIC_KEY` in `.env` to the public key from your `.pub` file. Keep the private key local.

```powershell
Get-Content $env:USERPROFILE\.ssh\id_ed25519.pub
```

Set `allowedSshSourceAddressPrefix`, `allowedBooksServiceSourceAddressPrefix`, `allowedHttpsSourceAddressPrefix`, and `allowedHttpSourceAddressPrefix` to your public IP as a `/32` CIDR.

```powershell
$myIp = Invoke-RestMethod https://api.ipify.org
"$myIp/32"
```

The default VM size is `Standard_F4s_v2`, which is 4 vCPU and 8 GiB memory. The template restricts `vmSize` to allowed sizes that meet or exceed that minimum.

The NSG allows SSH on port `22`, HTTP on port `80`, HTTPS on port `443`, and the books service on port `8080` from the configured source CIDRs.

The registry name is controlled by:

```bicep
param containerRegistryNamePrefix = 'acrwebbench'
param useTenantUniqueContainerRegistryName = true
```

With `useTenantUniqueContainerRegistryName = true`, the deployed ACR name is `acrwebbench` plus a deterministic suffix from `uniqueString(tenant().tenantId)`. Set it to `false` if you want to deploy the exact name `acrwebbench`.

`containerRegistryPushUserObjectId` receives the built-in `AcrPush` role on the deployed registry. The VM managed identity receives `AcrPull`.

PostgreSQL is controlled by the `postgresql*` parameters in `main.bicepparam`. Set `POSTGRESQL_ADMIN_PASSWORD` in `.env` before deploying. The deployment outputs a secure `postgresqlConnectionString` value for service configuration. Secure outputs are intentionally not shown in normal `az deployment group show` output.

The default PostgreSQL firewall rule allows Azure services to connect:

```bicep
param postgresqlFirewallRules = [
  {
    name: 'AllowAzureServices'
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
]
```

Add a `/32` rule for a non-Azure client if your service runs elsewhere.

Application Insights is workspace-based and sends telemetry to the deployed Log Analytics workspace. Log Analytics data export then writes selected Application Insights tables to the monitoring storage account. The exported tables are controlled by `logAnalyticsDataExportTableNames`.

By default, storage export includes common Application Insights tables such as `AppRequests`, `AppTraces`, `AppExceptions`, `AppDependencies`, and `AppMetrics`. Log Analytics data export starts from the time the export rule is created; it does not backfill older data.

## Deploy

Run the deployment script from this directory.

```powershell
.\deploy.ps1 -Subscription "<subscription-id-or-name>"
```

The script creates `rg-WebBench` in `northcentralus`, runs `what-if`, deploys the Bicep template, and prints the VM public IP address.

The script also updates your local hosts file with the VM name and public IP address. It only changes the entry when the hostname is missing or the IP address changed. On Windows, run PowerShell as administrator to allow the hosts file update.

Use `-SkipWhatIf` if you want to skip the preview.

```powershell
.\deploy.ps1 -Subscription "<subscription-id-or-name>" -SkipWhatIf
```

Use `-SkipHostsUpdate` if you do not want to update your local hosts file.

```powershell
.\deploy.ps1 -Subscription "<subscription-id-or-name>" -SkipHostsUpdate
```

The equivalent Azure CLI commands are below.

If you run the raw Azure CLI commands instead of `deploy.ps1`, set the required environment variables in the current PowerShell session first.

```powershell
$env:SSH_PUBLIC_KEY = Get-Content $env:USERPROFILE\.ssh\id_ed25519.pub
$env:POSTGRESQL_ADMIN_PASSWORD = "<strong-password>"
```

Login and select the target subscription.

```powershell
az login
az account set --subscription "<subscription-id-or-name>"
```

Create a resource group.

```powershell
az group create `
  --name rg-WebBench `
  --location northcentralus
```

Preview the deployment.

```powershell
az deployment group what-if `
  --resource-group rg-WebBench `
  --template-file main.bicep `
  --parameters main.bicepparam
```

Deploy.

```powershell
az deployment group create `
  --resource-group rg-WebBench `
  --template-file main.bicep `
  --parameters main.bicepparam
```

Get the public IP address.

```powershell
az deployment group show `
  --resource-group rg-WebBench `
  --name main `
  --query properties.outputs.publicIpAddress.value `
  --output tsv
```

Get the deployed ACR login server.

```powershell
az deployment group show `
  --resource-group rg-WebBench `
  --name main `
  --query properties.outputs.containerRegistryLoginServer.value `
  --output tsv
```

Get the PostgreSQL server host name.

```powershell
az deployment group show `
  --resource-group rg-WebBench `
  --name main `
  --query properties.outputs.postgresqlFullyQualifiedDomainName.value `
  --output tsv
```

Connect with your private SSH key.

```powershell
ssh -i $env:USERPROFILE\.ssh\id_ed25519 azureuser@<public-ip-address>
```

After the hosts entry is added, you can also connect by VM name.

```powershell
ssh -i $env:USERPROFILE\.ssh\id_ed25519 azureuser@linuxvm-vm
```

## Verify Docker

Docker is installed when `installDocker = true`.

```bash
docker --version
docker compose version
```

If your first SSH session cannot run Docker without `sudo`, sign out and connect again so the new group membership is applied.

## Clean Up

Delete the resource group when you no longer need the VM.

```powershell
az group delete --name rg-WebBench --yes
```
