[CmdletBinding()]
param(
    [string]$ServiceVmIp = "65.52.200.245",

    [string]$LoadTestVmIp = "20.106.120.73",

    [string]$BaseUrl = "https://65.52.200.245",

    [int]$Vus = 500,

    [string]$Duration = "5m",

    [Alias("DelayBetweenTestsSeconds")]
    [string]$IdleDuration = "5m"
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
$runStartedAt = Get-Date
$runFolderName = $runStartedAt.ToString("yyyyMMdd-HHmmss")
$runDirectory = Join-Path $reportsRoot $runFolderName
$childVerboseArgument = if ($VerbosePreference -eq "Continue") { " -Verbose" } else { "" }

$projectNames = @(
    "Dotnet"
    "Dotnet.Aot"
    "Dotnet.Dapper"
    "Dotnet.DapperAot"
    "Dotnet.Simple"
    "Dotnet.SimpleAot"
    "Golang"
    "GolangSimple"
    "Rust"
    "RustSimple"
)

if (-not (Test-Path -LiteralPath $perfTestRoot -PathType Container)) {
    throw "PerfTest directory was not found at '$perfTestRoot'."
}

New-Item -ItemType Directory -Force -Path $reportsRoot | Out-Null
New-Item -ItemType Directory -Force -Path $runDirectory | Out-Null

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

    if ($Line -match "(?<percent>\d{1,3}(?:\.\d+)?)%") {
        $percent = [math]::Round([double]$matches.percent)
        return [math]::Max(0, [math]::Min(100, [int]$percent))
    }

    return $null
}

function Remove-AnsiEscapeSequences {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Text
    )

    return [regex]::Replace($Text, "\x1B\[[0-9;?]*[ -/]*[@-~]", "")
}

function Convert-StringToDouble {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    $parsed = 0.0
    if ([double]::TryParse(
            $Value,
            [System.Globalization.NumberStyles]::Float,
            [System.Globalization.CultureInfo]::InvariantCulture,
            [ref]$parsed)) {
        return $parsed
    }

    return $null
}

function Convert-DurationToMilliseconds {
    param(
        [AllowNull()]
        [string]$Value
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $null
    }

    $trimmed = $Value.Trim()
    if ($trimmed -notmatch "^(?<value>\d+(?:\.\d+)?)(?<unit>ns|us|µs|ms|s|m|h)$") {
        return $null
    }

    $number = Convert-StringToDouble -Value $matches.value
    if ($null -eq $number) {
        return $null
    }

    switch ($matches.unit) {
        "ns" { return $number / 1000000.0 }
        "us" { return $number / 1000.0 }
        "µs" { return $number / 1000.0 }
        "ms" { return $number }
        "s" { return $number * 1000.0 }
        "m" { return $number * 60000.0 }
        "h" { return $number * 3600000.0 }
        default { return $null }
    }
}

function Convert-DurationToSeconds {
    param(
        [AllowNull()]
        [string]$Value
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $null
    }

    $trimmed = $Value.Trim()
    if ($trimmed -match "^\d+(?:\.\d+)?$") {
        return [int][math]::Round([double]$trimmed)
    }

    $milliseconds = Convert-DurationToMilliseconds -Value $trimmed
    if ($null -eq $milliseconds) {
        return $null
    }

    return [int][math]::Round($milliseconds / 1000.0)
}

function Convert-PercentToFraction {
    param(
        [AllowNull()]
        [string]$Value
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $null
    }

    $trimmed = $Value.Trim()
    if ($trimmed -notmatch "^(?<value>\d+(?:\.\d+)?)%$") {
        return $null
    }

    $number = Convert-StringToDouble -Value $matches.value
    if ($null -eq $number) {
        return $null
    }

    return $number / 100.0
}

function Convert-PerSecondValue {
    param(
        [AllowNull()]
        [string]$Value
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $null
    }

    $trimmed = $Value.Trim()
    if ($trimmed -notmatch "^(?<value>\d+(?:\.\d+)?)/s$") {
        return $null
    }

    return Convert-StringToDouble -Value $matches.value
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

            if (-not [string]::IsNullOrWhiteSpace($line)) {
                Write-Verbose $line
            }
        }

        $exitCode = $LASTEXITCODE
        Write-Progress -Id $ProgressId -Activity $Activity -Status "Completed" -Completed
        Write-Verbose ("Command completed with exit code {0}: {1}" -f $exitCode, $Command)
        return $exitCode
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
        $percentComplete = [math]::Min(100, [math]::Max(0, [math]::Floor(($elapsed / $Seconds) * 100)))
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

    $movedFiles = New-Object System.Collections.Generic.List[string]
    $newFiles = Get-ChildItem -LiteralPath $SourceRoot -File |
    Where-Object { -not $ExistingFiles.ContainsKey($_.FullName) }

    foreach ($file in $newFiles) {
        $destinationPath = Join-Path $DestinationDirectory $file.Name
        Move-Item -LiteralPath $file.FullName -Destination $destinationPath -Force
        $movedFiles.Add($destinationPath)
    }

    return $movedFiles
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

