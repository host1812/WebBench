# Deployment On Azure VM With Docker Compose

This guide assumes:

- You already have an Azure VM.
- Docker and Docker Compose are installed on the VM.
- The service image already exists in Azure Container Registry.
- PostgreSQL is managed outside this project, for example Azure Database for PostgreSQL.
- The VM can reach the managed PostgreSQL endpoint over the network.

The VM only runs the API container. It does not run PostgreSQL.

## 1. Directory Layout

SSH into the VM and create a dedicated app directory:

```bash
sudo mkdir -p /opt/rust-backend-service
sudo chown "$USER":"$USER" /opt/rust-backend-service
cd /opt/rust-backend-service
```

Place these files there:

```text
/opt/rust-backend-service/
  docker-compose.yml
  .env
```

You do not need to copy the Rust source code to the VM if the image is already in ACR. You can use this repository's `deployment.env.example` as a starting point for the VM `.env`, but never commit or publish the real `.env` file.

## 2. Environment File

Create `/opt/rust-backend-service/.env`:

```bash
nano .env
```

Use this repository's `deployment.env.example` as the template. The final VM `.env` should look like this, with real values filled in:

```env
API_IMAGE=yourregistry.azurecr.io/rust-backend-service:replace-with-version-tag

APP__SERVER__HOST=0.0.0.0
APP__SERVER__PORT=8080
APP__DATABASE__CONNECTION_STRING=postgres://db_user:db_password@your-postgres.postgres.database.azure.com:5432/library?sslmode=require
APP__DATABASE__MAX_CONNECTIONS=10
APP__DATABASE__RUN_MIGRATIONS_ON_STARTUP=false
APP__OBSERVABILITY__SERVICE_NAME=rust_backend_service
APP__OBSERVABILITY__ENVIRONMENT=production
APP__OBSERVABILITY__LOG_FILTER=info,rust_backend_service=debug,tower_http=info
APP__OBSERVABILITY__APPLICATION_INSIGHTS_CONNECTION_STRING=InstrumentationKey=replace-me;IngestionEndpoint=https://replace-me.in.applicationinsights.azure.com/
```

Keep this file private:

```bash
chmod 600 .env
```

Important: `APP__DATABASE__RUN_MIGRATIONS_ON_STARTUP=false` keeps the API from automatically changing the managed database every time the container starts. Migrations are applied explicitly with a one-off command.

## 3. Compose File

Create `/opt/rust-backend-service/docker-compose.yml`:

```bash
nano docker-compose.yml
```

Use this content:

```yaml
services:
  api:
    image: ${API_IMAGE}
    restart: unless-stopped
    command: ["serve"]
    environment:
      APP__SERVER__HOST: ${APP__SERVER__HOST}
      APP__SERVER__PORT: ${APP__SERVER__PORT}
      APP__DATABASE__CONNECTION_STRING: ${APP__DATABASE__CONNECTION_STRING}
      APP__DATABASE__MAX_CONNECTIONS: ${APP__DATABASE__MAX_CONNECTIONS}
      APP__DATABASE__RUN_MIGRATIONS_ON_STARTUP: ${APP__DATABASE__RUN_MIGRATIONS_ON_STARTUP}
      APP__OBSERVABILITY__SERVICE_NAME: ${APP__OBSERVABILITY__SERVICE_NAME}
      APP__OBSERVABILITY__ENVIRONMENT: ${APP__OBSERVABILITY__ENVIRONMENT}
      APP__OBSERVABILITY__LOG_FILTER: ${APP__OBSERVABILITY__LOG_FILTER}
      APP__OBSERVABILITY__APPLICATION_INSIGHTS_CONNECTION_STRING: ${APP__OBSERVABILITY__APPLICATION_INSIGHTS_CONNECTION_STRING}
    ports:
      - "8080:8080"

  migrate:
    image: ${API_IMAGE}
    profiles: ["migration"]
    command: ["migrate"]
    environment:
      APP__DATABASE__CONNECTION_STRING: ${APP__DATABASE__CONNECTION_STRING}
      APP__DATABASE__MAX_CONNECTIONS: ${APP__DATABASE__MAX_CONNECTIONS}
      APP__DATABASE__RUN_MIGRATIONS_ON_STARTUP: false
      APP__OBSERVABILITY__SERVICE_NAME: ${APP__OBSERVABILITY__SERVICE_NAME}
      APP__OBSERVABILITY__ENVIRONMENT: ${APP__OBSERVABILITY__ENVIRONMENT}
      APP__OBSERVABILITY__LOG_FILTER: ${APP__OBSERVABILITY__LOG_FILTER}
      APP__OBSERVABILITY__APPLICATION_INSIGHTS_CONNECTION_STRING: ${APP__OBSERVABILITY__APPLICATION_INSIGHTS_CONNECTION_STRING}
```

