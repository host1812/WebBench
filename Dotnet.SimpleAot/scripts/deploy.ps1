[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$VmIp,
    [string]$VmUser,
    [string]$RemoteDir,
    [string]$HttpsPort
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "..\..\scripts\ServiceScripts.ps1")

Invoke-ServiceDeploy -ScriptRoot $PSScriptRoot -DefaultImageName "books-service-dotnet-simple-aot" -VmIp $VmIp -VmUser $VmUser -RemoteDir $RemoteDir -HttpsPort $HttpsPort -DefaultRemoteDir "/opt/books-service-dotnet-simple-aot" -UseGateway -RunImageMigrations -AdditionalCleanupRemoteDirs @("/opt/books-service", "/opt/books-service-dotnet-simple-aot", "/opt/authors-books-service")