function New-DurationStats {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Avg,

        [Parameter(Mandatory = $true)]
        [string]$Min,

        [Parameter(Mandatory = $true)]
        [string]$Med,

        [Parameter(Mandatory = $true)]
        [string]$P90,

        [Parameter(Mandatory = $true)]
        [string]$P95,

        [Parameter(Mandatory = $true)]
        [string]$P99,

        [Parameter(Mandatory = $true)]
        [string]$Max
    )

    return [ordered]@{
        avg   = $Avg
        avgMs = Convert-DurationToMilliseconds -Value $Avg
        min   = $Min
        minMs = Convert-DurationToMilliseconds -Value $Min
        med   = $Med
        medMs = Convert-DurationToMilliseconds -Value $Med
        p90   = $P90
        p90Ms = Convert-DurationToMilliseconds -Value $P90
        p95   = $P95
        p95Ms = Convert-DurationToMilliseconds -Value $P95
        p99   = $P99
        p99Ms = Convert-DurationToMilliseconds -Value $P99
        max   = $Max
        maxMs = Convert-DurationToMilliseconds -Value $Max
    }
}

function Test-ThresholdExpression {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Expression,

        [Parameter(Mandatory = $true)]
        [string]$ActualValue
    )

    if ($Expression -notmatch "^(?<lhs>.+?)(?<operator><=|>=|<|>)(?<target>\d+(?:\.\d+)?)$") {
        return $null
    }

    $target = Convert-StringToDouble -Value $matches.target
    if ($null -eq $target) {
        return $null
    }

    $actualComparable = $null
    $actualDurationMs = Convert-DurationToMilliseconds -Value $ActualValue
    if ($null -ne $actualDurationMs) {
        $actualComparable = $actualDurationMs
    }
    else {
        $actualFraction = Convert-PercentToFraction -Value $ActualValue
        if ($null -ne $actualFraction) {
            $actualComparable = $actualFraction
        }
        else {
            $actualComparable = Convert-StringToDouble -Value $ActualValue
        }
    }

    if ($null -eq $actualComparable) {
        return $null
    }

    switch ($matches.operator) {
        "<" { return $actualComparable -lt $target }
        "<=" { return $actualComparable -le $target }
        ">" { return $actualComparable -gt $target }
        ">=" { return $actualComparable -ge $target }
        default { return $null }
    }
}

function Parse-K6PerfLog {
    param(
        [Parameter(Mandatory = $true)]
        [string]$LogPath
    )

    $result = [ordered]@{
        hasResults = $false
        overall    = [ordered]@{}
        endpoints  = [ordered]@{}
        thresholds = @()
    }

    if (-not (Test-Path -LiteralPath $LogPath -PathType Leaf)) {
        return $result
    }

    $lines = Get-Content -LiteralPath $LogPath
    $currentSection = ""
    $currentThresholdMetric = $null
    $currentThresholdEndpoint = $null

    foreach ($rawLine in $lines) {
        $line = Remove-AnsiEscapeSequences -Text $rawLine
        $trimmedLine = $line.Trim()

        if ($trimmedLine -match "THRESHOLDS") {
            $currentSection = "thresholds"
            continue
        }

        if ($trimmedLine -match "TOTAL RESULTS") {
            $currentSection = "total"
            continue
        }

        if ($trimmedLine -match "^HTTP$") {
            $currentSection = "http"
            continue
        }

        if ($trimmedLine -match "^EXECUTION$") {
            $currentSection = "execution"
            continue
        }

        if ($trimmedLine -match "^NETWORK$") {
            $currentSection = "network"
            continue
        }

        switch ($currentSection) {
            "thresholds" {
                if ($trimmedLine -match "^(?<metric>[A-Za-z0-9_\.]+)(?:\{endpoint:(?<endpoint>[^}]+)\})?$") {
                    $currentThresholdMetric = $matches.metric
                    $currentThresholdEndpoint = if ($matches.ContainsKey("endpoint") -and -not [string]::IsNullOrWhiteSpace($matches.endpoint)) { $matches.endpoint.Trim() } else { $null }
                    continue
                }

                if ($null -ne $currentThresholdMetric -and $trimmedLine -match ".*'(?<expression>[^']+)'\s+(?<actualName>[^=]+)=(?<actual>\S+)") {
                    $result.thresholds += [ordered]@{
                        metric     = $currentThresholdMetric
                        endpoint   = $currentThresholdEndpoint
                        expression = $matches.expression.Trim()
                        actualName = $matches.actualName.Trim()
                        actual     = $matches.actual.Trim()
                        passed     = Test-ThresholdExpression -Expression $matches.expression.Trim() -ActualValue $matches.actual.Trim()
                    }
                    continue
                }
            }
            "http" {
                if ($trimmedLine -match "^http_req_duration.*?:\s+avg=(?<avg>\S+)\s+min=(?<min>\S+)\s+med=(?<med>\S+)\s+p\(90\)=(?<p90>\S+)\s+p\(95\)=(?<p95>\S+)\s+p\(99\)=(?<p99>\S+)\s+max=(?<max>\S+)$") {
                    $result.overall.httpReqDuration = New-DurationStats -Avg $matches.avg -Min $matches.min -Med $matches.med -P90 $matches.p90 -P95 $matches.p95 -P99 $matches.p99 -Max $matches.max
                    $result.hasResults = $true
                    continue
                }

                if ($trimmedLine -match "^\{\s*endpoint:(?<endpoint>[^}]+)\s*\}.*?:\s+avg=(?<avg>\S+)\s+min=(?<min>\S+)\s+med=(?<med>\S+)\s+p\(90\)=(?<p90>\S+)\s+p\(95\)=(?<p95>\S+)\s+p\(99\)=(?<p99>\S+)\s+max=(?<max>\S+)$") {
                    $endpointName = $matches.endpoint.Trim()
                    $result.endpoints[$endpointName] = New-DurationStats -Avg $matches.avg -Min $matches.min -Med $matches.med -P90 $matches.p90 -P95 $matches.p95 -P99 $matches.p99 -Max $matches.max
                    $result.hasResults = $true
                    continue
                }

                if ($trimmedLine -match "^http_req_failed.*?:\s+(?<rate>\S+)") {
                    $result.overall.httpReqFailed = [ordered]@{
                        rate         = $matches.rate
                        rateFraction = Convert-PercentToFraction -Value $matches.rate
                    }
                    $result.hasResults = $true
                    continue
                }

                if ($trimmedLine -match "^http_reqs.*?:\s+(?<total>\d+)\s+(?<rate>\S+)$") {
                    $result.overall.httpReqs = [ordered]@{
                        total         = [int]$matches.total
                        totalDisplay  = $matches.total
                        rate          = $matches.rate
                        ratePerSecond = Convert-PerSecondValue -Value $matches.rate
                    }
                    $result.hasResults = $true
                    continue
                }
            }
            "execution" {
                if ($trimmedLine -match "^iteration_duration.*?:\s+avg=(?<avg>\S+)\s+min=(?<min>\S+)\s+med=(?<med>\S+)\s+p\(90\)=(?<p90>\S+)\s+p\(95\)=(?<p95>\S+)\s+p\(99\)=(?<p99>\S+)\s+max=(?<max>\S+)$") {
                    $result.overall.iterationDuration = New-DurationStats -Avg $matches.avg -Min $matches.min -Med $matches.med -P90 $matches.p90 -P95 $matches.p95 -P99 $matches.p99 -Max $matches.max
                    continue
                }

                if ($trimmedLine -match "^iterations.*?:\s+(?<total>\d+)\s+(?<rate>\S+)$") {
                    $result.overall.iterations = [ordered]@{
                        total         = [int]$matches.total
                        totalDisplay  = $matches.total
                        rate          = $matches.rate
                        ratePerSecond = Convert-PerSecondValue -Value $matches.rate
                    }
                    continue
                }
            }
            "network" {
                if ($trimmedLine -match "^data_received.*?:\s+(?<total>\d+(?:\.\d+)?\s+[A-Za-z]+)\s+(?<rate>\d+(?:\.\d+)?\s+[A-Za-z]+/s)$") {
                    $result.overall.dataReceived = [ordered]@{
                        total = $matches.total
                        rate  = $matches.rate
                    }
                    continue
                }

                if ($trimmedLine -match "^data_sent.*?:\s+(?<total>\d+(?:\.\d+)?\s+[A-Za-z]+)\s+(?<rate>\d+(?:\.\d+)?\s+[A-Za-z]+/s)$") {
                    $result.overall.dataSent = [ordered]@{
                        total = $matches.total
                        rate  = $matches.rate
                    }
                    continue
                }
            }
        }
    }

    return $result
}

