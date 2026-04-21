param(
    [string] $Subscription,
    [string] $ResourceGroupName = 'rg-WebBench',
    [string] $Location = 'northcentralus',
    [string] $TemplateFile = 'main.bicep',
    [string] $ParameterFile = 'main.bicepparam',
    [string] $PerfTestResourceGroupName = 'rg-PerfTest',
    [string] $PerfTestLocation = 'westus3',
    [string] $PerfTestTemplateFile = 'perftest-main.bicep',
    [string] $PerfTestParameterFile = 'perftest-main.bicepparam',
    [switch] $SkipWhatIf,
    [switch] $SkipPerfTest,
    [switch] $SkipHostsUpdate
)

$ErrorActionPreference = 'Stop'

function Update-HostsEntry {
    param(
        [Parameter(Mandatory = $true)]
        [string] $HostName,

        [Parameter(Mandatory = $true)]
        [string] $IpAddress
    )

    $hostsPath = if ($env:SystemRoot) {
        Join-Path $env:SystemRoot 'System32\drivers\etc\hosts'
    }
    else {
        '/etc/hosts'
    }

    if (-not (Test-Path -LiteralPath $hostsPath)) {
        Write-Warning "Hosts file not found: $hostsPath"
        return
    }

    try {
        $lines = @(Get-Content -LiteralPath $hostsPath)
        $found = $false
        $changed = $false

        $updatedLines = foreach ($line in $lines) {
            $trimmedLine = $line.Trim()

            if (-not $trimmedLine -or $trimmedLine.StartsWith('#')) {
                $line
                continue
            }

            $parts = $trimmedLine -split '\s+'
            $names = @($parts | Select-Object -Skip 1)

            if ($names -contains $HostName) {
                $found = $true

                if ($parts[0] -eq $IpAddress) {
                    $line
                }
                else {
                    $changed = $true
                    "$IpAddress`t$($names -join ' ')"
                }
            }
            else {
                $line
            }
        }

        if (-not $found) {
            $updatedLines += "$IpAddress`t$HostName"
            $changed = $true
        }

        if ($changed) {
            Set-Content -LiteralPath $hostsPath -Value $updatedLines -Encoding ascii
            Write-Host "Hosts entry updated: $IpAddress $HostName"
        }
        else {
            Write-Host "Hosts entry unchanged: $IpAddress $HostName"
        }
    }
    catch {
        Write-Warning "Could not update hosts file. Run PowerShell as administrator or add this entry manually: $IpAddress $HostName"
    }
}

function Import-DotEnv {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Environment file not found: $Path. Create it from .env.example before deploying."
    }

    foreach ($line in Get-Content -LiteralPath $Path) {
        $trimmedLine = $line.Trim()

        if (-not $trimmedLine -or $trimmedLine.StartsWith('#')) {
            continue
        }

        $separatorIndex = $trimmedLine.IndexOf('=')
        if ($separatorIndex -lt 1) {
            continue
        }

        $name = $trimmedLine.Substring(0, $separatorIndex).Trim()
        $value = $trimmedLine.Substring($separatorIndex + 1).Trim()

        if (($value.StartsWith('"') -and $value.EndsWith('"')) -or ($value.StartsWith("'") -and $value.EndsWith("'"))) {
            $value = $value.Substring(1, $value.Length - 2)
        }

        [Environment]::SetEnvironmentVariable($name, $value, 'Process')
    }
}

function Assert-RequiredEnvironmentValue {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Name
    )

    $value = [Environment]::GetEnvironmentVariable($Name, 'Process')

    if ([string]::IsNullOrWhiteSpace($value) -or $value.StartsWith('REPLACE_WITH_')) {
        throw "Required environment value '$Name' is missing. Set it in .env before deploying."
    }
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$templatePath = Join-Path $scriptRoot $TemplateFile
$parameterPath = Join-Path $scriptRoot $ParameterFile
$perfTestTemplatePath = Join-Path $scriptRoot $PerfTestTemplateFile
$perfTestParameterPath = Join-Path $scriptRoot $PerfTestParameterFile
$environmentPath = Join-Path $scriptRoot '.env'

