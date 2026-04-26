[CmdletBinding()]
param(
    [string]$AcrName,
    [string]$ImageRegistry,
    [string]$ImageName,
    [string]$ImageTag,
    [string[]]$AdditionalRemoteTags = @(),
    [string]$LocalImage = "books-service-rust:local"
)

$ErrorActionPreference = "Stop"

function Assert-Command {
    param([string]$Name)

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command '$Name' was not found on PATH."
    }
}

function Import-DotEnv {
    param([string]$Path)

    $values = @{}

    foreach ($line in Get-Content $Path) {
        $trimmed = $line.Trim()
        if ([string]::IsNullOrWhiteSpace($trimmed) -or $trimmed.StartsWith("#")) {
            continue
        }

        $parts = $trimmed.Split("=", 2)
        if ($parts.Count -ne 2) {
            continue
        }

        $key = $parts[0].Trim()
        $value = $parts[1].Trim()
        $values[$key] = $value
        Set-Item -Path "Env:$key" -Value $value
    }

    return $values
}

Assert-Command "az"
Assert-Command "docker"

$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$EnvFile = Join-Path $RepoRoot ".env"

if (-not (Test-Path $EnvFile)) {
    throw "Required path '$EnvFile' was not found."
}

$Settings = Import-DotEnv $EnvFile

$AcrName = if ($AcrName) { $AcrName } else { $Settings["ACR_NAME"] }
$ImageRegistry = if ($ImageRegistry) { $ImageRegistry } else { $Settings["IMAGE_REGISTRY"] }
$ImageName = if ($ImageName) { $ImageName } else { $Settings["IMAGE_NAME"] }
$ImageTag = if ($ImageTag) { $ImageTag } else { $Settings["IMAGE_TAG"] }

foreach ($setting in @(
    @{ Name = "ACR_NAME"; Value = $AcrName },
    @{ Name = "IMAGE_REGISTRY"; Value = $ImageRegistry },
    @{ Name = "IMAGE_NAME"; Value = $ImageName },
    @{ Name = "IMAGE_TAG"; Value = $ImageTag }
)) {
    if ([string]::IsNullOrWhiteSpace($setting.Value)) {
        throw "Missing required setting '$($setting.Name)' in .env."
    }
}

docker build -t $LocalImage .

$CurrentDateTime = Get-Date -Format "yyyyMMddHHmmss"
$RemoteTags = @($ImageTag, $CurrentDateTime) + $AdditionalRemoteTags |
Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
Select-Object -Unique

az acr login --name $AcrName

foreach ($remoteTag in $RemoteTags) {
    $remoteImage = "$ImageRegistry/${ImageName}:$remoteTag"
    docker tag $LocalImage $remoteImage
    docker push $remoteImage
    Write-Host "Pushed $remoteImage"
}
