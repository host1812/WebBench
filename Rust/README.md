# Rust Backend Service

Basic REST backend for `authors` and `books` using:

- `axum` for HTTP
- `tokio` for async runtime
- `sqlx` for PostgreSQL access and migrations
- `config` + TOML for typed configuration
- layered `domain` / `application` / `infrastructure` / `presentation` modules
- CQRS-style command and query services

## Configuration

The service loads `config/default.toml` and then applies optional environment overrides with the `APP__` prefix.

Create a local `.env` from the template before running Docker Compose:

```powershell
Copy-Item .env.example .env
```

Then edit `.env` and replace placeholder secrets.

PowerShell examples for running without Docker:

```powershell
$env:APP__DATABASE__CONNECTION_STRING = "postgres://user:password@localhost:5432/library"
$env:APP__SERVER__PORT = "8081"
```

Enable Azure Application Insights export:

```powershell
$env:APP__OBSERVABILITY__APPLICATION_INSIGHTS_CONNECTION_STRING = "InstrumentationKey=...;IngestionEndpoint=..."
$env:APP__OBSERVABILITY__ENVIRONMENT = "production"
```

The implementation uses OpenTelemetry with a community Application Insights exporter. Microsoft does not currently provide an official direct Azure Monitor exporter for Rust.

## Endpoints

```text
GET    /health

POST   /authors
GET    /authors
GET    /authors/{author_id}
PUT    /authors/{author_id}
DELETE /authors/{author_id}
GET    /authors/{author_id}/books

POST   /books
GET    /books
GET    /books/{book_id}
PUT    /books/{book_id}
DELETE /books/{book_id}
```

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

Migrations run automatically at startup.

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
