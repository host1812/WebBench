# Rust Backend Service

Basic REST backend for `authors` and `books` using:

- `axum` for HTTP
- `tokio` for async runtime
- `sqlx` for PostgreSQL access
- `config` + TOML for typed configuration
- layered `domain` / `application` / `infrastructure` / `presentation` modules
- CQRS-style command and query services

## Configuration

The service loads `config/default.toml`, then applies environment overrides. Preferred runtime variables use the `BOOKSVC_*` names below. The older `APP__...` config names are still accepted as fallback overrides.

Create a local `.env` from the template before running Docker Compose:

```powershell
Copy-Item .env.example .env
```

Then edit `.env` and replace placeholder secrets.

PowerShell examples for running without Docker:

```powershell
$env:BOOKSVC_DATABASE_CONNECTION_STRING = "postgres://user:password@localhost:5432/books?sslmode=disable"
$env:BOOKSVC_HTTP_ADDRESS = ":8081"
```

Enable Azure Application Insights export:

```powershell
$env:BOOKSVC_TELEMETRY_ENABLED = "true"
$env:BOOKSVC_TELEMETRY_SERVICE_NAME = "books-service"
$env:BOOKSVC_TELEMETRY_ENVIRONMENT = "production"
$env:APPLICATIONINSIGHTS_CONNECTION_STRING = "InstrumentationKey=...;IngestionEndpoint=..."
```

The implementation uses OpenTelemetry with a community Application Insights exporter. Microsoft does not currently provide an official direct Azure Monitor exporter for Rust.

Supported preferred environment variables:

```env
LOCAL_DATABASE_CONNECTION_STRING=postgres://postgres:postgres@postgres:5432/books?sslmode=disable
BOOKSVC_HTTP_ADDRESS=:8080
BOOKSVC_DATABASE_CONNECTION_STRING=postgres://user:password@host:5432/books?sslmode=require
BOOKSVC_DATABASE_MAX_CONNECTIONS=10
BOOKSVC_TELEMETRY_ENABLED=true
BOOKSVC_TELEMETRY_SERVICE_NAME=books-service
BOOKSVC_TELEMETRY_ENVIRONMENT=azure-vm
BOOKSVC_TELEMETRY_OTLP_ENDPOINT=otel-collector:4318
APPLICATIONINSIGHTS_CONNECTION_STRING=InstrumentationKey=...;IngestionEndpoint=...
```

If both database connection strings are present, `BOOKSVC_DATABASE_CONNECTION_STRING` wins. Local Docker Compose explicitly maps `LOCAL_DATABASE_CONNECTION_STRING` into the API container.

Docker Compose also forwards `APPLICATIONINSIGHTS_CONNECTION_STRING` as `BOOKSVC_TELEMETRY_APPLICATION_INSIGHTS_CONNECTION_STRING` for images that expect the nested `BOOKSVC_TELEMETRY_*` name.

## Endpoints

```text
GET    /health

POST   /api/v1/authors
GET    /api/v1/authors
GET    /api/v1/authors/{author_id}
PUT    /api/v1/authors/{author_id}
DELETE /api/v1/authors/{author_id}
GET    /api/v1/authors/{author_id}/books

POST   /api/v1/books
GET    /api/v1/books
GET    /api/v1/books/{book_id}
PUT    /api/v1/books/{book_id}
DELETE /api/v1/books/{book_id}
```

The original unversioned `/authors` and `/books` routes are still mounted as backward-compatible aliases.

`GET /health` returns basic service health and dependency status:

```json
{
  "service": "rust_backend_service",
  "status": "healthy",
  "checks": {
    "database": {
      "status": "healthy",
      "message": null
    }
  },
  "timestamp": "1970-01-01T00:00:00Z"
}
```

The endpoint returns `200 OK` when all checks are healthy and `503 Service Unavailable` when a dependency check fails.

## Run

```powershell
cargo run
```

The service does not run database migrations. Provide a database that already has the expected schema.

## Docker

Build and start the API plus PostgreSQL:

```powershell
Copy-Item .env.example .env
docker compose up --build
```

The API will be available at:

```text
http://localhost:8080
```

Stop the stack:

```powershell
docker compose down
```

Remove the PostgreSQL volume as well:

```powershell
docker compose down -v
```
