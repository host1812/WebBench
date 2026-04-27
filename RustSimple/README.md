# rust-simple

Basic Rust backend service for managing `authors` and `books` with PostgreSQL, `axum`, `sqlx`, Docker Compose, and direct HTTPS from the Rust process.

## Stack

- `axum` for HTTP routing
- `axum-server` with Rustls for native HTTPS
- `tokio` for async runtime
- `sqlx` for PostgreSQL access and migrations
- Docker Compose for deployment orchestration

## Configuration

All service and deployment settings live in `.env`. Start from `.env.example` and adjust the values for the target VM and registry:

```env
VM_IP=127.0.0.1
VM_USER=azureuser
REMOTE_DIR=/opt/books-service
ACR_NAME=acrwebbench4sayzpoemqsyo
IMAGE_REGISTRY=acrwebbench4sayzpoemqsyo.azurecr.io
IMAGE_NAME=books-service-rust
IMAGE_TAG=latest
PUBLIC_HTTPS_PORT=443
SERVER_HOST=0.0.0.0
SERVER_PORT=8443
DATABASE_URL=postgres://bookadmin%40your-server:your-password@your-server.postgres.database.azure.com:5432/booksdb?sslmode=require
DATABASE_MAX_CONNECTIONS=10
TLS_CERT_PATH=/app/certs/server.crt
TLS_KEY_PATH=/app/certs/server.key
TLS_CERT_DAYS=365
```

## Build And Test

```bash
cargo test
docker build -t books-service-rust:local .
```

## Deploy

1. Update `.env`.
2. Push the image with `scripts/push-acr.ps1`.
3. Deploy with `scripts/deploy.ps1`.

`deploy.ps1` removes existing Docker Compose workloads on the VM, copies `.env` and `compose.vm.yaml`, generates a self-signed certificate, runs migrations explicitly against the Azure-managed PostgreSQL instance from `DATABASE_URL`, and then starts the HTTPS Rust service.

## Endpoints

- `GET /health`
- `GET /api/v1/authors`
- `POST /api/v1/authors`
- `GET /api/v1/authors/:id`
- `PUT /api/v1/authors/:id`
- `DELETE /api/v1/authors/:id`
- `GET /api/v1/books?limit=10000`
- `POST /api/v1/books`
- `GET /api/v1/books/:id`
- `PUT /api/v1/books/:id`
- `DELETE /api/v1/books/:id`

For `GET /api/v1/books`, `limit` defaults to `10000` and must be between `1` and `100000`.
