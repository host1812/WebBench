Set-StrictMode -Version Latest

function Get-ServiceRepoRoot {
    param([Parameter(Mandatory = $true)][string]$ScriptRoot)

    return (Resolve-Path (Join-Path $ScriptRoot "..")).Path
}

function Import-ServiceDotEnv {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Required .env file was not found at '$Path'."
    }

    foreach ($line in Get-Content -LiteralPath $Path) {
        $trimmed = $line.Trim()
        if (-not $trimmed -or $trimmed.StartsWith("#")) {
            continue
        }

        $separatorIndex = $trimmed.IndexOf("=")
        if ($separatorIndex -lt 1) {
            continue
        }

        $name = $trimmed.Substring(0, $separatorIndex).Trim()
        $value = $trimmed.Substring($separatorIndex + 1).Trim()

        if (($value.StartsWith('"') -and $value.EndsWith('"')) -or ($value.StartsWith("'") -and $value.EndsWith("'"))) {
            $value = $value.Substring(1, $value.Length - 2)
        }

        [Environment]::SetEnvironmentVariable($name, $value, "Process")
    }
}

function Get-ServiceRequiredSetting {
    param([Parameter(Mandatory = $true)][string]$Name)

    $value = [Environment]::GetEnvironmentVariable($Name, "Process")
    if ([string]::IsNullOrWhiteSpace($value)) {
        throw "Required setting '$Name' was not found in the loaded .env file."
    }

    return $value
}

function Get-ServiceOptionalSetting {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$DefaultValue
    )

    $value = [Environment]::GetEnvironmentVariable($Name, "Process")
    if ([string]::IsNullOrWhiteSpace($value)) {
        return $DefaultValue
    }

    return $value
}

function Assert-ServiceCommand {
    param([Parameter(Mandatory = $true)][string]$Name)

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command '$Name' was not found on PATH."
    }
}

function Assert-ServicePath {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Required path '$Path' was not found."
    }
}

function Assert-ServiceLastExitCode {
    param([Parameter(Mandatory = $true)][string]$Action)

    if ($LASTEXITCODE -ne 0) {
        throw "$Action failed with exit code $LASTEXITCODE."
    }
}

function Get-AcrLoginName {
    param([Parameter(Mandatory = $true)][string]$AcrLoginServer)

    if ($AcrLoginServer.EndsWith(".azurecr.io", [System.StringComparison]::OrdinalIgnoreCase)) {
        return $AcrLoginServer.Substring(0, $AcrLoginServer.Length - ".azurecr.io".Length)
    }

    return $AcrLoginServer
}

function Invoke-ServiceBuild {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$ScriptRoot,
        [Parameter(Mandatory = $true)][string]$DefaultImageName,
        [string]$Tag = "local",
        [string[]]$AdditionalRemoteTags = @(),
        [string]$LocalImage,
        [string]$DockerfilePath,
        [string]$BuildContext
    )

    $repoRoot = Get-ServiceRepoRoot -ScriptRoot $ScriptRoot
    $envFile = Join-Path $repoRoot ".env"
    Import-ServiceDotEnv -Path $envFile

    Assert-ServiceCommand "az"
    Assert-ServiceCommand "docker"

    $acrLoginServer = Get-ServiceRequiredSetting "ACR_LOGIN_SERVER"
    $acrLoginName = Get-AcrLoginName -AcrLoginServer $acrLoginServer
    $imageName = Get-ServiceOptionalSetting "IMAGE_NAME" $DefaultImageName
    $imageTag = Get-ServiceOptionalSetting "IMAGE_TAG" "latest"
    $dockerfilePathResolved = if ([string]::IsNullOrWhiteSpace($DockerfilePath)) {
        Get-ServiceOptionalSetting "DOCKERFILE_PATH" "Dockerfile"
    }
    else {
        $DockerfilePath
    }
    $buildContextResolved = if ([string]::IsNullOrWhiteSpace($BuildContext)) {
        Get-ServiceOptionalSetting "BUILD_CONTEXT" "."
    }
    else {
        $BuildContext
    }
    $localImageResolved = if ([string]::IsNullOrWhiteSpace($LocalImage)) {
        "${imageName}:$Tag"
    }
    else {
        $LocalImage
    }

    Push-Location $repoRoot
    try {
        docker build -f $dockerfilePathResolved -t $localImageResolved $buildContextResolved
        Assert-ServiceLastExitCode "docker build"

        az acr login --name $acrLoginName
        Assert-ServiceLastExitCode "az acr login"

        $currentDateTime = Get-Date -Format "yyyyMMddHHmmss"
        $remoteTags = @($imageTag, $currentDateTime) + $AdditionalRemoteTags |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            Select-Object -Unique

        foreach ($remoteTag in $remoteTags) {
            $remoteImage = "$acrLoginServer/${imageName}:$remoteTag"
            docker tag $localImageResolved $remoteImage
            Assert-ServiceLastExitCode "docker tag $remoteImage"

            docker push $remoteImage
            Assert-ServiceLastExitCode "docker push $remoteImage"

            Write-Host "Pushed $remoteImage"
        }
    }
    finally {
        Pop-Location
    }
}

