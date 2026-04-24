[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$VmIp
)

$ErrorActionPreference = "Stop"

$VmUser = "azureuser"
$RemoteDir = "/opt/books-service"
$HttpsPort = "443"
$AcrName = "acrwebbench4sayzpoemqsyo"

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
$CollectorConfigFile = Join-Path $RepoRoot "otel-collector-config.yaml"
$NginxConfigFile = (Resolve-Path (Join-Path $RepoRoot "..\Infra\nginx.vm.conf")).Path
$Target = "$VmUser@$VmIp"
$CleanupRemoteDirs = @($RemoteDir, "/opt/authors-books-service") | Select-Object -Unique

Assert-Path $EnvFile
Assert-Path $ComposeFile
Assert-Path $CollectorConfigFile
Assert-Path $NginxConfigFile

foreach ($CleanupRemoteDir in $CleanupRemoteDirs) {
    Invoke-RemoteOptional "if [ -d '$CleanupRemoteDir' ] && [ -f '$CleanupRemoteDir/compose.yaml' ]; then cd '$CleanupRemoteDir' && docker compose down --remove-orphans --volumes; else true; fi" | Out-Null
    Invoke-RemoteOptional "project=`$(basename '$CleanupRemoteDir'); docker ps -aq --filter `"label=com.docker.compose.project=`$project`" | xargs -r docker rm -f; docker network ls -q --filter `"label=com.docker.compose.project=`$project`" | xargs -r docker network rm; docker volume ls -q --filter `"label=com.docker.compose.project=`$project`" | xargs -r docker volume rm -f" | Out-Null
    Invoke-Remote "case '$CleanupRemoteDir' in /opt/*) sudo rm -rf '$CleanupRemoteDir' ;; *) echo 'Refusing to remove unexpected remote directory: $CleanupRemoteDir' >&2; exit 1 ;; esac"
}
Invoke-RemoteOptional "sudo rm -f /etc/letsencrypt/renewal-hooks/deploy/books-service-nginx.sh" | Out-Null
Invoke-Remote "sudo mkdir -p '$RemoteDir' && sudo chown -R `$(id -u):`$(id -g) '$RemoteDir'"

Copy-ToRemote $EnvFile "$RemoteDir/.env"
Copy-ToRemote $ComposeFile "$RemoteDir/compose.yaml"
Copy-ToRemote $CollectorConfigFile "$RemoteDir/otel-collector-config.yaml"
Copy-ToRemote $NginxConfigFile "$RemoteDir/nginx.vm.conf"

Invoke-Remote "chmod 600 '$RemoteDir/.env'"
Invoke-Remote "mkdir -p '$RemoteDir/certs' '$RemoteDir/certbot-webroot'"
Invoke-Remote "if ! command -v openssl >/dev/null 2>&1; then sudo apt-get update && sudo apt-get install -y openssl; fi"
Invoke-Remote "if [ ! -f '$RemoteDir/certs/server.crt' ] || [ ! -f '$RemoteDir/certs/server.key' ]; then openssl req -x509 -nodes -newkey rsa:4096 -sha256 -days 365 -keyout '$RemoteDir/certs/server.key' -out '$RemoteDir/certs/server.crt' -subj '/CN=$VmIp' -addext 'subjectAltName=IP:$VmIp,DNS:localhost'; fi"

Invoke-Remote "az login --identity"
Invoke-Remote "az acr login --name '$AcrName'"

Invoke-Remote "cd '$RemoteDir' && docker compose pull && docker compose up -d gateway && docker compose ps"

$CertbotInstallCommand = "if command -v snap >/dev/null 2>&1; then sudo snap install core || true; sudo snap refresh core || true; sudo snap install --classic certbot || sudo snap refresh certbot || true; sudo ln -sf /snap/bin/certbot /usr/bin/certbot || true; fi; command -v certbot >/dev/null 2>&1"
$CertbotCommand = "sudo certbot certonly --non-interactive --agree-tos --register-unsafely-without-email --preferred-profile shortlived --webroot --webroot-path '$RemoteDir/certbot-webroot' --ip-address '$VmIp'"
$CertbotInstallHookCommand = "sudo mkdir -p /etc/letsencrypt/renewal-hooks/deploy && printf '%s\n' '#!/bin/sh' 'set -e' 'cp /etc/letsencrypt/live/$VmIp/fullchain.pem $RemoteDir/certs/server.crt' 'cp /etc/letsencrypt/live/$VmIp/privkey.pem $RemoteDir/certs/server.key' 'cd $RemoteDir' 'docker compose exec -T gateway nginx -s reload || true' | sudo tee /etc/letsencrypt/renewal-hooks/deploy/books-service-nginx.sh >/dev/null && sudo chmod +x /etc/letsencrypt/renewal-hooks/deploy/books-service-nginx.sh"
$CertbotActivateCommand = "sudo cp '/etc/letsencrypt/live/$VmIp/fullchain.pem' '$RemoteDir/certs/server.crt' && sudo cp '/etc/letsencrypt/live/$VmIp/privkey.pem' '$RemoteDir/certs/server.key' && cd '$RemoteDir' && docker compose exec -T gateway nginx -s reload"

if (Invoke-RemoteOptional $CertbotInstallCommand) {
    if (Invoke-RemoteOptional $CertbotCommand) {
        Invoke-Remote $CertbotInstallHookCommand
        Invoke-Remote $CertbotActivateCommand
        Write-Host "Let's Encrypt certificate installed for $VmIp."
    }
    else {
        Write-Warning "Let's Encrypt certificate request failed. Keeping the generated self-signed certificate."
    }
}
else {
    Write-Warning "Certbot was not available on the VM. Keeping the generated self-signed certificate."
}

Write-Host "Deployment completed."
Write-Host "Health check: https://${VmIp}:$HttpsPort/health"
Write-Host "For Let's Encrypt issuance and renewal, allow inbound TCP 80 to the VM. Allow inbound TCP 443 for service traffic."
Write-Host "If Let's Encrypt was not installed, the generated certificate is self-signed, so use curl -k or trust /opt/books-service/certs/server.crt on the client."
