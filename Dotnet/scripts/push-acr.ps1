[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "Common.ps1")

$repoRoot = Get-RepoRoot
$envFile = Join-Path $repoRoot ".env"

Import-DotEnv -Path $envFile

Assert-Command "az"
Assert-Command "docker"

$acrName = Get-RequiredSetting "ACR_NAME"
$acrLoginServer = Get-OptionalSetting "ACR_LOGIN_SERVER" "$acrName.azurecr.io"
$imageName = Get-OptionalSetting "IMAGE_NAME" "authors-books-api"
$imageTag = Get-OptionalSetting "IMAGE_TAG" "latest"
$dockerfilePath = Get-OptionalSetting "DOCKERFILE_PATH" "Dockerfile"
$buildContext = Get-OptionalSetting "BUILD_CONTEXT" "."
$localImage = "authors-books-api:local"
$remoteImage = "${acrLoginServer}/${imageName}:${imageTag}"

Push-Location $repoRoot

try {
    docker build -f $dockerfilePath -t $localImage $buildContext

    az acr login --name $acrName

    docker tag $localImage $remoteImage
    docker push $remoteImage

    Write-Host "Pushed $remoteImage"
}
finally {
    Pop-Location
}
