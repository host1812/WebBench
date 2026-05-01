# Books Service

`GolangSimple` is the stripped-down Go version of the authors/books service. It uses the standard library HTTP router, direct `pgxpool` queries, and the existing standalone SQL migration project. There is no framework DI, no custom application layer, and no telemetry pipeline in the request path.

## Run

1. Apply PostgreSQL migrations from `db-migrations/`.
2. Create a local `.env` file from `.env.example`.
3. Set `BOOKSVC_DATABASE_CONNECTION_STRING`.
4. Start the API:

```bash
go run ./cmd/api
```

The API listens on `/api/v1`. For direct `go run`, export `BOOKSVC_DATABASE_CONNECTION_STRING` in your shell first. Docker Compose reads `.env` automatically.

Health check:

```bash
curl http://localhost:8080/health
```

## Docker

Create `.env` first:

```bash
cp .env.example .env
```

For local Docker Compose, set:

```text
POSTGRES_PASSWORD=...
LOCAL_DATABASE_CONNECTION_STRING=postgres://postgres:<password>@postgres:5432/books_simple?sslmode=disable
```

Start PostgreSQL:

```bash
docker compose up -d postgres
```

Apply migrations:

```powershell
cd db-migrations
.\scripts\migrate.ps1 up -DatabaseUrl "postgres://postgres:<password>@localhost:5432/books_simple?sslmode=disable"
cd ..
```

Build and start the API:

```bash
docker compose up --build api
```

The compose setup:

- builds the Go service with an Alpine builder stage
- runs the service from a distroless runtime image
- starts PostgreSQL on `localhost:5432`
- exposes the API on `localhost:8080`

Apply migrations with the separate migration project:

```powershell
cd db-migrations
.\scripts\migrate.ps1 up
.\scripts\migrate.ps1 status
```

Build only:

```bash
docker build -t books-service-golang-simple:local .
```

Build and push to Azure Container Registry:

```powershell
.\scripts\build.ps1
```

The script reads `ACR_LOGIN_SERVER`, `IMAGE_NAME`, and `IMAGE_TAG` from `.env`, then pushes the image with the configured tag and a timestamp tag.

VM deployment:

```powershell
.\scripts\deploy.ps1 -VmIp <vm-public-ip>
```

The deploy script runs the SQL migrations first and then rolls out the VM stack under `/opt/books-service`.

## Secrets

Real connection strings and passwords belong in `.env`.

Do not commit `.env`. The repository only keeps `.env.example` with placeholder values.

## Endpoints

Book list endpoints accept `limit` from `1` to `100000`. The default is `10000`.

- `POST /api/v1/authors`
- `GET /api/v1/authors`
- `GET /api/v1/authors/{id}`
- `PUT /api/v1/authors/{id}`
- `DELETE /api/v1/authors/{id}`
- `GET /api/v1/authors/{id}/books?limit={1-100000}`
- `POST /api/v1/books`
- `GET /api/v1/books?limit={1-100000}`
- `GET /api/v1/books?author_id={authorID}&limit={1-100000}`
- `GET /api/v1/books/{id}`
- `PUT /api/v1/books/{id}`
- `DELETE /api/v1/books/{id}`
- `GET /api/v1/stores`
- `GET /api/v1/stores/{id}`
- `GET /health`
- `GET /api/v1/health`
