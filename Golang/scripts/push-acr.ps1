[CmdletBinding()]
param(
    [string]$RegistryName = "acrwebbench4sayzpoemqsyo.azurecr.io",

    [string]$ImageName = "books-service-golang",

    [string]$Tag = "local",

    [string[]]$AdditionalRemoteTags = @("latest"),

    [string]$LocalImage = "books-service-golang:local"
)

$ErrorActionPreference = "Stop"

function Assert-Command {
    param([string]$Name)

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command '$Name' was not found on PATH."
    }
}

Assert-Command "az"
Assert-Command "docker"

docker build -t $LocalImage .

# Get current date in YYYYMMDDHHSS format
$CurrentDateTime = (Get-Date -Format 'yyyyMMddHHmmss')

$remoteTags = @($CurrentDateTime) + $AdditionalRemoteTags |
Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
Select-Object -Unique

az acr login --name $RegistryName

foreach ($remoteTag in $remoteTags) {
    $remoteImage = "$RegistryName/${ImageName}:$remoteTag"
    docker tag $LocalImage $remoteImage
    docker push $remoteImage

    Write-Host "Pushed $remoteImage"
}