Import-DotEnv -Path $environmentPath
Assert-RequiredEnvironmentValue -Name 'SSH_PUBLIC_KEY'
Assert-RequiredEnvironmentValue -Name 'POSTGRESQL_ADMIN_PASSWORD'

if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    throw 'Azure CLI was not found on PATH. Install Azure CLI before running this script.'
}

if (-not (Test-Path -LiteralPath $templatePath)) {
    throw "Template file not found: $templatePath"
}

if (-not (Test-Path -LiteralPath $parameterPath)) {
    throw "Parameter file not found: $parameterPath"
}

if (-not $SkipPerfTest) {
    if (-not (Test-Path -LiteralPath $perfTestTemplatePath)) {
        throw "PerfTest template file not found: $perfTestTemplatePath"
    }

    if (-not (Test-Path -LiteralPath $perfTestParameterPath)) {
        throw "PerfTest parameter file not found: $perfTestParameterPath"
    }
}

$account = az account show --query id --output tsv 2>$null
if (-not $account) {
    az login | Out-Null
}

if ($Subscription) {
    az account set --subscription $Subscription
}

az group create `
    --name $ResourceGroupName `
    --location $Location `
    --output none

if (-not $SkipWhatIf) {
    az deployment group what-if `
        --resource-group $ResourceGroupName `
        --template-file $templatePath `
        --parameters $parameterPath
}

$deployment = az deployment group create `
    --resource-group $ResourceGroupName `
    --template-file $templatePath `
    --parameters $parameterPath `
    --output json | ConvertFrom-Json

$outputs = $deployment.properties.outputs
$vmName = $outputs.vmName.value
$publicIpAddress = $outputs.publicIpAddress.value
$sshCommand = $outputs.sshCommand.value

Write-Host "VM public IP: $publicIpAddress"
Write-Host "SSH command: $sshCommand"

if ($outputs.containerRegistryLoginServer) {
    Write-Host "ACR login server: $($outputs.containerRegistryLoginServer.value)"
}

if ($outputs.applicationInsightsConnectionString) {
    Write-Host "Application Insights connection string: $($outputs.applicationInsightsConnectionString.value)"
}

if ($outputs.logAnalyticsWorkspaceName) {
    Write-Host "Log Analytics workspace: $($outputs.logAnalyticsWorkspaceName.value)"
}

if ($outputs.monitoringStorageAccountName) {
    Write-Host "Monitoring storage account: $($outputs.monitoringStorageAccountName.value)"
}

if ($outputs.postgresqlFullyQualifiedDomainName) {
    Write-Host "PostgreSQL host: $($outputs.postgresqlFullyQualifiedDomainName.value)"
}

if ($outputs.postgresqlConnectionString -and $outputs.postgresqlConnectionString.value) {
    Write-Host "PostgreSQL connection string: $($outputs.postgresqlConnectionString.value)"
}
elseif ($outputs.postgresqlConnectionString) {
    Write-Warning 'PostgreSQL connection string is a secure deployment output and was not returned by Azure CLI.'
}

if (-not $SkipHostsUpdate) {
    Update-HostsEntry -HostName $vmName -IpAddress $publicIpAddress
}

if (-not $SkipPerfTest) {
    if (-not $SkipWhatIf) {
        az deployment sub what-if `
            --location $PerfTestLocation `
            --template-file $perfTestTemplatePath `
            --parameters $perfTestParameterPath `
            location=$PerfTestLocation `
            resourceGroupName=$PerfTestResourceGroupName
    }

    $perfTestDeployment = az deployment sub create `
        --location $PerfTestLocation `
        --template-file $perfTestTemplatePath `
        --parameters $perfTestParameterPath `
        location=$PerfTestLocation `
        resourceGroupName=$PerfTestResourceGroupName `
        --output json | ConvertFrom-Json

    $perfTestOutputs = $perfTestDeployment.properties.outputs

    Write-Host "PerfTest resource group: $($perfTestOutputs.resourceGroupName.value)"
    Write-Host "PerfTest VM public IP: $($perfTestOutputs.publicIpAddress.value)"
    Write-Host "PerfTest SSH command: $($perfTestOutputs.sshCommand.value)"
}
