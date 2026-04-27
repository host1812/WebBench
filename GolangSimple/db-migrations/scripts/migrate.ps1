[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateSet("up", "down", "status", "version", "force")]
    [string]$Command,

    [string]$EnvFile,

    [string]$DatabaseUrl,

    [int]$Steps = 1,

    [int]$Version = 0,

    [switch]$Dirty
)

$ErrorActionPreference = "Stop"

function Assert-Command {
    param([string]$Name)

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command '$Name' was not found on PATH."
    }
}

Assert-Command "go"

$ProjectRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
if ([string]::IsNullOrWhiteSpace($EnvFile)) {
    $EnvFile = Join-Path $ProjectRoot "..\.env"
}

$arguments = @(
    "run",
    ".\cmd\migrator",
    "-migrations",
    "migrations",
    "-env",
    $EnvFile,
    "-steps",
    $Steps.ToString()
)

if (-not [string]::IsNullOrWhiteSpace($DatabaseUrl)) {
    $arguments += @("-database-url", $DatabaseUrl)
}

if ($Command -eq "force") {
    $arguments += @("-version", $Version.ToString())
    if ($Dirty) {
        $arguments += "-dirty"
    }
}

$arguments += $Command

Push-Location $ProjectRoot
try {
    & go @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Migrator failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}
