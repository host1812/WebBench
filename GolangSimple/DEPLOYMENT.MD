# Azure VM Deployment

The API deployment and database migrations are separate concerns.

The managed PostgreSQL database is outside Docker Compose. The VM runs:

- the API container
- an nginx gateway container that terminates HTTPS

Database migrations live in the independent [db-migrations](C:/Projects/WebBench/GolangSimple/db-migrations/README.md) project and can be run from your workstation against the managed PostgreSQL connection string.

## Files On The VM

The VM only needs:

```text
/opt/books-service-golang-simple/
  .env
  compose.yaml
  nginx.vm.conf
  certs/
    server.crt
    server.key
  certbot-webroot/
```

The deploy script copies these files for you. It also copies your local `.env` file to `/opt/books-service-golang-simple/.env`. It generates `certs/server.crt` and `certs/server.key` on the VM if they do not already exist, then attempts to replace them with a trusted Let's Encrypt certificate.

## Configure `.env`

Create a local `.env` from the committed template:

```powershell
Copy-Item .env.example .env
```

Set these production values before deploying:

```text
BOOKSVC_IMAGE=your-registry.azurecr.io/books-service-golang-simple:latest
BOOKSVC_DATABASE_CONNECTION_STRING=postgres://user:password@your-managed-postgres-host:5432/books_simple?sslmode=require
BOOKSVC_DATABASE_MAX_CONNECTIONS=10
BOOKSVC_SERVICE_NAME=books-service
MIGRATIONS_DATABASE_CONNECTION_STRING=postgres://user:password@your-managed-postgres-host:5432/books_simple?sslmode=require
```

If the PostgreSQL password contains special URL characters, URL-encode it in `BOOKSVC_DATABASE_CONNECTION_STRING`.

Do not commit `.env`.

## Run Migrations

Run migrations from your workstation:

```powershell
cd db-migrations
.\scripts\migrate.ps1 status
.\scripts\migrate.ps1 up
.\scripts\migrate.ps1 version
```

The migration script loads the repo-root `.env` by default. Prefer setting `MIGRATIONS_DATABASE_CONNECTION_STRING`; if it is absent, the migrator falls back to `BOOKSVC_DATABASE_CONNECTION_STRING`.

The migrator tracks state in:

```text
schema_migrations
```

It records:

- the latest applied migration version
- whether the database is in a dirty state

The current migration files are:

```text
001_init.up.sql
001_init.down.sql
002_seed_catalog.up.sql
002_seed_catalog.down.sql
003_seed_more_books.up.sql
003_seed_more_books.down.sql
```

Rollback one migration:

```powershell
.\scripts\migrate.ps1 down -Steps 1
```

If a migration fails halfway, check state:

```powershell
.\scripts\migrate.ps1 status
```

Fix the database manually, then force the version only if you understand the current schema/data state:

```powershell
.\scripts\migrate.ps1 force -Version 2
.\scripts\migrate.ps1 up
```

## Deploy API

From your local machine:

```powershell
.\scripts\deploy.ps1 -VmIp <vm-public-ip>
```

The script:

- stops any existing Docker Compose stack in `/opt/books-service-golang-simple`
- removes and recreates `/opt/books-service-golang-simple`
- removes the old nginx Let's Encrypt renewal hook, if present
- copies `.env` as `/opt/books-service-golang-simple/.env`
- copies `compose.vm.yaml` as `/opt/books-service-golang-simple/compose.yaml`
- copies `nginx.vm.conf`
- creates a self-signed HTTPS certificate if one does not already exist
- runs `docker compose pull`
- starts the HTTPS gateway with `docker compose up -d gateway`
- attempts to install Certbot and request a Let's Encrypt IP-address certificate
- keeps the self-signed certificate if Let's Encrypt cannot be configured

The script logs into Azure on the VM by using the VM managed identity:

```bash
az login --identity
az acr login --name <your-acr-name>
```

The managed identity assigned to the VM must have `AcrPull` permission on the registry.

The ACR name is hardcoded in `scripts/deploy.ps1` as `$AcrName`.

## HTTPS

The VM deployment uses nginx as a TLS gateway:

```text
internet -> nginx:443 -> api:8080
```

The API container is only exposed inside the Docker network. Public traffic should go through HTTPS on port `443`.

For quick verification:

```bash
curl -k https://localhost/health
curl -k https://<vm-public-ip>/health
```

Open inbound TCP `443` in the Azure VM network security group. Open inbound TCP `80` if you want the Let's Encrypt step to succeed, because IP-address certificates are validated through the HTTP challenge path served by nginx.