The `migrate` service is not started during normal `docker compose up -d` because it is behind the `migration` profile.

## 4. Login To ACR

If the VM has Azure CLI:

```bash
az login
az acr login --name yourregistry
```

If the VM uses a managed identity with ACR pull permission:

```bash
az login --identity
az acr login --name yourregistry
```

Verify image access:

```bash
docker pull "$API_IMAGE"
```

## 5. Apply Migrations

Apply migrations before starting a new API version:

```bash
cd /opt/rust-backend-service
docker compose pull
docker compose --profile migration run --rm migrate
```

This runs the same application image with the `migrate` command. The process connects to the configured managed PostgreSQL database and applies embedded `sqlx` migrations from the image.

If the command succeeds, migrations are applied. If it fails, do not start or update the API until the migration issue is resolved.

## 6. Start The API

Start the API:

```bash
docker compose up -d api
```

Check status:

```bash
docker compose ps
```

Check logs:

```bash
docker compose logs -f api
```

Verify health from the VM:

```bash
curl http://localhost:8080/health
```

Expected healthy response:

```json
{
  "service": "rust_backend_service",
  "status": "healthy",
  "checks": {
    "database": {
      "status": "healthy",
      "message": null
    }
  }
}
```

If the database check is unhealthy, verify:

- The connection string is correct.
- Azure PostgreSQL firewall allows the VM outbound IP.
- The database user has permission to connect.
- SSL mode is correct for Azure PostgreSQL, usually `sslmode=require`.

## 7. Application Insights

The service exports traces to Azure Application Insights when this variable is set:

```env
APP__OBSERVABILITY__APPLICATION_INSIGHTS_CONNECTION_STRING=InstrumentationKey=...;IngestionEndpoint=...
```

Also set:

```env
APP__OBSERVABILITY__SERVICE_NAME=rust_backend_service
APP__OBSERVABILITY__ENVIRONMENT=production
```

Telemetry includes HTTP request spans, CQRS handler spans, repository spans, migration spans, startup events, health checks, and structured tracing events.

Note: Microsoft currently does not provide an official direct Azure Monitor exporter for Rust. This service uses OpenTelemetry with the community `opentelemetry-application-insights` exporter. If you want a Microsoft-supported path, route OpenTelemetry to an OpenTelemetry Collector and export from there using supported Azure Monitor ingestion.

## 8. Normal Deployment Flow

For each new image version:

```bash
cd /opt/rust-backend-service
docker compose pull
docker compose --profile migration run --rm migrate
docker compose up -d api
docker compose logs -f api
```

If you tag images immutably, update `API_IMAGE` in `.env` first:

```env
API_IMAGE=yourregistry.azurecr.io/rust-backend-service:2026-04-20.1
```

Then run the deployment flow above.

## 9. Rollback

Set `API_IMAGE` in `.env` back to the previous image tag:

```env
API_IMAGE=yourregistry.azurecr.io/rust-backend-service:previous-tag
```

Then restart the API:

```bash
docker compose pull api
docker compose up -d api
```

Database rollbacks are not automatic. If a migration is not backward-compatible, prepare a manual rollback migration or restore plan before applying it.

## 10. Stop Or Restart

Stop the API:

```bash
docker compose down
```

Restart the API:

```bash
docker compose restart api
```

## 11. Optional Auto-Start With systemd

Docker restart policy usually handles VM reboots, but a systemd unit can make startup explicit.

Create:

```bash
sudo nano /etc/systemd/system/rust-backend-service.service
```

Content:

```ini
[Unit]
Description=Rust Backend Service
Requires=docker.service
After=docker.service

[Service]
Type=oneshot
WorkingDirectory=/opt/rust-backend-service
ExecStart=/usr/bin/docker compose up -d api
ExecStop=/usr/bin/docker compose down
RemainAfterExit=yes
TimeoutStartSec=0

[Install]
WantedBy=multi-user.target
```

Enable it:

```bash
sudo systemctl daemon-reload
sudo systemctl enable rust-backend-service
sudo systemctl start rust-backend-service
```

Check status:

```bash
sudo systemctl status rust-backend-service
```

## 12. Migration Control Rules

Use these rules for managed PostgreSQL:

- Keep `APP__DATABASE__RUN_MIGRATIONS_ON_STARTUP=false` in the API service.
- Run migrations explicitly with `docker compose --profile migration run --rm migrate`.
- Run migrations before starting the API image that depends on them.
- Use immutable image tags instead of `latest` for predictable deployments.
- Review every migration before applying it to production.
- Make destructive migrations backward-compatible or schedule downtime.
- Keep managed database backups enabled before applying production migrations.
