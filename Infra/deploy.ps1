[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9_-]{1,38}[A-Za-z0-9]$')]
    [string] $ProjectName,
    [string] $Subscription = 'b8d7fc1c-6003-4425-baf0-15d8db0e1714',
    [string] $ResourceGroupName,
    [string] $Location = 'northcentralus',
    [string] $TemplateFile = 'main.bicep',
    [string] $ParameterFile = 'main.bicepparam',
    [string] $PerfTestResourceGroupName,
    [string] $PerfTestLocation = 'westus3',
    [string] $PerfTestTemplateFile = 'perftest-main.bicep',
    [string] $PerfTestParameterFile = 'perftest-main.bicepparam',
    [switch] $SkipWhatIf,
    [switch] $SkipPerfTest,
    [switch] $SkipHostsUpdate
)

$ErrorActionPreference = 'Stop'
$script:DeploymentMessages = [System.Collections.Generic.List[string]]::new()
$script:ProgressActivity = 'Deploy WebBench infrastructure'
$script:ProgressStep = 0
$script:ProgressTotalSteps = 8

function Test-VerboseEnabled {
    return $VerbosePreference -ne 'SilentlyContinue'
}

function Add-DeploymentMessage {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Message
    )

    $script:DeploymentMessages.Add($Message)
}

function Set-DeploymentProgress {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Status
    )

    $script:ProgressStep += 1
    $percentComplete = [Math]::Min(95, [int](($script:ProgressStep / $script:ProgressTotalSteps) * 100))

    Write-Progress `
        -Activity $script:ProgressActivity `
        -Status $Status `
        -PercentComplete $percentComplete

    Write-Verbose $Status
}

function Get-AzArguments {
    param(
        [Parameter(Mandatory = $true)]
        [string[]] $Arguments
    )

    if (Test-VerboseEnabled) {
        return $Arguments
    }

    return @($Arguments + '--only-show-errors')
}

function Invoke-AzCliCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string[]] $Arguments,

        [string] $Description
    )

    $effectiveArguments = Get-AzArguments -Arguments $Arguments

    if ($Description) {
        Write-Verbose $Description
    }

    Write-Verbose "az $($Arguments -join ' ')"

    if (Test-VerboseEnabled) {
        & az @effectiveArguments
    }
    else {
        & az @effectiveArguments | Out-Null
    }

    if ($LASTEXITCODE -ne 0) {
        throw "Azure CLI command failed with exit code $LASTEXITCODE`: az $($Arguments -join ' ')"
    }
}

