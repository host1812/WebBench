[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$VmIp,

    [Parameter(Mandatory = $true)]
    [string]$BaseUrl,

    [string]$VmUser = 'azureuser',

    [string]$RemoteDir = '/opt/k6-tests',

    [switch]$SkipTlsVerify,

    [string]$AuthorId,

    [string]$BookId,

    [int]$Vus = 25,

    [string]$Duration = '5m'
)

$ErrorActionPreference = 'Stop'

function Assert-CommandExists {
    param([Parameter(Mandatory = $true)][string]$Name)

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command '$Name' was not found on PATH."
    }
}

function ConvertTo-RemoteShellArg {
    param([AllowNull()][string]$Value)

    if ([string]::IsNullOrEmpty($Value)) {
        return "''"
    }

    return "'" + $Value.Replace("'", "'""'""'") + "'"
}

function Invoke-CheckedNative {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$Arguments
    )

    Write-Host "Running: $FilePath $($Arguments -join ' ')"
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FilePath failed with exit code $LASTEXITCODE."
    }
}

function Invoke-RemoteChecked {
    param([Parameter(Mandatory = $true)][string]$Command)

    Write-Host "Remote command: $Command"
    & ssh $RemoteLogin $Command
    if ($LASTEXITCODE -ne 0) {
        throw "Remote command failed with exit code $LASTEXITCODE."
    }
}

Assert-CommandExists -Name 'ssh'
Assert-CommandExists -Name 'scp'

$RepoRoot = Split-Path -Parent $PSScriptRoot
$LocalK6Script = Join-Path $RepoRoot 'perf/books-read.js'
$LocalReportsDir = Join-Path $RepoRoot 'perf/reports'

if (-not (Test-Path -LiteralPath $LocalK6Script -PathType Leaf)) {
    throw "Required k6 script was not found: $LocalK6Script"
}

if ($Vus -lt 1) {
    throw 'Vus must be 1 or greater.'
}

if ([string]::IsNullOrWhiteSpace($Duration)) {
    throw 'Duration must not be empty.'
}

$RemoteDir = $RemoteDir.TrimEnd('/')
if ([string]::IsNullOrWhiteSpace($RemoteDir)) {
    throw 'RemoteDir must not be empty.'
}

$RemoteReportsDir = "$RemoteDir/reports"
$RemoteK6Script = "$RemoteDir/books-read.js"
$RemoteReport = "$RemoteReportsDir/books-read.html"
$RemoteLogin = "$VmUser@$VmIp"
$SkipTlsValue = if ($SkipTlsVerify.IsPresent) { 'true' } else { 'false' }
$Timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$LocalReport = Join-Path $LocalReportsDir "books-read-$Timestamp.html"

New-Item -ItemType Directory -Force -Path $LocalReportsDir | Out-Null

Write-Host "Reminder: if the target API uses a self-signed certificate, pass -SkipTlsVerify."
Write-Host "Local report path: $LocalReport"

Invoke-RemoteChecked -Command "mkdir -p $(ConvertTo-RemoteShellArg $RemoteDir) $(ConvertTo-RemoteShellArg $RemoteReportsDir)"
Invoke-CheckedNative -FilePath 'scp' -Arguments @($LocalK6Script, "${RemoteLogin}:$RemoteK6Script")
Invoke-RemoteChecked -Command 'docker pull grafana/k6'

$dockerArgs = @(
    'docker run --rm',
    '-e', "BASE_URL=$(ConvertTo-RemoteShellArg $BaseUrl)",
    '-e', "SKIP_TLS_VERIFY=$(ConvertTo-RemoteShellArg $SkipTlsValue)",
    '-e', "VUS=$(ConvertTo-RemoteShellArg ([string]$Vus))",
    '-e', "DURATION=$(ConvertTo-RemoteShellArg $Duration)",
    '-e', 'K6_WEB_DASHBOARD=true',
    '-e', 'K6_WEB_DASHBOARD_EXPORT=/reports/books-read.html'
)

if (-not [string]::IsNullOrWhiteSpace($AuthorId)) {
    $dockerArgs += @('-e', "AUTHOR_ID=$(ConvertTo-RemoteShellArg $AuthorId)")
}

if (-not [string]::IsNullOrWhiteSpace($BookId)) {
    $dockerArgs += @('-e', "BOOK_ID=$(ConvertTo-RemoteShellArg $BookId)")
}

$dockerArgs += @(
    '-v', "$(ConvertTo-RemoteShellArg "${RemoteDir}:/scripts:ro")",
    '-v', "$(ConvertTo-RemoteShellArg "${RemoteReportsDir}:/reports")",
    'grafana/k6 run /scripts/books-read.js'
)

$dockerCommand = $dockerArgs -join ' '

Write-Host "Remote command: $dockerCommand"
& ssh $RemoteLogin $dockerCommand
$k6ExitCode = $LASTEXITCODE

$remoteReportExistsCommand = "test -f $(ConvertTo-RemoteShellArg $RemoteReport)"
& ssh $RemoteLogin $remoteReportExistsCommand
if ($LASTEXITCODE -eq 0) {
    Invoke-CheckedNative -FilePath 'scp' -Arguments @("${RemoteLogin}:$RemoteReport", $LocalReport)
    Write-Host "Report copied to: $LocalReport"
}
else {
    Write-Warning "Remote HTML report was not found at $RemoteReport."
}

if ($k6ExitCode -ne 0) {
    throw "k6 failed with exit code $k6ExitCode. Threshold failures also return a non-zero exit code."
}

Write-Host "k6 completed successfully."
