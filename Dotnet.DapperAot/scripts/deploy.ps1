[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$VmIp,

    [string]$VmUser = "azureuser",

    [string]$RemoteDir = "/opt/books-service-dapper-aot",

    [string]$HttpsPort = "443",

    [string]$AcrName
)

$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "Common.ps1")

function Invoke-Remote {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Target,

        [Parameter(Mandatory = $true)]
        [string]$Command
    )

    & ssh $Target $Command

    if ($LASTEXITCODE -ne 0) {
        throw "Remote command failed: $Command"
    }
}

function Invoke-RemoteOptional {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Target,

        [Parameter(Mandatory = $true)]
        [string]$Command
    )

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
        [Parameter(Mandatory = $true)]
        [string]$Target,

        [Parameter(Mandatory = $true)]
        [string]$Source,

        [Parameter(Mandatory = $true)]
        [string]$Destination
    )

    & scp $Source "${Target}:$Destination"

    if ($LASTEXITCODE -ne 0) {
        throw "Failed to copy '$Source' to '$Destination'."
    }
}

$repoRoot = Get-RepoRoot
$envFile = Join-Path $repoRoot ".env"
$composeFile = Join-Path $repoRoot "compose.vm.yaml"
$collectorConfigFile = Join-Path $repoRoot "otel-collector-config.yaml"
$nginxConfigFile = (Resolve-Path (Join-Path $repoRoot "..\Infra\nginx.vm.conf")).Path

Import-DotEnv -Path $envFile

Assert-Command "az"
Assert-Command "ssh"
Assert-Command "scp"
Assert-Path $composeFile
Assert-Path $collectorConfigFile
Assert-Path $nginxConfigFile

$vmUser = if ($PSBoundParameters.ContainsKey("VmUser")) {
    $VmUser
}
else {
    Get-OptionalSetting "VM_USER" "azureuser"
}
$remoteDir = if ($PSBoundParameters.ContainsKey("RemoteDir")) {
    $RemoteDir
}
else {
    Get-OptionalSetting "REMOTE_DIR" "/opt/books-service-dapper-aot"
}
$acrName = if ($PSBoundParameters.ContainsKey("AcrName") -and -not [string]::IsNullOrWhiteSpace($AcrName)) {
    $AcrName
}
else {
    Get-RequiredSetting "ACR_NAME"
}
$httpsPort = if ($PSBoundParameters.ContainsKey("HttpsPort")) {
    $HttpsPort
}
else {
    "443"
}
$target = "$vmUser@$vmIp"
$cleanupRemoteDirs = @($remoteDir, "/opt/authors-books-service") | Select-Object -Unique

Write-Host "Destroying any existing Compose stack on the VM..."
foreach ($cleanupRemoteDir in $cleanupRemoteDirs) {
    Invoke-RemoteOptional -Target $target -Command "if [ -d '$cleanupRemoteDir' ] && [ -f '$cleanupRemoteDir/compose.yaml' ]; then cd '$cleanupRemoteDir' && docker compose down --remove-orphans --volumes; else true; fi" | Out-Null
    Invoke-RemoteOptional -Target $target -Command "project=`$(basename '$cleanupRemoteDir'); docker ps -aq --filter `"label=com.docker.compose.project=`$project`" | xargs -r docker rm -f; docker network ls -q --filter `"label=com.docker.compose.project=`$project`" | xargs -r docker network rm; docker volume ls -q --filter `"label=com.docker.compose.project=`$project`" | xargs -r docker volume rm -f" | Out-Null
    Invoke-Remote -Target $target -Command "case '$cleanupRemoteDir' in /opt/*) sudo rm -rf '$cleanupRemoteDir' ;; *) echo 'Refusing to remove unexpected remote directory: $cleanupRemoteDir' >&2; exit 1 ;; esac"
}
Invoke-RemoteOptional -Target $target -Command "sudo rm -f /etc/letsencrypt/renewal-hooks/deploy/books-service-nginx.sh" | Out-Null
Invoke-Remote -Target $target -Command "sudo mkdir -p '$remoteDir' && sudo chown -R `$(id -u):`$(id -g) '$remoteDir'"

Copy-ToRemote -Target $target -Source $envFile -Destination "$remoteDir/.env"
Copy-ToRemote -Target $target -Source $composeFile -Destination "$remoteDir/compose.yaml"
Copy-ToRemote -Target $target -Source $collectorConfigFile -Destination "$remoteDir/otel-collector-config.yaml"
Copy-ToRemote -Target $target -Source $nginxConfigFile -Destination "$remoteDir/nginx.vm.conf"