function Invoke-AzCliJson {
    param(
        [Parameter(Mandatory = $true)]
        [string[]] $Arguments,

        [string] $Description
    )

    $effectiveArguments = Get-AzArguments -Arguments $Arguments

    if ($Description) {
        Write-Verbose $Description
    }

    Write-Verbose "az $($Arguments -join ' ')"

    $output = & az @effectiveArguments

    if ($LASTEXITCODE -ne 0) {
        throw "Azure CLI command failed with exit code $LASTEXITCODE`: az $($Arguments -join ' ')"
    }

    return $output | Out-String | ConvertFrom-Json
}

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
        Add-DeploymentMessage "Hosts file not found: $hostsPath"
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
            Add-DeploymentMessage "Hosts entry updated: $IpAddress $HostName"
        }
        else {
            Add-DeploymentMessage "Hosts entry unchanged: $IpAddress $HostName"
        }
    }
    catch {
        Add-DeploymentMessage "Hosts file not updated. Run PowerShell as administrator or add manually: $IpAddress $HostName"
        Write-Verbose $_
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

function ConvertTo-ResourceNameToken {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Value
    )

    return $Value.Trim().ToLowerInvariant().Replace('_', '-')
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$templatePath = Join-Path $scriptRoot $TemplateFile
$parameterPath = Join-Path $scriptRoot $ParameterFile
$perfTestTemplatePath = Join-Path $scriptRoot $PerfTestTemplateFile
$perfTestParameterPath = Join-Path $scriptRoot $PerfTestParameterFile
$environmentPath = Join-Path $scriptRoot '.env'
$projectNameToken = ConvertTo-ResourceNameToken -Value $ProjectName

if (-not $ResourceGroupName) {
    $ResourceGroupName = "rg-$projectNameToken-api"
}

if (-not $PerfTestResourceGroupName) {
    $PerfTestResourceGroupName = "rg-$projectNameToken-perf"
}

[Environment]::SetEnvironmentVariable('PROJECT_NAME', $projectNameToken, 'Process')
[Environment]::SetEnvironmentVariable('PERFTEST_RESOURCE_GROUP_NAME', $PerfTestResourceGroupName, 'Process')

Set-DeploymentProgress -Status 'Loading environment configuration.'
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

$accountId = & az account show --query id --output tsv --only-show-errors 2>$null
if ($LASTEXITCODE -ne 0) {
    $accountId = $null
    Write-Verbose 'No active Azure CLI account found.'
}

if (-not $accountId) {
    Write-Verbose 'Starting az login.'
    & az login --output none

    if ($LASTEXITCODE -ne 0) {
        throw "Azure CLI login failed with exit code $LASTEXITCODE."
    }
}

if ($Subscription) {
    Set-DeploymentProgress -Status 'Selecting Azure subscription.'
    Invoke-AzCliCommand `
        -Description "Selecting subscription $Subscription." `
        -Arguments @('account', 'set', '--subscription', $Subscription)
}

$perfTestOutputs = $null
$perfTestPublicIpAddress = $null

if (-not $SkipPerfTest) {
    Set-DeploymentProgress -Status 'Deploying PerfTest environment.'
    $perfTestCommonArgs = @(
        '--location', $PerfTestLocation,
        '--template-file', $perfTestTemplatePath,
        '--parameters', $perfTestParameterPath,
        "projectName=$projectNameToken",
        "location=$PerfTestLocation",
        "resourceGroupName=$PerfTestResourceGroupName"
    )

    if (-not $SkipWhatIf) {
        Invoke-AzCliCommand `
            -Description 'Running PerfTest subscription-scope what-if.' `
            -Arguments (@('deployment', 'sub', 'what-if') + $perfTestCommonArgs)
    }

    $perfTestDeployment = Invoke-AzCliJson `
        -Description 'Deploying PerfTest subscription-scope template.' `
        -Arguments (@('deployment', 'sub', 'create') + $perfTestCommonArgs + @('--output', 'json'))

    $perfTestOutputs = $perfTestDeployment.properties.outputs
    $perfTestPublicIpAddress = $perfTestOutputs.publicIpAddress.value
}

Set-DeploymentProgress -Status 'Creating or updating main resource group.'
Invoke-AzCliCommand `
    -Description "Creating or updating resource group $ResourceGroupName in $Location." `
    -Arguments @('group', 'create', '--name', $ResourceGroupName, '--location', $Location, '--output', 'none')

$mainParameterOverrides = @()
if ($perfTestPublicIpAddress) {
    $mainParameterOverrides += "allowedPerfTestHttpsSourceAddressPrefix=$perfTestPublicIpAddress/32"
}

$mainCommonArgs = @(
    '--resource-group', $ResourceGroupName,
    '--template-file', $templatePath,
    '--parameters', $parameterPath,
    "projectName=$projectNameToken"
) + $mainParameterOverrides

if (-not $SkipWhatIf) {
    Set-DeploymentProgress -Status 'Running main deployment preview.'
    Invoke-AzCliCommand `
        -Description 'Running main resource-group what-if.' `
        -Arguments (@('deployment', 'group', 'what-if') + $mainCommonArgs)
}

Set-DeploymentProgress -Status 'Deploying main infrastructure.'
$deployment = Invoke-AzCliJson `
    -Description 'Deploying main resource-group template.' `
    -Arguments (@('deployment', 'group', 'create') + $mainCommonArgs + @('--output', 'json'))

$outputs = $deployment.properties.outputs
$vmName = $outputs.vmName.value
$publicIpAddress = $outputs.publicIpAddress.value
$sshCommand = $outputs.sshCommand.value

Add-DeploymentMessage "VM public IP: $publicIpAddress"
Add-DeploymentMessage "SSH command: $sshCommand"

if ($outputs.containerRegistryLoginServer) {
    Add-DeploymentMessage "ACR login server: $($outputs.containerRegistryLoginServer.value)"
}

if ($outputs.applicationInsightsConnectionString) {
    Add-DeploymentMessage "Application Insights connection string: $($outputs.applicationInsightsConnectionString.value)"
}

if ($outputs.logAnalyticsWorkspaceName) {
    Add-DeploymentMessage "Log Analytics workspace: $($outputs.logAnalyticsWorkspaceName.value)"
}

if ($outputs.monitoringStorageAccountName) {
    Add-DeploymentMessage "Monitoring storage account: $($outputs.monitoringStorageAccountName.value)"
}

if ($outputs.postgresqlFullyQualifiedDomainName) {
    Add-DeploymentMessage "PostgreSQL host: $($outputs.postgresqlFullyQualifiedDomainName.value)"
}

if ($outputs.postgresqlConnectionString -and $outputs.postgresqlConnectionString.value) {
    Add-DeploymentMessage "PostgreSQL connection string: $($outputs.postgresqlConnectionString.value)"
}
elseif ($outputs.postgresqlConnectionString) {
    Add-DeploymentMessage 'PostgreSQL connection string is a secure deployment output and was not returned by Azure CLI.'
}

if (-not $SkipHostsUpdate) {
    Set-DeploymentProgress -Status 'Updating local hosts file.'
    Update-HostsEntry -HostName $vmName -IpAddress $publicIpAddress
}

if ($perfTestOutputs) {
    Add-DeploymentMessage "PerfTest resource group: $($perfTestOutputs.resourceGroupName.value)"
    Add-DeploymentMessage "PerfTest VM public IP: $perfTestPublicIpAddress"
    Add-DeploymentMessage "PerfTest SSH command: $($perfTestOutputs.sshCommand.value)"
    Add-DeploymentMessage "PerfTest HTTPS source allowed on main VM: $perfTestPublicIpAddress/32"
}

Write-Progress -Activity $script:ProgressActivity -Completed

Write-Host 'Deployment summary:'
foreach ($message in $script:DeploymentMessages) {
    Write-Host "- $message"
}
