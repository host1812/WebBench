# Azure Linux VM Bicep Project

This project deploys a Linux VM with a public IP address, SSH key authentication, optional Docker installation through cloud-init, an Azure Container Registry, registry role assignments, Azure Database for PostgreSQL Flexible Server, and monitoring resources.

The VM uses a user-assigned managed identity. The deployment grants that identity the built-in `AcrPull` role on the deployed registry, and grants `AcrPush` to the configured user object ID.

## Files

- `main.bicep` - orchestration template that wires the modules together.
- `main.bicepparam` - all deployment inputs.
- `.env.example` - template for local secret values.
- `deploy.ps1` - simple PowerShell deployment wrapper that deploys the API resources and optional perf VM.
- `modules/containerRegistry.bicep` - Azure Container Registry.
- `modules/managedIdentity.bicep` - user-assigned managed identity.
- `modules/vnet.bicep` - VNet, subnet, NSG, and SSH rule.
- `modules/publicIp.bicep` - static public IP address.
- `modules/networkInterface.bicep` - VM NIC.
- `modules/virtualMachine.bicep` - Linux VM.
- `modules/loadTestEnvironment.bicep` - dedicated LoadTest VNet, NSG, public IP, NIC, and VM.
- `modules/logAnalyticsWorkspace.bicep` - Log Analytics workspace for monitoring data.
- `modules/storageAccount.bicep` - storage account for exported monitoring data.
- `modules/applicationInsights.bicep` - workspace-based Application Insights.
- `modules/logAnalyticsDataExport.bicep` - Log Analytics data export to storage.
- `modules/postgresql.bicep` - PostgreSQL Flexible Server, database, and firewall rules.
- `modules/containerRegistryRoleAssignment.bicep` - role assignments on the deployed Azure Container Registry.
- `scripts/cloud-init-docker.yaml` - first-boot Docker bootstrap script.
- `scripts/cloud-init-loadtest.yaml` - first-boot LoadTest VM bootstrap script.

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

`ProjectName` is required when running `deploy.ps1`. The script normalizes underscores to hyphens and uses the project name to derive resource names.

Main API resource names follow role-specific purposes:

```text
rg-<project>
vm-<project>-api
vnet-<project>-network
subnet-<project>-api
nsg-<project>-api
pip-<project>-public
nic-<project>-api
mi-<project>-api
pg-<project>-db-<unique-suffix>
law-<project>-monitoring
appi-<project>-monitoring
de-<project>-monitoring
```

Perf VM resource names use the `perf` purpose:

```text
vm-<project>-perf
vnet-<project>-perf
subnet-<project>-perf
nsg-<project>-perf
pip-<project>-perf
nic-<project>-perf
```

Globally constrained resources use compact names with the same project and a deterministic tenant suffix:

```text
acr<project>registry<unique-suffix>
st<project>monitoring<unique-suffix>
```

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

The default API and perf VM size is `Standard_F4s_v2`, which is 4 vCPU and 8 GiB memory. The template restricts both VM size parameters to allowed sizes that meet or exceed that minimum.

The NSG allows SSH on port `22`, HTTP on port `80`, HTTPS on port `443`, and the books service on port `8080` from the configured source CIDRs.

When the perf VM is deployed, `deploy.ps1` also allows the perf VM public IP to reach HTTPS port `443` on the API VM. This lets the load-test VM call the API without opening HTTPS broadly.

`containerRegistryPushUserObjectId` receives the built-in `AcrPush` role on the deployed registry. The VM managed identity receives `AcrPull`.

PostgreSQL is controlled by the `postgresql*` parameters in `main.bicepparam`. Set `POSTGRESQL_ADMIN_PASSWORD` in `.env` before deploying. The deployment outputs a secure `postgresqlConnectionString` value for service configuration. Secure outputs are intentionally not shown in normal `az deployment group show` output.

The PostgreSQL server name is derived as `pg-<project>-db-<unique-suffix>`, and the database child resource is derived as `db-<project>-api`.

The default PostgreSQL compute is `Standard_D32s_v3` in the `GeneralPurpose` tier. This gives the database 32 vCores; memory is not the sizing driver for this benchmark.

The default PostgreSQL firewall rule allows Azure services to connect. Rule names are normalized during deployment to `fwr-<project>-<rule-name>`.

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

Run the deployment script from this directory. It deploys all resources into one resource group named `rg-<project>`. By default that includes both the API VM and the perf VM.

```powershell
.\deploy.ps1 -ProjectName "webbench"
```

