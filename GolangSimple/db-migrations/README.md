# Books Database Migrations

This is an independent migration project for the books service database.

It keeps SQL migrations outside the API runtime/deployment flow and applies them with a small Go CLI that provides:

- migration locking with PostgreSQL advisory locks
- `schema_migrations` version and dirty-state tracking
- statement-level logs
- PostgreSQL `NOTICE` logs for long-running data migrations
- `up`, `down`, `status`, `version`, and `force` commands

## Configure

The migrator reads the first available connection string from:

1. `MIGRATIONS_DATABASE_CONNECTION_STRING`
2. `BOOKSVC_DATABASE_CONNECTION_STRING`
3. `LOCAL_DATABASE_CONNECTION_STRING`

By default, `scripts/migrate.ps1` loads the API repo root `.env` file, so you can reuse the existing deployment configuration.

Set `MIGRATIONS_DATABASE_CONNECTION_STRING` when you want migrations to use an explicit target. If it is not set, the migrator falls back to `BOOKSVC_DATABASE_CONNECTION_STRING`, then `LOCAL_DATABASE_CONNECTION_STRING`.

## Run

From this directory:

```powershell
.\scripts\migrate.ps1 status
.\scripts\migrate.ps1 up
.\scripts\migrate.ps1 version
```

For local Docker Compose, pass the local connection string explicitly if your root `.env` also contains production values:

```powershell
.\scripts\migrate.ps1 up -DatabaseUrl "postgres://postgres:<password>@localhost:5432/books?sslmode=disable"
```

Rollback one migration:

```powershell
.\scripts\migrate.ps1 down -Steps 1
```

If a failed or interrupted migration leaves the database dirty, inspect the database first. Then force only when you know the real state:

```powershell
.\scripts\migrate.ps1 force -Version 2
.\scripts\migrate.ps1 up
```

## Long Data Migrations

Large data migrations should log progress with PostgreSQL `RAISE NOTICE`. The Go migrator prints those notices while the migration is running.