function ConvertTo-HtmlText {
    param(
        [AllowNull()]
        [object]$Value
    )

    if ($null -eq $Value) {
        return ""
    }

    return [System.Net.WebUtility]::HtmlEncode([string]$Value)
}

function Get-StatusClass {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Status
    )

    switch ($Status) {
        "passed" { return "status-passed" }
        "perf_failed" { return "status-warning" }
        default { return "status-failed" }
    }
}

function Get-OptionalProperty {
    param(
        [AllowNull()]
        [object]$InputObject,

        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    if ($null -eq $InputObject) {
        return $null
    }

    if ($InputObject -is [System.Collections.IDictionary]) {
        if ($InputObject.Contains($Name)) {
            return $InputObject[$Name]
        }

        return $null
    }

    $property = $InputObject.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $null
    }

    return $property.Value
}

function New-BarChartHtml {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Title,

        [Parameter(Mandatory = $true)]
        [System.Collections.IEnumerable]$Items
    )

    $itemList = @($Items | Where-Object { $null -ne $_.Value })
    if ($itemList.Count -eq 0) {
        return ""
    }

    $maxValue = ($itemList | Measure-Object -Property Value -Maximum).Maximum
    if ($null -eq $maxValue -or $maxValue -le 0) {
        $maxValue = 1
    }

    $builder = New-Object System.Text.StringBuilder
    [void]$builder.AppendLine("<section class='panel'>")
    [void]$builder.AppendLine(("  <h3>{0}</h3>" -f (ConvertTo-HtmlText -Value $Title)))
    [void]$builder.AppendLine("  <div class='bar-chart'>")

    foreach ($item in ($itemList | Sort-Object -Property Value -Descending)) {
        $width = [math]::Max(1, [math]::Round(($item.Value / $maxValue) * 100, 2))
        [void]$builder.AppendLine("    <div class='bar-row'>")
        [void]$builder.AppendLine(("      <div class='bar-label'>{0}</div>" -f (ConvertTo-HtmlText -Value $item.Label)))
        [void]$builder.AppendLine("      <div class='bar-track'>")
        [void]$builder.AppendLine(("        <div class='bar-fill {0}' style='width: {1}%;'></div>" -f $item.CssClass, $width.ToString([System.Globalization.CultureInfo]::InvariantCulture)))
        [void]$builder.AppendLine("      </div>")
        [void]$builder.AppendLine(("      <div class='bar-value'>{0}</div>" -f (ConvertTo-HtmlText -Value $item.Display)))
        [void]$builder.AppendLine("    </div>")
    }

    [void]$builder.AppendLine("  </div>")
    [void]$builder.AppendLine("</section>")
    return $builder.ToString()
}

