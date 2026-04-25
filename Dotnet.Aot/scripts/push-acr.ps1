[CmdletBinding()]
param(
    [string]$RegistryName,

    [string]$ImageName,

    [string]$Tag = "local",

    [string[]]$AdditionalRemoteTags = @("latest"),

    [string]$LocalImage,

    [string]$DockerfilePath,

    [string]$BuildContext
)

$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "Common.ps1")

function Assert-LastExitCode {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Action
    )

    if ($LASTEXITCODE -ne 0) {
        throw "$Action failed with exit code $LASTEXITCODE."
    }
}

$repoRoot = Get-RepoRoot
$envFile = Join-Path $repoRoot ".env"

Import-DotEnv -Path $envFile

Assert-Command "az"
Assert-Command "docker"

$acrName = Get-RequiredSetting "ACR_NAME"
$registryNameResolved = if ([string]::IsNullOrWhiteSpace($RegistryName)) {
    Get-OptionalSetting "ACR_LOGIN_SERVER" "$acrName.azurecr.io"
}
else {
    $RegistryName
}
$acrLoginName = if ($registryNameResolved.EndsWith(".azurecr.io", [System.StringComparison]::OrdinalIgnoreCase)) {
    $registryNameResolved.Substring(0, $registryNameResolved.Length - ".azurecr.io".Length)
}
else {
    $registryNameResolved
}
$imageNameResolved = if ([string]::IsNullOrWhiteSpace($ImageName)) {
    Get-OptionalSetting "IMAGE_NAME" "books-service-dotnet-aot"
}
else {
    $ImageName
}
$dockerfilePathResolved = if ([string]::IsNullOrWhiteSpace($DockerfilePath)) {
    Get-OptionalSetting "DOCKERFILE_PATH" "Dockerfile"
}
else {
    $DockerfilePath
}
$buildContextResolved = if ([string]::IsNullOrWhiteSpace($BuildContext)) {
    Get-OptionalSetting "BUILD_CONTEXT" "."
}
else {
    $BuildContext
}
$localImageResolved = if ([string]::IsNullOrWhiteSpace($LocalImage)) {
    "${imageNameResolved}:$Tag"
}
else {
    $LocalImage
}

Push-Location $repoRoot

try {
    docker build -f $dockerfilePathResolved -t $localImageResolved $buildContextResolved
    Assert-LastExitCode "docker build"

    az acr login --name $acrLoginName
    Assert-LastExitCode "az acr login"

    $currentDateTime = Get-Date -Format "yyyyMMddHHmmss"
    $remoteTags = @($currentDateTime) + $AdditionalRemoteTags |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Select-Object -Unique

    foreach ($remoteTag in $remoteTags) {
        $remoteImage = "$registryNameResolved/${imageNameResolved}:$remoteTag"

        docker tag $localImageResolved $remoteImage
        Assert-LastExitCode "docker tag $remoteImage"

        docker push $remoteImage
        Assert-LastExitCode "docker push $remoteImage"

        Write-Host "Pushed $remoteImage"
    }
}
finally {
    Pop-Location
}
