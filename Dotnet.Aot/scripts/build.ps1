[CmdletBinding()]
param(
    [string]$Tag = "local",
    [string[]]$AdditionalRemoteTags = @(),
    [string]$LocalImage,
    [string]$DockerfilePath,
    [string]$BuildContext
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "..\..\scripts\ServiceScripts.ps1")

Invoke-ServiceBuild -ScriptRoot $PSScriptRoot -DefaultImageName "books-service-dotnet-aot" -Tag $Tag -AdditionalRemoteTags $AdditionalRemoteTags -LocalImage $LocalImage -DockerfilePath $DockerfilePath -BuildContext $BuildContext