function New-RunReportHtml {
    param(
        [Parameter(Mandatory = $true)]
        [object]$RunSummary
    )

    $builder = New-Object System.Text.StringBuilder
    $projects = @($RunSummary.projects)
    $endpointNames = New-Object System.Collections.Generic.List[string]

    foreach ($project in $projects) {
        foreach ($endpointName in $project.perfMetrics.endpoints.Keys) {
            if (-not $endpointNames.Contains($endpointName)) {
                $endpointNames.Add($endpointName)
            }
        }
    }

    [void]$builder.AppendLine("<!DOCTYPE html>")
    [void]$builder.AppendLine("<html lang='en'>")
    [void]$builder.AppendLine("<head>")
    [void]$builder.AppendLine("  <meta charset='utf-8'>")
    [void]$builder.AppendLine("  <meta name='viewport' content='width=device-width, initial-scale=1'>")
    [void]$builder.AppendLine(("  <title>{0}</title>" -f (ConvertTo-HtmlText -Value ("Perf Comparison " + $RunSummary.runFolderName))))
    [void]$builder.AppendLine("  <style>")
    [void]$builder.AppendLine("    :root { color-scheme: light; --bg: #f5f7fb; --panel: #ffffff; --line: #d9e0ea; --text: #142033; --muted: #607086; --good: #1f8f5f; --warn: #c98315; --bad: #c43d3d; --bar: #2b6de0; --bar-warn: #d28b24; --bar-bad: #c54a4a; }")
    [void]$builder.AppendLine("    * { box-sizing: border-box; }")
    [void]$builder.AppendLine("    body { margin: 0; padding: 24px; background: var(--bg); color: var(--text); font-family: Segoe UI, Arial, sans-serif; }")
    [void]$builder.AppendLine("    h1, h2, h3 { margin: 0 0 12px 0; }")
    [void]$builder.AppendLine("    p { margin: 0 0 8px 0; }")
    [void]$builder.AppendLine("    .layout { display: grid; gap: 20px; }")
    [void]$builder.AppendLine("    .panel { background: var(--panel); border: 1px solid var(--line); border-radius: 14px; padding: 18px; box-shadow: 0 8px 24px rgba(20, 32, 51, 0.06); }")
    [void]$builder.AppendLine("    .meta-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(220px, 1fr)); gap: 12px; }")
    [void]$builder.AppendLine("    .meta-item { padding: 12px; border: 1px solid var(--line); border-radius: 10px; background: #fbfcff; }")
    [void]$builder.AppendLine("    .meta-label { color: var(--muted); font-size: 12px; text-transform: uppercase; letter-spacing: 0.04em; }")
    [void]$builder.AppendLine("    .meta-value { margin-top: 4px; font-size: 16px; font-weight: 600; }")
    [void]$builder.AppendLine("    table { width: 100%; border-collapse: collapse; }")
    [void]$builder.AppendLine("    th, td { padding: 10px 12px; border-bottom: 1px solid var(--line); text-align: left; vertical-align: top; }")
    [void]$builder.AppendLine("    th { color: var(--muted); font-size: 12px; text-transform: uppercase; letter-spacing: 0.04em; }")
    [void]$builder.AppendLine("    .status-pill { display: inline-block; padding: 4px 10px; border-radius: 999px; font-size: 12px; font-weight: 700; text-transform: uppercase; }")
    [void]$builder.AppendLine("    .status-passed { background: rgba(31, 143, 95, 0.12); color: var(--good); }")
    [void]$builder.AppendLine("    .status-warning { background: rgba(201, 131, 21, 0.12); color: var(--warn); }")
    [void]$builder.AppendLine("    .status-failed { background: rgba(196, 61, 61, 0.12); color: var(--bad); }")
    [void]$builder.AppendLine("    .chart-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(340px, 1fr)); gap: 20px; }")
    [void]$builder.AppendLine("    .bar-chart { display: grid; gap: 10px; }")
    [void]$builder.AppendLine("    .bar-row { display: grid; grid-template-columns: 140px minmax(120px, 1fr) 90px; gap: 12px; align-items: center; }")
    [void]$builder.AppendLine("    .bar-label, .bar-value { font-size: 14px; }")
    [void]$builder.AppendLine("    .bar-track { height: 14px; background: #e6ebf4; border-radius: 999px; overflow: hidden; }")
    [void]$builder.AppendLine("    .bar-fill { height: 100%; background: var(--bar); border-radius: 999px; }")
    [void]$builder.AppendLine("    .bar-fill.bar-fill-passed { background: var(--bar); }")
    [void]$builder.AppendLine("    .bar-fill.bar-fill-warning { background: var(--bar-warn); }")
    [void]$builder.AppendLine("    .bar-fill.bar-fill-failed { background: var(--bar-bad); }")
    [void]$builder.AppendLine("    a { color: #2358c5; text-decoration: none; }")
    [void]$builder.AppendLine("    a:hover { text-decoration: underline; }")
    [void]$builder.AppendLine("    .link-list { display: flex; flex-wrap: wrap; gap: 8px; }")
    [void]$builder.AppendLine("    .small { color: var(--muted); font-size: 13px; }")
    [void]$builder.AppendLine("  </style>")
    [void]$builder.AppendLine("</head>")
    [void]$builder.AppendLine("<body>")
    [void]$builder.AppendLine("  <div class='layout'>")
    [void]$builder.AppendLine("    <section class='panel'>")
    [void]$builder.AppendLine(("      <h1>{0}</h1>" -f (ConvertTo-HtmlText -Value ("Perf Comparison " + $RunSummary.runFolderName))))
    [void]$builder.AppendLine(("      <p class='small'>Generated {0}</p>" -f (ConvertTo-HtmlText -Value $RunSummary.generatedAtUtc)))
    [void]$builder.AppendLine("      <div class='meta-grid'>")
    [void]$builder.AppendLine(("        <div class='meta-item'><div class='meta-label'>Projects</div><div class='meta-value'>{0}</div></div>" -f $projects.Count))
    [void]$builder.AppendLine(("        <div class='meta-item'><div class='meta-label'>Service VM</div><div class='meta-value'>{0}</div></div>" -f (ConvertTo-HtmlText -Value $RunSummary.settings.serviceVmIp)))
    [void]$builder.AppendLine(("        <div class='meta-item'><div class='meta-label'>Load Test VM</div><div class='meta-value'>{0}</div></div>" -f (ConvertTo-HtmlText -Value $RunSummary.settings.loadTestVmIp)))
    [void]$builder.AppendLine(("        <div class='meta-item'><div class='meta-label'>Base URL</div><div class='meta-value'>{0}</div></div>" -f (ConvertTo-HtmlText -Value $RunSummary.settings.baseUrl)))
    [void]$builder.AppendLine(("        <div class='meta-item'><div class='meta-label'>VUs</div><div class='meta-value'>{0}</div></div>" -f (ConvertTo-HtmlText -Value $RunSummary.settings.vus)))
    [void]$builder.AppendLine(("        <div class='meta-item'><div class='meta-label'>Duration</div><div class='meta-value'>{0}</div></div>" -f (ConvertTo-HtmlText -Value $RunSummary.settings.duration)))
    [void]$builder.AppendLine(("        <div class='meta-item'><div class='meta-label'>Idle Duration</div><div class='meta-value'>{0}</div></div>" -f (ConvertTo-HtmlText -Value $RunSummary.settings.idleDuration)))
    [void]$builder.AppendLine("      </div>")
    [void]$builder.AppendLine("    </section>")

    [void]$builder.AppendLine("    <section class='panel'>")
    [void]$builder.AppendLine("      <h2>Project Status</h2>")
    [void]$builder.AppendLine("      <table>")
    [void]$builder.AppendLine("        <thead>")
    [void]$builder.AppendLine("          <tr><th>Project</th><th>Status</th><th>HTTP p95</th><th>HTTP p99</th><th>Req/s</th><th>Failed Rate</th><th>Artifacts</th></tr>")
    [void]$builder.AppendLine("        </thead>")
    [void]$builder.AppendLine("        <tbody>")

    foreach ($project in $projects) {
        $httpStats = $project.perfMetrics.overall.httpReqDuration
        $httpReqs = $project.perfMetrics.overall.httpReqs
        $httpFailed = $project.perfMetrics.overall.httpReqFailed
        $httpP95 = if ($null -ne $httpStats) { $httpStats.p95 } else { "n/a" }
        $httpP99 = if ($null -ne $httpStats) { $httpStats.p99 } else { "n/a" }
        $httpReqRate = if ($null -ne $httpReqs) { $httpReqs.rate } else { "n/a" }
        $httpFailedRate = if ($null -ne $httpFailed) { $httpFailed.rate } else { "n/a" }
        $artifactLinks = New-Object System.Collections.Generic.List[string]

        foreach ($artifactName in @("buildLog", "deployLog", "perfLog", "k6DashboardHtml")) {
            $artifactPath = $project.artifacts[$artifactName]
            if (-not [string]::IsNullOrWhiteSpace($artifactPath)) {
                $label = switch ($artifactName) {
                    "buildLog" { "build" }
                    "deployLog" { "deploy" }
                    "perfLog" { "perf" }
                    "k6DashboardHtml" { "k6 html" }
                    default { $artifactName }
                }
                $artifactLinks.Add(("<a href='{0}'>{1}</a>" -f (ConvertTo-HtmlText -Value $artifactPath), (ConvertTo-HtmlText -Value $label)))
            }
        }

        [void]$builder.AppendLine("          <tr>")
        [void]$builder.AppendLine(("            <td>{0}</td>" -f (ConvertTo-HtmlText -Value $project.project)))
        [void]$builder.AppendLine(("            <td><span class='status-pill {0}'>{1}</span></td>" -f (Get-StatusClass -Status $project.status), (ConvertTo-HtmlText -Value $project.status)))
        [void]$builder.AppendLine(("            <td>{0}</td>" -f (ConvertTo-HtmlText -Value $httpP95)))
        [void]$builder.AppendLine(("            <td>{0}</td>" -f (ConvertTo-HtmlText -Value $httpP99)))
        [void]$builder.AppendLine(("            <td>{0}</td>" -f (ConvertTo-HtmlText -Value $httpReqRate)))
        [void]$builder.AppendLine(("            <td>{0}</td>" -f (ConvertTo-HtmlText -Value $httpFailedRate)))
        [void]$builder.AppendLine(("            <td><div class='link-list'>{0}</div></td>" -f ($artifactLinks -join "")))
        [void]$builder.AppendLine("          </tr>")
    }

    [void]$builder.AppendLine("        </tbody>")
    [void]$builder.AppendLine("      </table>")
    [void]$builder.AppendLine("    </section>")

    $overviewChartHtml = @()

    $overviewChartHtml += New-BarChartHtml -Title "Overall HTTP p95 - ⬇️ better" -Items @(
        foreach ($project in $projects) {
            $stats = Get-OptionalProperty -InputObject (Get-OptionalProperty -InputObject $project.perfMetrics -Name "overall") -Name "httpReqDuration"
            if ($null -ne $stats -and $null -ne $stats.p95Ms) {
                [pscustomobject]@{
                    Label    = $project.project
                    Value    = $stats.p95Ms
                    Display  = $stats.p95
                    CssClass = switch ($project.status) {
                        "passed" { "bar-fill-passed" }
                        "perf_failed" { "bar-fill-warning" }
                        default { "bar-fill-failed" }
                    }
                }
            }
        }
    )

    $overviewChartHtml += New-BarChartHtml -Title "Overall HTTP p99 - ⬇️ better" -Items @(
        foreach ($project in $projects) {
            $stats = Get-OptionalProperty -InputObject (Get-OptionalProperty -InputObject $project.perfMetrics -Name "overall") -Name "httpReqDuration"
            if ($null -ne $stats -and $null -ne $stats.p99Ms) {
                [pscustomobject]@{
                    Label    = $project.project
                    Value    = $stats.p99Ms
                    Display  = $stats.p99
                    CssClass = switch ($project.status) {
                        "passed" { "bar-fill-passed" }
                        "perf_failed" { "bar-fill-warning" }
                        default { "bar-fill-failed" }
                    }
                }
            }
        }
    )

    $overviewChartHtml += New-BarChartHtml -Title "Failed Request Rate - ⬇️ better" -Items @(
        foreach ($project in $projects) {
            $stats = Get-OptionalProperty -InputObject (Get-OptionalProperty -InputObject $project.perfMetrics -Name "overall") -Name "httpReqFailed"
            if ($null -ne $stats -and $null -ne $stats.rateFraction) {
                [pscustomobject]@{
                    Label    = $project.project
                    Value    = $stats.rateFraction
                    Display  = $stats.rate
                    CssClass = switch ($project.status) {
                        "passed" { "bar-fill-passed" }
                        "perf_failed" { "bar-fill-warning" }
                        default { "bar-fill-failed" }
                    }
                }
            }
        }
    )

    $overviewChartHtml += New-BarChartHtml -Title "HTTP Requests per Second - ⬆️ better" -Items @(
        foreach ($project in $projects) {
            $stats = Get-OptionalProperty -InputObject (Get-OptionalProperty -InputObject $project.perfMetrics -Name "overall") -Name "httpReqs"
            if ($null -ne $stats -and $null -ne $stats.ratePerSecond) {
                [pscustomobject]@{
                    Label    = $project.project
                    Value    = $stats.ratePerSecond
                    Display  = $stats.rate
                    CssClass = switch ($project.status) {
                        "passed" { "bar-fill-passed" }
                        "perf_failed" { "bar-fill-warning" }
                        default { "bar-fill-failed" }
                    }
                }
            }
        }
    )

    $overviewChartHtml += New-BarChartHtml -Title "Iteration Duration p95 - ⬇️ better" -Items @(
        foreach ($project in $projects) {
            $stats = Get-OptionalProperty -InputObject (Get-OptionalProperty -InputObject $project.perfMetrics -Name "overall") -Name "iterationDuration"
            if ($null -ne $stats -and $null -ne $stats.p95Ms) {
                [pscustomobject]@{
                    Label    = $project.project
                    Value    = $stats.p95Ms
                    Display  = $stats.p95
                    CssClass = switch ($project.status) {
                        "passed" { "bar-fill-passed" }
                        "perf_failed" { "bar-fill-warning" }
                        default { "bar-fill-failed" }
                    }
                }
            }
        }
    )

    [void]$builder.AppendLine("    <section class='panel'>")
    [void]$builder.AppendLine("      <h2>Overview Charts</h2>")
    [void]$builder.AppendLine("      <div class='chart-grid'>")
    foreach ($chartHtml in $overviewChartHtml) {
        if (-not [string]::IsNullOrWhiteSpace($chartHtml)) {
            [void]$builder.Append($chartHtml)
        }
    }
    [void]$builder.AppendLine("      </div>")
    [void]$builder.AppendLine("    </section>")

    [void]$builder.AppendLine("    <section class='panel'>")
    [void]$builder.AppendLine("      <h2>Per-Endpoint Charts</h2>")

    foreach ($endpointName in ($endpointNames | Sort-Object)) {
        $endpointP95Chart = New-BarChartHtml -Title ("{0} p95 - ⬇️ better" -f $endpointName) -Items @(
            foreach ($project in $projects) {
                $stats = Get-OptionalProperty -InputObject (Get-OptionalProperty -InputObject $project.perfMetrics -Name "endpoints") -Name $endpointName
                if ($null -ne $stats -and $null -ne $stats.p95Ms) {
                    [pscustomobject]@{
                        Label    = $project.project
                        Value    = $stats.p95Ms
                        Display  = $stats.p95
                        CssClass = switch ($project.status) {
                            "passed" { "bar-fill-passed" }
                            "perf_failed" { "bar-fill-warning" }
                            default { "bar-fill-failed" }
                        }
                    }
                }
            }
        )

        $endpointP99Chart = New-BarChartHtml -Title ("{0} p99 - ⬇️ better" -f $endpointName) -Items @(
            foreach ($project in $projects) {
                $stats = Get-OptionalProperty -InputObject (Get-OptionalProperty -InputObject $project.perfMetrics -Name "endpoints") -Name $endpointName
                if ($null -ne $stats -and $null -ne $stats.p99Ms) {
                    [pscustomobject]@{
                        Label    = $project.project
                        Value    = $stats.p99Ms
                        Display  = $stats.p99
                        CssClass = switch ($project.status) {
                            "passed" { "bar-fill-passed" }
                            "perf_failed" { "bar-fill-warning" }
                            default { "bar-fill-failed" }
                        }
                    }
                }
            }
        )

        if ([string]::IsNullOrWhiteSpace($endpointP95Chart) -and [string]::IsNullOrWhiteSpace($endpointP99Chart)) {
            continue
        }

        [void]$builder.AppendLine(("      <h3>{0}</h3>" -f (ConvertTo-HtmlText -Value $endpointName)))
        [void]$builder.AppendLine("      <div class='chart-grid'>")
        if (-not [string]::IsNullOrWhiteSpace($endpointP95Chart)) {
            [void]$builder.Append($endpointP95Chart)
        }
        if (-not [string]::IsNullOrWhiteSpace($endpointP99Chart)) {
            [void]$builder.Append($endpointP99Chart)
        }
        [void]$builder.AppendLine("      </div>")
    }

    [void]$builder.AppendLine("    </section>")

    [void]$builder.AppendLine("    <section class='panel'>")
    [void]$builder.AppendLine("      <h2>Threshold Summary</h2>")
    [void]$builder.AppendLine("      <table>")
    [void]$builder.AppendLine("        <thead>")
    [void]$builder.AppendLine("          <tr><th>Project</th><th>Endpoint</th><th>Metric</th><th>Threshold</th><th>Actual</th><th>Result</th></tr>")
    [void]$builder.AppendLine("        </thead>")
    [void]$builder.AppendLine("        <tbody>")

    $thresholdRows = 0
    foreach ($project in $projects) {
        foreach ($threshold in $project.perfMetrics.thresholds) {
            $thresholdRows++
            $thresholdResultClass = if ($threshold.passed -eq $true) { "status-passed" } elseif ($threshold.passed -eq $false) { "status-failed" } else { "status-warning" }
            $thresholdResultLabel = if ($threshold.passed -eq $true) { "passed" } elseif ($threshold.passed -eq $false) { "failed" } else { "unknown" }

            [void]$builder.AppendLine("          <tr>")
            [void]$builder.AppendLine(("            <td>{0}</td>" -f (ConvertTo-HtmlText -Value $project.project)))
            [void]$builder.AppendLine(("            <td>{0}</td>" -f (ConvertTo-HtmlText -Value $threshold.endpoint)))
            [void]$builder.AppendLine(("            <td>{0}</td>" -f (ConvertTo-HtmlText -Value $threshold.metric)))
            [void]$builder.AppendLine(("            <td>{0}</td>" -f (ConvertTo-HtmlText -Value $threshold.expression)))
            [void]$builder.AppendLine(("            <td>{0}</td>" -f (ConvertTo-HtmlText -Value $threshold.actual)))
            [void]$builder.AppendLine(("            <td><span class='status-pill {0}'>{1}</span></td>" -f $thresholdResultClass, (ConvertTo-HtmlText -Value $thresholdResultLabel)))
            [void]$builder.AppendLine("          </tr>")
        }
    }

    if ($thresholdRows -eq 0) {
        [void]$builder.AppendLine("          <tr><td colspan='6'>No threshold data was parsed from the perf logs.</td></tr>")
    }

    [void]$builder.AppendLine("        </tbody>")
    [void]$builder.AppendLine("      </table>")
    [void]$builder.AppendLine("    </section>")

    [void]$builder.AppendLine("  </div>")
    [void]$builder.AppendLine("</body>")
    [void]$builder.AppendLine("</html>")
    return $builder.ToString()
}

