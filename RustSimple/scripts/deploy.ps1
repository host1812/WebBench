[CmdletBinding()]
param(
    [string]$VmIp,
    [string]$VmUser,
    [string]$RemoteDir,
    [string]$HttpsPort,
    [string]$AcrName
)

$ErrorActionPreference = "Stop"

function Assert-Command {
    param([string]$Name)

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command '$Name' was not found on PATH."
    }
}

function Assert-Path {
    param([string]$Path)

    if (-not (Test-Path $Path)) {
        throw "Required path '$Path' was not found."
    }
}

function Import-DotEnv {
    param([string]$Path)

    $values = @{}

    foreach ($line in Get-Content $Path) {
        $trimmed = $line.Trim()
        if ([string]::IsNullOrWhiteSpace($trimmed) -or $trimmed.StartsWith("#")) {
            continue
        }

        $parts = $trimmed.Split("=", 2)
        if ($parts.Count -ne 2) {
            continue
        }

        $key = $parts[0].Trim()
        $value = $parts[1].Trim()
        $values[$key] = $value
        Set-Item -Path "Env:$key" -Value $value
    }

    return $values
}

function Invoke-Remote {
    param([string]$Command)

    & ssh $Target $Command
    if ($LASTEXITCODE -ne 0) {
        throw "Remote command failed: $Command"
    }
}

function Invoke-RemoteOptional {
    param([string]$Command)

    $output = & ssh $Target $Command 2>&1
    $exitCode = $LASTEXITCODE

    foreach ($line in $output) {
        if ($exitCode -eq 0) {
            Write-Host $line
        }
        else {
            Write-Warning "$line"
        }
    }

    return $exitCode -eq 0
}

function Copy-ToRemote {
    param(
        [string]$Source,
        [string]$Destination,
        [switch]$Recurse
    )

    if ($Recurse) {
        & scp -r $Source "${Target}:$Destination"
    }
    else {
        & scp $Source "${Target}:$Destination"
    }

    if ($LASTEXITCODE -ne 0) {
        throw "Failed to copy '$Source' to '$Destination'."
    }
}

Assert-Command "ssh"
Assert-Command "scp"

$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$EnvFile = Join-Path $RepoRoot ".env"
$ComposeFile = Join-Path $RepoRoot "compose.vm.yaml"

Assert-Path $EnvFile
Assert-Path $ComposeFile

$Settings = Import-DotEnv $EnvFile

$VmIp = if ($VmIp) { $VmIp } else { $Settings["VM_IP"] }
$VmUser = if ($VmUser) { $VmUser } else { $Settings["VM_USER"] }
$RemoteDir = if ($RemoteDir) { $RemoteDir } else { $Settings["REMOTE_DIR"] }
$HttpsPort = if ($HttpsPort) { $HttpsPort } else { $Settings["PUBLIC_HTTPS_PORT"] }
$AcrName = if ($AcrName) { $AcrName } else { $Settings["ACR_NAME"] }
$TlsCertDays = if ($Settings["TLS_CERT_DAYS"]) { $Settings["TLS_CERT_DAYS"] } else { "365" }

foreach ($setting in @(
    @{ Name = "VM_IP"; Value = $VmIp },
    @{ Name = "VM_USER"; Value = $VmUser },
    @{ Name = "REMOTE_DIR"; Value = $RemoteDir },
    @{ Name = "PUBLIC_HTTPS_PORT"; Value = $HttpsPort },
    @{ Name = "ACR_NAME"; Value = $AcrName }
)) {
    if ([string]::IsNullOrWhiteSpace($setting.Value)) {
        throw "Missing required setting '$($setting.Name)' in .env."
    }
}

$Target = "$VmUser@$VmIp"

Write-Host "Removing existing Docker Compose workloads from the VM..."
Invoke-RemoteOptional "docker ps -aq --filter label=com.docker.compose.project | xargs -r docker rm -f" | Out-Null
Invoke-RemoteOptional "docker network ls -q --filter label=com.docker.compose.project | xargs -r docker network rm" | Out-Null
Invoke-RemoteOptional "docker volume ls -q --filter label=com.docker.compose.project | xargs -r docker volume rm -f" | Out-Null
Invoke-Remote "case '$RemoteDir' in /opt/*) sudo rm -rf '$RemoteDir' ;; *) echo 'Refusing to remove unexpected remote directory: $RemoteDir' >&2; exit 1 ;; esac"

Invoke-Remote "sudo mkdir -p '$RemoteDir' && sudo chown -R `$(id -u):`$(id -g) '$RemoteDir'"

Copy-ToRemote $EnvFile "$RemoteDir/.env"
Copy-ToRemote $ComposeFile "$RemoteDir/compose.yaml"

Invoke-Remote "chmod 600 '$RemoteDir/.env'"
Invoke-Remote "mkdir -p '$RemoteDir/certs'"
Invoke-Remote "if ! command -v openssl >/dev/null 2>&1; then sudo apt-get update && sudo apt-get install -y openssl; fi"
Invoke-Remote "openssl req -x509 -nodes -newkey rsa:4096 -sha256 -days '$TlsCertDays' -keyout '$RemoteDir/certs/server.key' -out '$RemoteDir/certs/server.crt' -subj '/CN=$VmIp' -addext 'subjectAltName=IP:$VmIp,DNS:localhost'"

Invoke-Remote "az login --identity"
Invoke-Remote "az acr login --name '$AcrName'"

Invoke-Remote "cd '$RemoteDir' && docker compose --env-file .env pull"
Invoke-Remote "cd '$RemoteDir' && docker compose --env-file .env up -d postgres"
Invoke-Remote "cd '$RemoteDir' && docker compose --env-file .env run --rm migrate"
Invoke-Remote "cd '$RemoteDir' && docker compose --env-file .env up -d app"
Invoke-Remote "cd '$RemoteDir' && docker compose --env-file .env ps"

Write-Host "Deployment completed."
Write-Host "Health check: https://${VmIp}:$HttpsPort/health"
Write-Host "The Rust service is terminating TLS directly with a generated self-signed certificate."
Write-Host "Use curl -k or trust $RemoteDir/certs/server.crt on the client."
