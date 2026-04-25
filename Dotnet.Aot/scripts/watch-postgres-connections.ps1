[CmdletBinding()]
param(
    [string]$ServerName,

    [int]$LookbackMinutes = 30
)

$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "Common.ps1")

function Get-ConnectionStringValue {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ConnectionString,

        [Parameter(Mandatory = $true)]
        [string]$Key
    )

    foreach ($segment in $ConnectionString -split ';') {
        $trimmed = $segment.Trim()
        if (-not $trimmed) {
            continue
        }

        $separatorIndex = $trimmed.IndexOf('=')
        if ($separatorIndex -lt 1) {
            continue
        }

        $name = $trimmed.Substring(0, $separatorIndex).Trim()
        if (-not $name.Equals($Key, [System.StringComparison]::OrdinalIgnoreCase)) {
            continue
        }

        return $trimmed.Substring($separatorIndex + 1).Trim()
    }

    return $null
}

Assert-Command "az"

$repoRoot = Get-RepoRoot
$envFile = Join-Path $repoRoot ".env"
Assert-Path $envFile
Import-DotEnv -Path $envFile

if (-not $PSBoundParameters.ContainsKey("ServerName") -or [string]::IsNullOrWhiteSpace($ServerName)) {
    $connectionString = Get-RequiredSetting "ConnectionStrings__Postgres"
    $host = Get-ConnectionStringValue -ConnectionString $connectionString -Key "Host"

    if ([string]::IsNullOrWhiteSpace($host)) {
        throw "Could not determine PostgreSQL host from ConnectionStrings__Postgres."
    }

    $ServerName = $host.Split('.', 2)[0]
}

if ($LookbackMinutes -lt 1) {
    throw "LookbackMinutes must be 1 or greater."
}

$resourceId = az resource list `
    --name $ServerName `
    --resource-type Microsoft.DBforPostgreSQL/flexibleServers `
    --query "[0].id" `
    --output tsv

if ([string]::IsNullOrWhiteSpace($resourceId)) {
    throw "Could not resolve a flexible server resource ID for server '$ServerName'."
}

$endTime = (Get-Date).ToUniversalTime()
$startTime = $endTime.AddMinutes(-$LookbackMinutes)

Write-Host "Monitoring PostgreSQL metrics for server '$ServerName' from $($startTime.ToString('u')) to $($endTime.ToString('u'))."

az monitor metrics list `
    --resource $resourceId `
    --metric active_connections max_connections connections_failed `
    --interval PT1M `
    --aggregation Average Maximum Total `
    --start-time $startTime.ToString("o") `
    --end-time $endTime.ToString("o") `
    --output table