Invoke-Remote -Target $target -Command "chmod 600 '$remoteDir/.env'"
Invoke-Remote -Target $target -Command "mkdir -p '$remoteDir/certs' '$remoteDir/certbot-webroot'"
Invoke-Remote -Target $target -Command "if ! command -v openssl >/dev/null 2>&1; then sudo apt-get update && sudo apt-get install -y openssl; fi"
Invoke-Remote -Target $target -Command "openssl req -x509 -nodes -newkey rsa:4096 -sha256 -days 365 -keyout '$remoteDir/certs/server.key' -out '$remoteDir/certs/server.crt' -subj '/CN=$vmIp' -addext 'subjectAltName=IP:$vmIp,DNS:localhost'"

Invoke-Remote -Target $target -Command "az login --identity"
Invoke-Remote -Target $target -Command "az acr login --name '$acrName'"

Write-Host "Starting API container and applying migrations..."
Invoke-Remote -Target $target -Command "cd '$remoteDir' && docker compose pull && docker compose up -d api"
$remoteDirForShell = $remoteDir.Replace('\', '\\').Replace('"', '\"').Replace('$', '\$')
$apiStartupCheckCommand = @'
cd "__REMOTE_DIR__" &&
sleep 10 &&
apiContainer=$(docker compose ps -q api) &&
if [ -z "$apiContainer" ]; then
  echo 'API container was not created.' >&2
  exit 1
fi &&
apiStatus=$(docker inspect -f '{{.State.Status}}' "$apiContainer") &&
if [ "$apiStatus" != 'running' ]; then
  echo 'API container is not running after startup. Recent logs:' >&2
  docker compose logs --tail 200 api >&2
  exit 1
fi
'@.Replace('__REMOTE_DIR__', $remoteDirForShell)
Invoke-Remote -Target $target -Command $apiStartupCheckCommand

Write-Host "API container is running. Starting gateway..."
Invoke-Remote -Target $target -Command "cd '$remoteDir' && docker compose up -d gateway && docker compose ps"

$certbotInstallCommand = "if command -v snap >/dev/null 2>&1; then sudo snap install core || true; sudo snap refresh core || true; sudo snap install --classic certbot || sudo snap refresh certbot || true; sudo ln -sf /snap/bin/certbot /usr/bin/certbot || true; fi; command -v certbot >/dev/null 2>&1"
$certbotCommand = "sudo certbot certonly --non-interactive --agree-tos --register-unsafely-without-email --preferred-profile shortlived --webroot --webroot-path '$remoteDir/certbot-webroot' --ip-address '$vmIp'"
$certbotInstallHookCommand = "sudo mkdir -p /etc/letsencrypt/renewal-hooks/deploy && printf '%s\n' '#!/bin/sh' 'set -e' 'cp /etc/letsencrypt/live/$vmIp/fullchain.pem $remoteDir/certs/server.crt' 'cp /etc/letsencrypt/live/$vmIp/privkey.pem $remoteDir/certs/server.key' 'cd $remoteDir' 'docker compose exec -T gateway nginx -s reload || true' | sudo tee /etc/letsencrypt/renewal-hooks/deploy/books-service-nginx.sh >/dev/null && sudo chmod +x /etc/letsencrypt/renewal-hooks/deploy/books-service-nginx.sh"
$certbotActivateCommand = "sudo cp '/etc/letsencrypt/live/$vmIp/fullchain.pem' '$remoteDir/certs/server.crt' && sudo cp '/etc/letsencrypt/live/$vmIp/privkey.pem' '$remoteDir/certs/server.key' && cd '$remoteDir' && docker compose exec -T gateway nginx -s reload"

if (Invoke-RemoteOptional -Target $target -Command $certbotInstallCommand) {
    if (Invoke-RemoteOptional -Target $target -Command $certbotCommand) {
        Invoke-Remote -Target $target -Command $certbotInstallHookCommand
        Invoke-Remote -Target $target -Command $certbotActivateCommand
        Write-Host "Let's Encrypt certificate installed for $vmIp."
    }
    else {
        Write-Warning "Let's Encrypt certificate request failed. Keeping the generated self-signed certificate."
    }
}
else {
    Write-Warning "Certbot was not available on the VM. Keeping the generated self-signed certificate."
}

Write-Host "Deployment completed."
Write-Host "Health check: https://${vmIp}:$httpsPort/health"
Write-Host "For Let's Encrypt issuance and renewal, allow inbound TCP 80 to the VM. Allow inbound TCP 443 for service traffic."
Write-Host "If Let's Encrypt was not installed, the generated certificate is self-signed, so use curl -k or trust $remoteDir/certs/server.crt on the client."
