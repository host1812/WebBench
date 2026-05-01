# Books Service

`Dotnet.Simple` is the stripped-down .NET version of the authors/books service. It uses a single ASP.NET Core project, direct `Npgsql` access on the request path, embedded SQL migrations, and no application/domain layering.

## Run

1. Create a local `.env` file from `.env.example`.
2. Set `BOOKSVC_DATABASE_CONNECTION_STRING` to your PostgreSQL database.
3. Start the API:

```powershell
dotnet run --project .\src\AuthorsBooks.Api\ serve
```

The service applies migrations on startup. You can also run migrations only:

```powershell
dotnet run --project .\src\AuthorsBooks.Api\ migrate
```

Health check:

```powershell
curl http://localhost:8080/health
```

## Docker

Build the image:

```powershell
docker build -t books-service-dotnet-simple:local .
```

Run migrations inside the image:

```powershell
docker run --rm --env-file .env books-service-dotnet-simple:local migrate
```

Run the API container:

```powershell
docker run --rm -p 8080:8080 --env-file .env books-service-dotnet-simple:local serve
```

## Deployment

Build and push to Azure Container Registry:

```powershell
.\scripts\build.ps1
```

Deploy to a VM:

```powershell
.\scripts\deploy.ps1 -VmIp <vm-public-ip>
```

The deploy script runs the image in `migrate` mode against the configured remote PostgreSQL instance before it starts the VM compose stack.

## Endpoints

Book list endpoints accept `limit` from `1` to `100000`. `take` is still accepted as a backward-compatible alias. The default is `10000`.

- `POST /api/v1/authors`
- `GET /api/v1/authors`
- `GET /api/v1/authors/{id}`
- `PUT /api/v1/authors/{id}`
- `DELETE /api/v1/authors/{id}`
- `GET /api/v1/authors/{id}/books?limit={1-100000}`
- `POST /api/v1/books`
- `GET /api/v1/books?limit={1-100000}`
- `GET /api/v1/books?author_id={authorId}&limit={1-100000}`
- `GET /api/v1/books/{id}`
- `PUT /api/v1/books/{id}`
- `DELETE /api/v1/books/{id}`
- `GET /api/v1/stores`
- `GET /api/v1/stores/{id}`
- `GET /health`
- `GET /api/v1/health`
