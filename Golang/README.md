# Books Service

A basic Go backend service for managing authors and books with Gin, pgx, Viper, Uber Fx, DDD-style domain boundaries, and CQRS-oriented application handlers.

## Run

1. Apply the PostgreSQL migrations from `migrations/`.
2. Create a local `.env` file from `.env.example`.
3. Set `BOOKSVC_DATABASE_CONNECTION_STRING`.
   Docker Compose reads `.env` automatically; for direct `go run`, export the variable in your shell.
4. Start the API:

```bash
go run ./cmd/api
```

The API listens on `/api/v1`.

Health check:

```bash
curl http://localhost:8080/health
```

Telemetry:

- the Go service uses OpenTelemetry
- HTTP requests are traced with Gin middleware
- command/query handlers and PostgreSQL repository calls create child spans
- VM deployment sends OTLP traces to an OpenTelemetry Collector
- the Collector exports to Azure Application Insights with the configured connection string

## Docker

Create `.env` first:

```bash
cp .env.example .env
```

For local Docker Compose, set:

```text
POSTGRES_PASSWORD=...
LOCAL_DATABASE_CONNECTION_STRING=postgres://postgres:<password>@postgres:5432/books?sslmode=disable
```

Build and start the service with PostgreSQL:

```bash
docker compose up --build
```

The compose setup:

- builds the Go service with an Alpine Go builder stage
- runs the service from a distroless runtime image
- starts PostgreSQL on `localhost:5432`
- applies migrations with `golang-migrate`
- exposes the API on `localhost:8080`

Check migration state:

```bash
docker compose run --rm migrate version
```

Build only:

```bash
docker build -t books-service:local .
```

Push to Azure Container Registry:

```powershell
.\scripts\push-acr.ps1
```

The script pushes the built image with a timestamp tag and `latest`.

## Secrets

Real connection strings, passwords, and Application Insights connection strings belong in `.env`.

Do not commit `.env`. The repository only keeps `.env.example` with placeholder values.

## Endpoints

- `POST /api/v1/authors`
- `GET /api/v1/authors`
- `GET /api/v1/authors/{id}`
- `PUT /api/v1/authors/{id}`
- `DELETE /api/v1/authors/{id}`
- `GET /api/v1/authors/{id}/books`
- `POST /api/v1/books`
- `GET /api/v1/books`
- `GET /api/v1/books?author_id={authorID}`
- `GET /api/v1/books/{id}`
- `PUT /api/v1/books/{id}`
- `DELETE /api/v1/books/{id}`
- `GET /health`
- `GET /api/v1/health`