function Invoke-ServiceRemote {
    param(
        [Parameter(Mandatory = $true)][string]$Target,
        [Parameter(Mandatory = $true)][string]$Command
    )

    & ssh $Target $Command
    if ($LASTEXITCODE -ne 0) {
        throw "Remote command failed: $Command"
    }
}

function Invoke-ServiceRemoteOptional {
    param(
        [Parameter(Mandatory = $true)][string]$Target,
        [Parameter(Mandatory = $true)][string]$Command
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

function Copy-ServiceToRemote {
    param(
        [Parameter(Mandatory = $true)][string]$Target,
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Destination
    )

    & scp $Source "${Target}:$Destination"
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to copy '$Source' to '$Destination'."
    }
}

function Invoke-ServiceDeploy {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$ScriptRoot,
        [Parameter(Mandatory = $true)][string]$DefaultImageName,
        [Parameter(Mandatory = $true)][string]$VmIp,
        [Parameter(Mandatory = $true)][string]$DefaultRemoteDir,
        [string]$VmUser,
        [string]$RemoteDir,
        [string]$HttpsPort,
        [switch]$UseGateway,
        [switch]$RunLocalMigrations,
        [switch]$RunImageMigrations,
        [switch]$RunComposeMigrateService,
        [string[]]$AdditionalCleanupRemoteDirs = @()
    )

    $repoRoot = Get-ServiceRepoRoot -ScriptRoot $ScriptRoot
    $envFile = Join-Path $repoRoot ".env"
    $composeFile = Join-Path $repoRoot "compose.vm.yaml"
    $collectorConfigFile = Join-Path $repoRoot "otel-collector-config.yaml"
    $nginxConfigFile = Join-Path (Resolve-Path (Join-Path $repoRoot "..\Infra")).Path "nginx.vm.conf"

    Import-ServiceDotEnv -Path $envFile

    Assert-ServiceCommand "az"
    Assert-ServiceCommand "ssh"
    Assert-ServiceCommand "scp"
    Assert-ServicePath $envFile
    Assert-ServicePath $composeFile

    if ($UseGateway) {
        Assert-ServicePath $nginxConfigFile
    }

    $vmUserResolved = if ([string]::IsNullOrWhiteSpace($VmUser)) {
        Get-ServiceOptionalSetting "VM_USER" "azureuser"
    }
    else {
        $VmUser
    }
    $remoteDirResolved = if ([string]::IsNullOrWhiteSpace($RemoteDir)) {
        Get-ServiceOptionalSetting "REMOTE_DIR" $DefaultRemoteDir
    }
    else {
        $RemoteDir
    }
    $httpsPortResolved = if ([string]::IsNullOrWhiteSpace($HttpsPort)) {
        Get-ServiceOptionalSetting "PUBLIC_HTTPS_PORT" "443"
    }
    else {
        $HttpsPort
    }
    $tlsCertDays = Get-ServiceOptionalSetting "TLS_CERT_DAYS" "365"
    $acrLoginServer = Get-ServiceRequiredSetting "ACR_LOGIN_SERVER"
    $acrLoginName = Get-AcrLoginName -AcrLoginServer $acrLoginServer
    $imageName = Get-ServiceOptionalSetting "IMAGE_NAME" $DefaultImageName
    $imageTag = Get-ServiceOptionalSetting "IMAGE_TAG" "latest"
    $image = "$acrLoginServer/${imageName}:$imageTag"
    $target = "$vmUserResolved@$VmIp"

    if ($RunLocalMigrations) {
        $migrationsScript = Join-Path $repoRoot "db-migrations\scripts\migrate.ps1"
        Assert-ServicePath $migrationsScript

        Write-Host "Running database migrations before deployment..."
        & $migrationsScript up -EnvFile $envFile
        Assert-ServiceLastExitCode "database migration up"

        & $migrationsScript version -EnvFile $envFile
        Assert-ServiceLastExitCode "database migration version"
    }

    $cleanupRemoteDirs = @(
        $remoteDirResolved,
        "/opt/books-service",
        "/opt/authors-books-service"
    ) + $AdditionalCleanupRemoteDirs | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique

    Write-Host "Destroying any existing Compose stack on the VM..."
    foreach ($cleanupRemoteDir in $cleanupRemoteDirs) {
        Invoke-ServiceRemoteOptional -Target $target -Command "if [ -d '$cleanupRemoteDir' ] && [ -f '$cleanupRemoteDir/compose.yaml' ]; then cd '$cleanupRemoteDir' && docker compose down --remove-orphans --volumes; else true; fi" | Out-Null
        Invoke-ServiceRemoteOptional -Target $target -Command "project=`$(basename '$cleanupRemoteDir'); docker ps -aq --filter `"label=com.docker.compose.project=`$project`" | xargs -r docker rm -f; docker network ls -q --filter `"label=com.docker.compose.project=`$project`" | xargs -r docker network rm; docker volume ls -q --filter `"label=com.docker.compose.project=`$project`" | xargs -r docker volume rm -f" | Out-Null
        Invoke-ServiceRemote -Target $target -Command "case '$cleanupRemoteDir' in /opt/*) sudo rm -rf '$cleanupRemoteDir' ;; *) echo 'Refusing to remove unexpected remote directory: $cleanupRemoteDir' >&2; exit 1 ;; esac"
    }

    Invoke-ServiceRemoteOptional -Target $target -Command "sudo rm -f /etc/letsencrypt/renewal-hooks/deploy/books-service-nginx.sh" | Out-Null
    Invoke-ServiceRemote -Target $target -Command "sudo mkdir -p '$remoteDirResolved' && sudo chown -R `$(id -u):`$(id -g) '$remoteDirResolved'"

    Copy-ServiceToRemote -Target $target -Source $envFile -Destination "$remoteDirResolved/.env"
    Copy-ServiceToRemote -Target $target -Source $composeFile -Destination "$remoteDirResolved/compose.yaml"

    if (Test-Path -LiteralPath $collectorConfigFile) {
        Copy-ServiceToRemote -Target $target -Source $collectorConfigFile -Destination "$remoteDirResolved/otel-collector-config.yaml"
    }

    if ($UseGateway) {
        Copy-ServiceToRemote -Target $target -Source $nginxConfigFile -Destination "$remoteDirResolved/nginx.vm.conf"
    }

    Invoke-ServiceRemote -Target $target -Command "chmod 600 '$remoteDirResolved/.env'"

    if ($UseGateway) {
        Invoke-ServiceRemote -Target $target -Command "mkdir -p '$remoteDirResolved/certs' '$remoteDirResolved/certbot-webroot'"
    }
    else {
        Invoke-ServiceRemote -Target $target -Command "mkdir -p '$remoteDirResolved/certs'"
    }

    Invoke-ServiceRemote -Target $target -Command "if ! command -v openssl >/dev/null 2>&1; then sudo apt-get update && sudo apt-get install -y openssl; fi"
    Invoke-ServiceRemote -Target $target -Command "openssl req -x509 -nodes -newkey rsa:4096 -sha256 -days '$tlsCertDays' -keyout '$remoteDirResolved/certs/server.key' -out '$remoteDirResolved/certs/server.crt' -subj '/CN=$VmIp' -addext 'subjectAltName=IP:$VmIp,DNS:localhost'"

    Invoke-ServiceRemote -Target $target -Command "az login --identity"
    Invoke-ServiceRemote -Target $target -Command "az acr login --name '$acrLoginName'"

    if ($RunImageMigrations) {
        Invoke-ServiceRemote -Target $target -Command "docker pull '$image'"
        Write-Host "Applying database migrations with the API image..."
        Invoke-ServiceRemote -Target $target -Command "docker run --rm --env-file '$remoteDirResolved/.env' '$image' migrate"
    }

    Invoke-ServiceRemote -Target $target -Command "cd '$remoteDirResolved' && docker compose --env-file .env pull"

    if ($RunComposeMigrateService) {
        Invoke-ServiceRemote -Target $target -Command "cd '$remoteDirResolved' && docker compose --env-file .env run --rm migrate"
    }

    if ($UseGateway) {
        Write-Host "Starting API container and gateway..."
        Invoke-ServiceRemote -Target $target -Command "cd '$remoteDirResolved' && docker compose --env-file .env up -d gateway && docker compose --env-file .env ps"

        $certbotInstallCommand = "if command -v snap >/dev/null 2>&1; then sudo snap install core || true; sudo snap refresh core || true; sudo snap install --classic certbot || sudo snap refresh certbot || true; sudo ln -sf /snap/bin/certbot /usr/bin/certbot || true; fi; command -v certbot >/dev/null 2>&1"
        $certbotCommand = "sudo certbot certonly --non-interactive --agree-tos --register-unsafely-without-email --preferred-profile shortlived --webroot --webroot-path '$remoteDirResolved/certbot-webroot' --ip-address '$VmIp'"
        $certbotInstallHookCommand = "sudo mkdir -p /etc/letsencrypt/renewal-hooks/deploy && printf '%s\n' '#!/bin/sh' 'set -e' 'cp /etc/letsencrypt/live/$VmIp/fullchain.pem $remoteDirResolved/certs/server.crt' 'cp /etc/letsencrypt/live/$VmIp/privkey.pem $remoteDirResolved/certs/server.key' 'cd $remoteDirResolved' 'docker compose exec -T gateway nginx -s reload || true' | sudo tee /etc/letsencrypt/renewal-hooks/deploy/books-service-nginx.sh >/dev/null && sudo chmod +x /etc/letsencrypt/renewal-hooks/deploy/books-service-nginx.sh"
        $certbotActivateCommand = "sudo cp '/etc/letsencrypt/live/$VmIp/fullchain.pem' '$remoteDirResolved/certs/server.crt' && sudo cp '/etc/letsencrypt/live/$VmIp/privkey.pem' '$remoteDirResolved/certs/server.key' && cd '$remoteDirResolved' && docker compose exec -T gateway nginx -s reload"

        if (Invoke-ServiceRemoteOptional -Target $target -Command $certbotInstallCommand) {
            if (Invoke-ServiceRemoteOptional -Target $target -Command $certbotCommand) {
                Invoke-ServiceRemote -Target $target -Command $certbotInstallHookCommand
                Invoke-ServiceRemote -Target $target -Command $certbotActivateCommand
                Write-Host "Let's Encrypt certificate installed for $VmIp."
            }
            else {
                Write-Warning "Let's Encrypt certificate request failed. Keeping the generated self-signed certificate."
            }
        }
        else {
            Write-Warning "Certbot was not available on the VM. Keeping the generated self-signed certificate."
        }

        Write-Host "For Let's Encrypt issuance and renewal, allow inbound TCP 80 to the VM. Allow inbound TCP 443 for service traffic."
        Write-Host "If Let's Encrypt was not installed, the generated certificate is self-signed, so use curl -k or trust $remoteDirResolved/certs/server.crt on the client."
    }
    else {
        Invoke-ServiceRemote -Target $target -Command "cd '$remoteDirResolved' && docker compose --env-file .env up -d api && docker compose --env-file .env ps"
        Write-Host "The service is terminating TLS directly with a generated self-signed certificate."
        Write-Host "Use curl -k or trust $remoteDirResolved/certs/server.crt on the client."
    }

    Write-Host "Deployment completed."
    Write-Host "Health check: https://${VmIp}:$httpsPortResolved/health"
}