The script defaults to subscription `b8d7fc1c-6003-4425-baf0-15d8db0e1714`. Pass `-Subscription` only when intentionally deploying somewhere else.

The script creates `rg-<project>` in `northcentralus`, deploys the API resources, optionally deploys the perf VM resources in `westus3` inside the same resource group, and allows the perf VM IP to reach HTTPS port `443` on the API VM. It prints both VM public IP addresses.

By default, Azure CLI deployment output is quiet, the script shows deployment progress, and it prints a final summary at the end. Use `-Verbose` to stream what-if output and deployment command details.

```powershell
.\deploy.ps1 -ProjectName "webbench" -Verbose
```

The script also updates your local hosts file with the VM name and public IP address. It only changes the entry when the hostname is missing or the IP address changed. On Windows, run PowerShell as administrator to allow the hosts file update.

Use `-SkipWhatIf` if you want to skip the preview.

```powershell
.\deploy.ps1 -ProjectName "webbench" -SkipWhatIf
```

Use `-SkipPerfVm` if you only want to deploy the API resources without the perf VM.

```powershell
.\deploy.ps1 -ProjectName "webbench" -SkipPerfVm
```

Use `-SkipHostsUpdate` if you do not want to update your local hosts file.

```powershell
.\deploy.ps1 -ProjectName "webbench" -SkipHostsUpdate
```

The equivalent Azure CLI commands for the main resource-group deployment are below.

If you run the raw Azure CLI commands instead of `deploy.ps1`, set the required environment variables in the current PowerShell session first.

```powershell
$env:SSH_PUBLIC_KEY = Get-Content $env:USERPROFILE\.ssh\id_ed25519.pub
$env:POSTGRESQL_ADMIN_PASSWORD = "<strong-password>"
$projectName = "webbench"
$env:PROJECT_NAME = $projectName
$resourceGroupName = "rg-$projectName"
```

Login and select the target subscription.

```powershell
az login
az account set --subscription "b8d7fc1c-6003-4425-baf0-15d8db0e1714"
```

Create a resource group.

```powershell
az group create `
  --name $resourceGroupName `
  --location northcentralus
```

Preview the deployment.

```powershell
az deployment group what-if `
  --resource-group $resourceGroupName `
  --template-file main.bicep `
  --parameters main.bicepparam projectName=$projectName
```

Deploy.

```powershell
az deployment group create `
  --resource-group $resourceGroupName `
  --template-file main.bicep `
  --parameters main.bicepparam projectName=$projectName
```

Get the public IP address.

```powershell
az deployment group show `
  --resource-group $resourceGroupName `
  --name main `
  --query properties.outputs.publicIpAddress.value `
  --output tsv
```

Get the deployed ACR login server.

```powershell
az deployment group show `
  --resource-group $resourceGroupName `
  --name main `
  --query properties.outputs.containerRegistryLoginServer.value `
  --output tsv
```

Get the PostgreSQL server host name.

```powershell
az deployment group show `
  --resource-group $resourceGroupName `
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
ssh -i $env:USERPROFILE\.ssh\id_ed25519 azureuser@vm-webbench-api
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
az group delete --name rg-webbench --yes
```

## Perf Load-Test VM

The perf VM is deployed into the same `rg-<project>` resource group as the API resources. Its resources use the `perf` purpose suffix, for example `vm-<project>-perf` and `pip-<project>-perf`. It is deployed by `deploy.ps1` unless you pass `-SkipPerfVm`.

Update `perfVmAllowedSshSourceAddressPrefix` in `main.bicepparam` to your public client IP as a `/32` CIDR.

```powershell
$myIp = Invoke-RestMethod https://api.ipify.org
"$myIp/32"
```

To override the perf VM location during deployment:

```powershell
.\deploy.ps1 -ProjectName "webbench" -PerfVmLocation "westus3"
```

The perf VM bootstrap installs Docker, enables and starts Docker, adds the admin user to the `docker` group, pulls `grafana/k6:latest`, installs `curl`, `jq`, `unzip`, and `git`, creates `/opt/k6-tests`, and prints Docker and k6 versions to `/var/log/loadtest-bootstrap.log`.

Connect to the VM after deployment.

```powershell
ssh -i $env:USERPROFILE\.ssh\id_ed25519 azureuser@<perf-vm-public-ip>
```

Run the load test from Docker.

```bash
docker run --rm -i grafana/k6:latest run - < /opt/k6-tests/test.js
```

Clean up the whole environment.

```powershell
az group delete --name rg-webbench --yes
```
