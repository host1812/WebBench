[CmdletBinding()]
param(
    [string]$ServiceVmIp = "65.52.200.245",

    [string]$LoadTestVmIp = "20.106.120.73",

    [string]$BaseUrl = "https://65.52.200.245",

    [int]$Vus = 500,

    [string]$Duration = "5m",

    [int]$DelayBetweenTestsSeconds = 300
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ($PSVersionTable.PSVersion.Major -lt 7) {
    throw "This script requires PowerShell 7 or later."
}

$repoRoot = $PSScriptRoot
$perfTestRoot = Join-Path $repoRoot "PerfTest"
$reportsRoot = Join-Path $perfTestRoot "perf\reports"
$currentPowerShellExe = (Get-Process -Id $PID).Path
$excludedDirectories = @("Docs", "Infra", "PerfTest")

if (-not (Test-Path -LiteralPath $perfTestRoot -PathType Container)) {
    throw "PerfTest directory was not found at '$perfTestRoot'."
}

New-Item -ItemType Directory -Force -Path $reportsRoot | Out-Null

function Write-Section {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    Write-Host ""
    Write-Host "=== $Message ==="
}

function Get-DeployableProjects {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RootPath
    )

    return Get-ChildItem -LiteralPath $RootPath -Directory |
        Where-Object { $_.Name -notin $excludedDirectories } |
        Sort-Object Name
}

function Invoke-LoggedPowerShellCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string]$WorkingDirectory,

        [Parameter(Mandatory = $true)]
        [string]$Command,

        [Parameter(Mandatory = $true)]
        [string]$LogPath
    )

    Add-Content -LiteralPath $LogPath -Value ("[{0:u}] PS> {1}" -f (Get-Date), $Command)

    Push-Location $WorkingDirectory
    try {
        & $currentPowerShellExe -NoProfile -Command $Command 2>&1 | Tee-Object -FilePath $LogPath -Append
        return $LASTEXITCODE
    }
    finally {
        Pop-Location
    }
}

function Move-NewPerfReports {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SourceRoot,

        [Parameter(Mandatory = $true)]
        [hashtable]$ExistingFiles,

        [Parameter(Mandatory = $true)]
        [string]$DestinationDirectory
    )

    $newFiles = Get-ChildItem -LiteralPath $SourceRoot -File |
        Where-Object { -not $ExistingFiles.ContainsKey($_.FullName) }

    foreach ($file in $newFiles) {
        $destinationPath = Join-Path $DestinationDirectory $file.Name
        Move-Item -LiteralPath $file.FullName -Destination $destinationPath -Force
    }
}

function New-ExistingFileMap {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $map = @{}

    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        return $map
    }

    foreach ($file in Get-ChildItem -LiteralPath $Path -File) {
        $map[$file.FullName] = $true
    }

    return $map
}

$projects = @(Get-DeployableProjects -RootPath $repoRoot)

if ($projects.Count -eq 0) {
    throw "No deployable projects were found."
}

$deployCommand = '.\scripts\push-acr.ps1 && .\scripts\deploy.ps1 -VmIp "{0}"' -f $ServiceVmIp
$perfCommand = '.\scripts\run-k6.ps1 -VmIp {0} -BaseUrl {1} -SkipTlsVerify -Vus {2} -Duration {3}' -f $LoadTestVmIp, $BaseUrl, $Vus, $Duration
$results = New-Object System.Collections.Generic.List[object]

for ($index = 0; $index -lt $projects.Count; $index++) {
    $project = $projects[$index]
    $startedAt = Get-Date
    $reportFolderName = "{0}-{1}" -f $startedAt.ToString("yyyyMMdd-HHmmss"), $project.Name
    $reportDirectory = Join-Path $reportsRoot $reportFolderName
    $deployLog = Join-Path $reportDirectory "deploy.log"
    $perfLog = Join-Path $reportDirectory "perf.log"
    $metadataPath = Join-Path $reportDirectory "metadata.json"

    New-Item -ItemType Directory -Force -Path $reportDirectory | Out-Null

    Write-Section "Testing $($project.Name)"

    $deployExitCode = Invoke-LoggedPowerShellCommand -WorkingDirectory $project.FullName -Command $deployCommand -LogPath $deployLog
    $perfExitCode = $null
    $status = "deploy_failed"

    if ($deployExitCode -eq 0) {
        $perfExistingFiles = New-ExistingFileMap -Path $reportsRoot
        $perfExitCode = Invoke-LoggedPowerShellCommand -WorkingDirectory $perfTestRoot -Command $perfCommand -LogPath $perfLog
        Move-NewPerfReports -SourceRoot $reportsRoot -ExistingFiles $perfExistingFiles -DestinationDirectory $reportDirectory

        if ($perfExitCode -eq 0) {
            $status = "passed"
        }
        else {
            $status = "perf_failed"
        }
    }
    else {
        Set-Content -LiteralPath $perfLog -Value "Perf test skipped because deploy failed."
    }

    $finishedAt = Get-Date
    $metadata = [ordered]@{
        project = $project.Name
        projectDirectory = $project.FullName
        startedAtUtc = $startedAt.ToUniversalTime().ToString("o")
        finishedAtUtc = $finishedAt.ToUniversalTime().ToString("o")
        status = $status
        deployCommand = $deployCommand
        deployExitCode = $deployExitCode
        perfCommand = $perfCommand
        perfExitCode = $perfExitCode
        serviceVmIp = $ServiceVmIp
        loadTestVmIp = $LoadTestVmIp
        baseUrl = $BaseUrl
        vus = $Vus
        duration = $Duration
        delayBetweenTestsSeconds = $DelayBetweenTestsSeconds
    }

    $metadata | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $metadataPath
    $results.Add([pscustomobject]$metadata)

    Write-Host ("Status for {0}: {1}" -f $project.Name, $status)

    if ($index -lt ($projects.Count - 1)) {
        Write-Host ("Waiting {0} seconds before the next test." -f $DelayBetweenTestsSeconds)
        Start-Sleep -Seconds $DelayBetweenTestsSeconds
    }
}

$summaryPath = Join-Path $reportsRoot ("summary-{0}.json" -f (Get-Date -Format "yyyyMMdd-HHmmss"))
$results | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $summaryPath

Write-Section "Summary"
$results | Format-Table project, status, deployExitCode, perfExitCode -AutoSize
Write-Host "Summary written to $summaryPath"

if ($results.Where({ $_.status -ne "passed" }).Count -gt 0) {
    exit 1
}
