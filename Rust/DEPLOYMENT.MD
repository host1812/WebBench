# Deployment On Azure VM With Docker Compose

This guide assumes you already have an Azure VM, Docker Compose, an image in Azure Container Registry, and a managed PostgreSQL database outside this project. The VM runs only the API and nginx gateway containers. It does not run PostgreSQL and it does not apply migrations.

## 1. VM Directory

Create the app directory on the VM:

```bash
sudo mkdir -p /opt/books-service
sudo chown "$USER":"$USER" /opt/books-service
cd /opt/books-service
```

The deployment directory should contain:

```text
/opt/books-service/
  compose.yaml
  nginx.vm.conf
  .env
  certs/
  certbot-webroot/
```

The Rust source code is not required on the VM when the image already exists in ACR.

## 2. Environment File

Create `/opt/books-service/.env` from [deployment.env.example](deployment.env.example). Do not commit the real `.env`.

```env
BOOKSVC_IMAGE=yourregistry.azurecr.io/books-service-rust:replace-with-version-tag
BOOKSVC_HTTP_ADDRESS=:8080
BOOKSVC_DATABASE_CONNECTION_STRING=postgres://db_user:db_password@your-postgres.postgres.database.azure.com:5432/webbench?sslmode=require
BOOKSVC_DATABASE_MAX_CONNECTIONS=10
BOOKSVC_TELEMETRY_ENABLED=true
BOOKSVC_TELEMETRY_SERVICE_NAME=books-service
BOOKSVC_TELEMETRY_ENVIRONMENT=azure-vm
BOOKSVC_TELEMETRY_OTLP_ENDPOINT=otel-collector:4318
APPLICATIONINSIGHTS_CONNECTION_STRING=InstrumentationKey=replace-me;IngestionEndpoint=https://replace-me.in.applicationinsights.azure.com/
```

Protect it:

```bash
chmod 600 /opt/books-service/.env
```

For local Docker Compose, [docker-compose.yml](docker-compose.yml) maps the API to `LOCAL_DATABASE_CONNECTION_STRING` so it connects to the local PostgreSQL container. For VM deployment, [compose.vm.yaml](compose.vm.yaml) uses `BOOKSVC_DATABASE_CONNECTION_STRING` for managed PostgreSQL.

## 3. Compose Files

Use [compose.vm.yaml](compose.vm.yaml) on the VM as `/opt/books-service/compose.yaml`. It starts:

- `api`: the Rust service, exposed only inside the Docker network.
- `gateway`: nginx on ports `80` and `443` for SSL termination.

The nginx config is [nginx.vm.conf](nginx.vm.conf). The gateway proxies HTTPS traffic to `api:8080`, so keep `BOOKSVC_HTTP_ADDRESS=:8080` unless you also update nginx.

## 4. Deploy With Script

From your local repository root, create a private `.env` with the VM values, then run:

```powershell
.\scripts\deploy-vm.ps1 -VmIp "<vm-public-ip>"
```

The script:

- Requires local `ssh` and `scp`.
- Copies `.env`, `compose.vm.yaml`, and `nginx.vm.conf` to `/opt/books-service`.
- Destroys the existing remote Compose stack first with `docker compose down --remove-orphans --volumes` and removes leftover Compose resources for that project.
- Generates a self-signed certificate before startup.
- Logs into Azure with the VM managed identity.
- Logs into ACR.
- Pulls the configured `BOOKSVC_IMAGE`.
- Starts `gateway`, which starts `api`.
- Tries to replace the self-signed certificate with a Let's Encrypt certificate.

The script does not run migrations.

## 5. Manual Deployment

If deploying manually:

```bash
cd /opt/books-service
az login --identity
az acr login --name yourregistry
docker pull "$BOOKSVC_IMAGE"
docker compose pull
docker compose up -d gateway
docker compose ps
```

Check logs:

```bash
docker compose logs -f api
docker compose logs -f gateway
```

Verify health:

```bash
curl -k https://localhost/health
```

The endpoint returns `200 OK` when the API and database are healthy, and `503 Service Unavailable` when the database check fails.

## 6. Database Schema

This service does not run migrations. Before deploying an API version, make sure the managed PostgreSQL database already has the schema expected by that image.

Current expected tables:

```text
public.authors
  id uuid primary key
  name text not null
  bio text not null
  created_at timestamptz not null
  updated_at timestamptz not null

public.books
  id uuid primary key
  author_id uuid not null references public.authors(id)
  title text not null
  isbn text not null
  published_year int4 null
  created_at timestamptz not null
  updated_at timestamptz not null
```

If the schema changes, update the managed database first using your external schema-management process, then deploy a compatible API image.

## 7. Application Insights

Telemetry is enabled when:

```env
BOOKSVC_TELEMETRY_ENABLED=true
APPLICATIONINSIGHTS_CONNECTION_STRING=InstrumentationKey=...;IngestionEndpoint=...
```

Also set:

```env
BOOKSVC_TELEMETRY_SERVICE_NAME=books-service
BOOKSVC_TELEMETRY_ENVIRONMENT=azure-vm
BOOKSVC_TELEMETRY_OTLP_ENDPOINT=otel-collector:4318
```

Telemetry includes HTTP request spans, CQRS handler spans, repository spans, startup events, health checks, and structured tracing events. `BOOKSVC_TELEMETRY_OTLP_ENDPOINT` is accepted in configuration for compatibility with the deployment environment; the current Rust service exports directly to Application Insights using the configured connection string.

Compose forwards `APPLICATIONINSIGHTS_CONNECTION_STRING` to the API as both `APPLICATIONINSIGHTS_CONNECTION_STRING` and `BOOKSVC_TELEMETRY_APPLICATION_INSIGHTS_CONNECTION_STRING`. This keeps the VM `.env` stable even if an image expects the nested telemetry variable name.

## 8. Normal Release Flow

For each new image version, update `BOOKSVC_IMAGE` in `.env`:

```env
BOOKSVC_IMAGE=yourregistry.azurecr.io/books-service-rust:2026-04-21.1
```

Then redeploy:

```bash
cd /opt/books-service
docker compose pull
docker compose up -d gateway
docker compose logs -f api
```

## 9. Rollback

Set `BOOKSVC_IMAGE` back to the previous image tag:

```env
BOOKSVC_IMAGE=yourregistry.azurecr.io/books-service-rust:previous-tag
```

Then restart:

```bash
cd /opt/books-service
docker compose pull
docker compose up -d gateway
```

Database rollbacks are not automatic. If a schema change is not backward-compatible, prepare a database restore or rollback plan before applying it.

## 10. Stop Or Restart

```bash
cd /opt/books-service
docker compose down
docker compose restart api gateway
```

## 11. Optional systemd Unit

Docker restart policy usually handles VM reboots, but a systemd unit can make startup explicit:

```ini
[Unit]
Description=Books Service
Requires=docker.service
After=docker.service

[Service]
Type=oneshot
WorkingDirectory=/opt/books-service
ExecStart=/usr/bin/docker compose up -d gateway
ExecStop=/usr/bin/docker compose down
RemainAfterExit=yes
TimeoutStartSec=0

[Install]
WantedBy=multi-user.target
```

Enable it:

```bash
sudo systemctl daemon-reload
sudo systemctl enable books-service
sudo systemctl start books-service
sudo systemctl status books-service
```