$projects = @(Get-DeployableProjects -RootPath $repoRoot -ProjectNames $projectNames)

if ($projects.Count -eq 0) {
    throw "No deployable projects were found."
}

$buildCommand = ".\scripts\push-acr.ps1{0}" -f $childVerboseArgument
$deployCommand = ".\scripts\deploy.ps1 -VmIp ""{0}""{1}" -f $ServiceVmIp, $childVerboseArgument
$perfCommand = ".\scripts\run-k6.ps1 -VmIp {0} -BaseUrl {1} -SkipTlsVerify -Vus {2} -Duration {3}{4}" -f $LoadTestVmIp, $BaseUrl, $Vus, $Duration, $childVerboseArgument
$idleDurationSeconds = Convert-DurationToSeconds -Value $IdleDuration
if ($null -eq $idleDurationSeconds) {
    throw "IdleDuration must be a valid duration value, for example '30s', '5m', or '300'."
}

$results = New-Object System.Collections.Generic.List[object]

Write-Verbose ("Projects selected for testing: {0}" -f ($projects.Name -join ", "))
Write-Verbose ("Run directory: {0}" -f $runDirectory)
Write-Verbose ("Build command: {0}" -f $buildCommand)
Write-Verbose ("Deploy command: {0}" -f $deployCommand)
Write-Verbose ("Perf command: {0}" -f $perfCommand)
Write-Verbose ("Idle duration: {0} ({1} seconds)" -f $IdleDuration, $idleDurationSeconds)

