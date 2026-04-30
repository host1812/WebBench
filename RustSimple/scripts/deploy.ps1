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

Invoke-ServiceDeploy -ScriptRoot $PSScriptRoot -DefaultImageName "books-service-rust-simple" -VmIp $VmIp -VmUser $VmUser -RemoteDir $RemoteDir -HttpsPort $HttpsPort -DefaultRemoteDir "/opt/books-service-rust-simple" -RunComposeMigrateService -AdditionalCleanupRemoteDirs @("/opt/books-service", "/opt/books-service-rust-simple", "/opt/authors-books-service")
