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

$projectNames = @(
    "Dotnet"
    "Dotnet.Aot"
    # "Dotnet.Dapper"
    # "Dotnet.DapperAot"
    # "Dotnet.Simple"
    # "Golang"
    # "GolangSimple"
    # "Rust"
    # "RustSimple"
)

if (-not (Test-Path -LiteralPath $perfTestRoot -PathType Container)) {
    throw "PerfTest directory was not found at '$perfTestRoot'."
}

New-Item -ItemType Directory -Force -Path $reportsRoot | Out-Null
$childVerboseArgument = if ($VerbosePreference -eq 'Continue') { ' -Verbose' } else { '' }

function Write-Section {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    Write-Host ""
    Write-Host "=== $Message ==="
}

function Get-PercentFromLine {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Line
    )

    if ($Line -match '(?<percent>\d{1,3}(?:\.\d+)?)%') {
        $percent = [math]::Round([double]$matches.percent)
        return [math]::Max(0, [math]::Min(100, [int]$percent))
    }

    return $null
}

function Get-DeployableProjects {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RootPath,

        [Parameter(Mandatory = $true)]
        [string[]]$ProjectNames
    )

    $projects = New-Object System.Collections.Generic.List[System.IO.DirectoryInfo]

    foreach ($projectName in $ProjectNames) {
        $projectPath = Join-Path $RootPath $projectName

        if (-not (Test-Path -LiteralPath $projectPath -PathType Container)) {
            throw "Configured project directory was not found: '$projectPath'."
        }

        $projects.Add((Get-Item -LiteralPath $projectPath))
    }

    return $projects
}

function Invoke-LoggedPowerShellCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string]$WorkingDirectory,

        [Parameter(Mandatory = $true)]
        [string]$Command,

        [Parameter(Mandatory = $true)]
        [string]$LogPath,

        [Parameter(Mandatory = $true)]
        [string]$Activity,

        [Parameter(Mandatory = $true)]
        [int]$ProgressId
    )

    Add-Content -LiteralPath $LogPath -Value ("[{0:u}] PS> {1}" -f (Get-Date), $Command)
    Write-Progress -Id $ProgressId -Activity $Activity -Status "Starting"
    Write-Verbose ("Running command in '{0}': {1}" -f $WorkingDirectory, $Command)

    Push-Location $WorkingDirectory
    try {
        $lastPercent = $null

        & $currentPowerShellExe -NoProfile -Command $Command 2>&1 | ForEach-Object {
            $line = $_.ToString()
            $statusLine = if ([string]::IsNullOrWhiteSpace($line)) { "..." } else { $line }
            Add-Content -LiteralPath $LogPath -Value $line

            $percent = Get-PercentFromLine -Line $line
            if ($null -ne $percent) {
                $lastPercent = $percent
                Write-Progress -Id $ProgressId -Activity $Activity -Status $statusLine -PercentComplete $percent
            }
            elseif ($null -ne $lastPercent) {
                Write-Progress -Id $ProgressId -Activity $Activity -Status $statusLine -PercentComplete $lastPercent
            }
            else {
                Write-Progress -Id $ProgressId -Activity $Activity -Status $statusLine
            }

            Write-Host $line
        }

        Write-Progress -Id $ProgressId -Activity $Activity -Status "Completed" -Completed
        Write-Verbose ("Command completed with exit code {0}: {1}" -f $LASTEXITCODE, $Command)
        return $LASTEXITCODE
    }
    finally {
        Pop-Location
    }
}