for ($index = 0; $index -lt $projects.Count; $index++) {
    $project = $projects[$index]
    $projectNumber = $index + 1
    $startedAt = Get-Date
    $projectReportDirectory = Join-Path $runDirectory $project.Name
    $buildLog = Join-Path $projectReportDirectory "build.log"
    $deployLog = Join-Path $projectReportDirectory "deploy.log"
    $perfLog = Join-Path $projectReportDirectory "perf.log"
    $metadataPath = Join-Path $projectReportDirectory "metadata.json"

    New-Item -ItemType Directory -Force -Path $projectReportDirectory | Out-Null

    Write-Progress -Id 1 -Activity "Project sweep" -Status ("Project {0} of {1}: {2}" -f $projectNumber, $projects.Count, $project.Name) -PercentComplete ([math]::Floor(($index / $projects.Count) * 100))

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
            [void](Move-NewPerfReports -SourceRoot $reportsRoot -ExistingFiles $perfExistingFiles -DestinationDirectory $projectReportDirectory)

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
    $k6ReportFile = Get-ChildItem -LiteralPath $projectReportDirectory -Filter "books-read-*.html" -File -ErrorAction SilentlyContinue | Select-Object -First 1
    $perfMetrics = Parse-K6PerfLog -LogPath $perfLog

    $artifacts = [ordered]@{
        buildLog        = "./{0}/build.log" -f $project.Name
        deployLog       = "./{0}/deploy.log" -f $project.Name
        perfLog         = "./{0}/perf.log" -f $project.Name
        metadataJson    = "./{0}/metadata.json" -f $project.Name
        k6DashboardHtml = if ($null -ne $k6ReportFile) { "./{0}/{1}" -f $project.Name, $k6ReportFile.Name } else { $null }
    }

    $metadata = [ordered]@{
        project             = $project.Name
        projectDirectory    = $project.FullName
        runFolderName       = $runFolderName
        startedAtUtc        = $startedAt.ToUniversalTime().ToString("o")
        finishedAtUtc       = $finishedAt.ToUniversalTime().ToString("o")
        status              = $status
        buildCommand        = $buildCommand
        buildExitCode       = $buildExitCode
        deployCommand       = $deployCommand
        deployExitCode      = $deployExitCode
        perfCommand         = $perfCommand
        perfExitCode        = $perfExitCode
        serviceVmIp         = $ServiceVmIp
        loadTestVmIp        = $LoadTestVmIp
        baseUrl             = $BaseUrl
        vus                 = $Vus
        duration            = $Duration
        idleDuration        = $IdleDuration
        idleDurationSeconds = $idleDurationSeconds
        artifacts           = $artifacts
        perfMetrics         = $perfMetrics
    }

    $metadata | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $metadataPath
    $results.Add([pscustomobject]$metadata)

    Write-Verbose ("Status for {0}: {1}" -f $project.Name, $status)

    if ($index -lt ($projects.Count - 1)) {
        Write-Verbose ("Waiting {0} ({1} seconds) before the next test." -f $IdleDuration, $idleDurationSeconds)
        Wait-WithProgress -Seconds $idleDurationSeconds -ProgressId 4 -Activity ("{0}: Idling" -f $project.Name)
    }
}

