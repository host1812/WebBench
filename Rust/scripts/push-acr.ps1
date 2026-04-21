[CmdletBinding()]
param(
    [string]$RegistryName = "acrwebbench-dub0bzb9fqcqhehf.azurecr.io",

    [string]$ImageName = "books-service-rust",

    [string]$Tag = "local",

    [string[]]$AdditionalRemoteTags = @("latest"),

    [string]$LocalImage = "books-service-rust:local"
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