function Wait-WithProgress {
    param(
        [Parameter(Mandatory = $true)]
        [int]$Seconds,

        [Parameter(Mandatory = $true)]
        [int]$ProgressId,

        [Parameter(Mandatory = $true)]
        [string]$Activity
    )

    if ($Seconds -le 0) {
        return
    }

    $startedAt = Get-Date

    for ($remaining = $Seconds; $remaining -gt 0; $remaining--) {
        $elapsed = [int]((Get-Date) - $startedAt).TotalSeconds
        $percentComplete = [math]::Floor(($elapsed / $Seconds) * 100)
        Write-Progress -Id $ProgressId -Activity $Activity -Status "$remaining seconds remaining" -PercentComplete $percentComplete
        Start-Sleep -Seconds 1
    }

    Write-Progress -Id $ProgressId -Activity $Activity -Completed
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

$projects = @(Get-DeployableProjects -RootPath $repoRoot -ProjectNames $projectNames)

if ($projects.Count -eq 0) {
    throw "No deployable projects were found."
}

$buildCommand = '.\scripts\push-acr.ps1{0}' -f $childVerboseArgument
$deployCommand = '.\scripts\deploy.ps1 -VmIp "{0}"{1}' -f $ServiceVmIp, $childVerboseArgument
$perfCommand = '.\scripts\run-k6.ps1 -VmIp {0} -BaseUrl {1} -SkipTlsVerify -Vus {2} -Duration {3}{4}' -f $LoadTestVmIp, $BaseUrl, $Vus, $Duration, $childVerboseArgument
$results = New-Object System.Collections.Generic.List[object]

Write-Verbose ("Projects selected for testing: {0}" -f ($projects.Name -join ", "))
Write-Verbose ("Build command: {0}" -f $buildCommand)
Write-Verbose ("Deploy command: {0}" -f $deployCommand)
Write-Verbose ("Perf command: {0}" -f $perfCommand)

for ($index = 0; $index -lt $projects.Count; $index++) {
    $project = $projects[$index]
    $projectNumber = $index + 1
    $startedAt = Get-Date
    $reportFolderName = "{0}-{1}" -f $startedAt.ToString("yyyyMMdd-HHmmss"), $project.Name
    $reportDirectory = Join-Path $reportsRoot $reportFolderName
    $buildLog = Join-Path $reportDirectory "build.log"
    $deployLog = Join-Path $reportDirectory "deploy.log"
    $perfLog = Join-Path $reportDirectory "perf.log"
    $metadataPath = Join-Path $reportDirectory "metadata.json"

    New-Item -ItemType Directory -Force -Path $reportDirectory | Out-Null

    Write-Progress -Id 1 -Activity "Project sweep" -Status ("Project {0} of {1}: {2}" -f $projectNumber, $projects.Count, $project.Name) -PercentComplete ([math]::Floor(($index / $projects.Count) * 100))
    Write-Section ("Testing {0} ({1}/{2})" -f $project.Name, $projectNumber, $projects.Count)

    $buildExitCode = Invoke-LoggedPowerShellCommand -WorkingDirectory $project.FullName -Command $buildCommand -LogPath $buildLog -Activity ("{0}: Building" -f $project.Name) -ProgressId 2
    $deployExitCode = $null
    $perfExitCode = $null
    $status = "build_failed"

    if ($buildExitCode -eq 0) {
        $deployExitCode = Invoke-LoggedPowerShellCommand -WorkingDirectory $project.FullName -Command $deployCommand -LogPath $deployLog -Activity ("{0}: Deploying" -f $project.Name) -ProgressId 2
        $status = "deploy_failed"

        if ($deployExitCode -eq 0) {
            $perfExistingFiles = New-ExistingFileMap -Path $reportsRoot
            $perfExitCode = Invoke-LoggedPowerShellCommand -WorkingDirectory $perfTestRoot -Command $perfCommand -LogPath $perfLog -Activity ("{0}: Perf Testing" -f $project.Name) -ProgressId 3
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
    }
    else {
        Set-Content -LiteralPath $deployLog -Value "Deploy skipped because build failed."
        Set-Content -LiteralPath $perfLog -Value "Perf test skipped because build failed."
    }

    $finishedAt = Get-Date
    $metadata = [ordered]@{
        project                  = $project.Name
        projectDirectory         = $project.FullName
        startedAtUtc             = $startedAt.ToUniversalTime().ToString("o")
        finishedAtUtc            = $finishedAt.ToUniversalTime().ToString("o")
        status                   = $status
        buildCommand             = $buildCommand
        buildExitCode            = $buildExitCode
        deployCommand            = $deployCommand
        deployExitCode           = $deployExitCode
        perfCommand              = $perfCommand
        perfExitCode             = $perfExitCode
        serviceVmIp              = $ServiceVmIp
        loadTestVmIp             = $LoadTestVmIp
        baseUrl                  = $BaseUrl
        vus                      = $Vus
        duration                 = $Duration
        delayBetweenTestsSeconds = $DelayBetweenTestsSeconds
    }

    $metadata | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $metadataPath
    $results.Add([pscustomobject]$metadata)

    Write-Host ("Status for {0}: {1}" -f $project.Name, $status)

    if ($index -lt ($projects.Count - 1)) {
        Write-Host ("Waiting {0} seconds before the next test." -f $DelayBetweenTestsSeconds)
        Wait-WithProgress -Seconds $DelayBetweenTestsSeconds -ProgressId 4 -Activity ("{0}: Idling" -f $project.Name)
    }
}

$summaryPath = Join-Path $reportsRoot ("summary-{0}.json" -f (Get-Date -Format "yyyyMMdd-HHmmss"))
$results | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $summaryPath

Write-Progress -Id 1 -Activity "Project sweep" -Completed
Write-Section "Summary"
$results | Format-Table project, status, deployExitCode, perfExitCode -AutoSize
Write-Host "Summary written to $summaryPath"

if ($results.Where({ $_.status -ne "passed" }).Count -gt 0) {
    exit 1
}