$summaryPath = Join-Path $runDirectory "summary.json"
$htmlPath = Join-Path $runDirectory "index.html"
$runFinishedAt = Get-Date
$projectResults = [object[]]$results

Write-Verbose ("Preparing run summary for {0} project(s)." -f $projectResults.Count)
Write-Verbose ("Summary path: {0}" -f $summaryPath)
Write-Verbose ("Aggregate report path: {0}" -f $htmlPath)

$runSummary = [ordered]@{
    runFolderName  = $runFolderName
    runDirectory   = $runDirectory
    startedAtUtc   = $runStartedAt.ToUniversalTime().ToString("o")
    finishedAtUtc  = $runFinishedAt.ToUniversalTime().ToString("o")
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    settings       = [ordered]@{
        serviceVmIp         = $ServiceVmIp
        loadTestVmIp        = $LoadTestVmIp
        baseUrl             = $BaseUrl
        vus                 = $Vus
        duration            = $Duration
        idleDuration        = $IdleDuration
        idleDurationSeconds = $idleDurationSeconds
    }
    commands       = [ordered]@{
        build  = $buildCommand
        deploy = $deployCommand
        perf   = $perfCommand
    }
    projects       = $projectResults
}

Write-Verbose ("Run summary object created for folder '{0}'." -f $runFolderName)
Write-Verbose "Writing summary.json"
$runSummary | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $summaryPath

Write-Verbose "Building aggregate HTML report"
$reportHtml = New-RunReportHtml -RunSummary $runSummary
Write-Verbose ("Aggregate HTML generated ({0} characters)." -f $reportHtml.Length)

Write-Verbose "Writing index.html"
Set-Content -LiteralPath $htmlPath -Value $reportHtml
Write-Verbose "Summary artifacts written successfully"

Write-Progress -Id 1 -Activity "Project sweep" -Completed
Write-Section "Summary"
$results | Format-Table project, status, buildExitCode, deployExitCode, perfExitCode -AutoSize
Write-Host "Run folder: $runDirectory"
Write-Host "Summary written to $summaryPath"
Write-Host "Aggregate report written to $htmlPath"

if ($results.Where({ $_.status -ne "passed" }).Count -gt 0) {
    exit 1
}
